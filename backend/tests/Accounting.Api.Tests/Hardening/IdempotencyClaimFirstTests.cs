using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Accounting.Api.Middleware;
using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Identity;
using Accounting.Application.Master;
using Accounting.Application.Sales;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using Accounting.TestKit;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace Accounting.Api.Tests.Hardening;

/// <summary>
/// WP-3 acceptance tests for specs/fix-idempotency-claim-first.md, written BLIND from the spec
/// alone (acceptance-tester role) — never read the middleware, the store implementation, the
/// migration, or the rewritten Sprint14ExternalApiTests. Test list was committed to the spec's
/// Attempt log BEFORE this file was written. Covers §6 T1–T11.
///
/// Harness: <see cref="IdempotencyApiFactory"/> mirrors the McpApiFactory pattern
/// (McpServerSmokeTests.cs:42) — X-Api-Key header, UseSetting for connection string. A quotation
/// on company 1 (fresh customer per test) is the money-path document under test; the free-text
/// `Notes` field carries a unique marker so DB assertions can count real executions.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class IdempotencyClaimFirstTests
{
    private readonly PostgresFixture _fx;
    public IdempotencyClaimFirstTests(PostgresFixture fx) => _fx = fx;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    // ── Harness ──────────────────────────────────────────────────────────────

    private sealed class ThrowingQuotationDecorator(IQuotationService inner, Func<string?> failureMarker)
        : IQuotationService
    {
        public Task<long> CreateDraftAsync(CreateQuotationRequest req, CancellationToken ct)
        {
            var marker = failureMarker();
            if (marker is not null && req.Notes == marker)
                throw new InvalidOperationException($"T6 forced failure for marker '{marker}'");
            return inner.CreateDraftAsync(req, ct);
        }

        public Task UpdateDraftAsync(long id, CreateQuotationRequest req, CancellationToken ct) =>
            inner.UpdateDraftAsync(id, req, ct);
        public Task DeleteDraftAsync(long id, CancellationToken ct) => inner.DeleteDraftAsync(id, ct);
        public Task SendAsync(long id, CancellationToken ct) => inner.SendAsync(id, ct);
        public Task AcceptAsync(long id, CancellationToken ct) => inner.AcceptAsync(id, ct);
        public Task RejectAsync(long id, string reason, CancellationToken ct) => inner.RejectAsync(id, reason, ct);
        public Task CancelAsync(long id, string reason, CancellationToken ct) => inner.CancelAsync(id, reason, ct);
        public Task<long> ConvertToSalesOrderAsync(long id, CancellationToken ct) =>
            inner.ConvertToSalesOrderAsync(id, ct);
        public Task<IReadOnlyList<QuotationListItem>> ListAsync(string? status, CancellationToken ct,
            DateOnly? dateFrom = null, DateOnly? dateTo = null, long? customerId = null, long? productId = null) =>
            inner.ListAsync(status, ct, dateFrom, dateTo, customerId, productId);
        public Task<QuotationDetail?> GetAsync(long id, CancellationToken ct) => inner.GetAsync(id, ct);
    }

    /// <summary>Copies the McpApiFactory pattern (McpServerSmokeTests.cs:42). Explicit
    /// Frontend:Origin so T9's CORS assertion doesn't depend on Program.cs's fallback default.
    /// The optional IQuotationService decorator swap (T6) replaces whatever was registered,
    /// whether by type or by factory — no assumption about the concrete implementation type
    /// or its registration style, which this dispatch's blind rule forbids reading.</summary>
    private sealed class IdempotencyApiFactory(string connectionString) : WebApplicationFactory<Program>
    {
        public string? FailureMarker;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Postgres", connectionString);
            builder.UseSetting("Database:RunInitializerOnStartup", "false");
            builder.UseSetting("App:BaseUrl", "http://localhost:3000");
            builder.UseSetting("Frontend:Origin", "http://localhost:3000");
            builder.UseSetting("Quartz:Enabled", "false");

            builder.ConfigureTestServices(services =>
            {
                var descriptor = services.Last(d => d.ServiceType == typeof(IQuotationService));
                services.Remove(descriptor);
                services.AddScoped<IQuotationService>(sp =>
                {
                    IQuotationService real = descriptor.ImplementationFactory is not null
                        ? (IQuotationService)descriptor.ImplementationFactory(sp)
                        : (IQuotationService)ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType!);
                    return new ThrowingQuotationDecorator(real, () => FailureMarker);
                });
            });
        }
    }

    private async Task<(string Plaintext, long ApiKeyId)> MintKeyAsync(
        int companyId, int branchId, IReadOnlyList<string> scopes)
    {
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, companyId, branchId);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
        var created = await svc.CreateAsync(new CreateApiKeyRequest(TestIds.Name("idem"), scopes), default);
        return (created.Plaintext, created.ApiKeyId);
    }

    private async Task<long> SeedCustomerAsync(int companyId, int branchId = 1)
    {
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, companyId, branchId);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICustomerService>();
        return await svc.CreateAsync(new CreateCustomerRequest(
            TestIds.CustomerCode(), CustomerType.Corporate, "ลูกค้า Idempotency", null,
            null, null, null, VatRegistered: false, null, null, null, null,
            CreditLimit: 0m, PaymentTermDays: 30, DefaultCurrency: "THB"), default);
    }

    private static string BuildQuotationJson(long customerId, string notes)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var req = new CreateQuotationRequest(
            today, today.AddDays(30), customerId, null, "THB", 1m, notes, null,
            [new ChainLineInput(null, "idempotency test line", 1m, "หน่วย", 100m, 0m, null, null, 0m, null)]);
        return JsonSerializer.Serialize(req, JsonOpts);
    }

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient http, string path, string apiKey, string? idempotencyKey, string? jsonBody)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, path);
        req.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);
        if (idempotencyKey is not null) req.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        if (jsonBody is not null) req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        return await http.SendAsync(req);
    }

    // Raw ADO — deliberately NOT sharing the app's pinned connection/tenant plumbing, since these
    // are pure test-side verification/seeding queries against a fixed, explicit company_id filter,
    // run as the superuser (accounting) connection which bypasses RLS (fixture default).
    private async Task<long> RawCountAsync(string sql, params object[] parameters)
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        for (var i = 0; i < parameters.Length; i++) cmd.Parameters.AddWithValue($"p{i}", parameters[i]);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private sealed record IdemRow(long Id, string RequestHash, int? ResponseStatus, string? ResponseBody, DateTimeOffset ExpiresAt);

    private async Task<IdemRow?> ReadRowAsync(int companyId, long apiKeyId, string key)
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT idempotency_key_id, request_hash, response_status, response_body, expires_at " +
            "FROM sys.idempotency_keys WHERE company_id=@c AND api_key_id=@a AND \"key\"=@k", conn);
        cmd.Parameters.AddWithValue("c", companyId);
        cmd.Parameters.AddWithValue("a", apiKeyId);
        cmd.Parameters.AddWithValue("k", key);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new IdemRow(
            reader.GetInt64(0), reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetInt32(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetFieldValue<DateTimeOffset>(4));
    }

    /// <summary>Raw-SQL seed per §3.1 schema (response_status int? NULL / response_body text NULL /
    /// response_headers jsonb NULL) — schema shape taken from the spec text itself, never from the
    /// migration file (forbidden to read).</summary>
    private async Task<long> SeedRowAsync(
        int companyId, long apiKeyId, string key, string requestHash,
        int? responseStatus, string? responseBody, string? responseHeaders,
        DateTimeOffset createdAt, DateTimeOffset expiresAt)
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO sys.idempotency_keys
                (company_id, api_key_id, "key", request_hash, response_status, response_body,
                 response_headers, created_at, expires_at)
            VALUES (@company, @apikey, @key, @hash, @status, @body, @headers, @created, @expires)
            RETURNING idempotency_key_id
            """, conn);
        cmd.Parameters.Add(new NpgsqlParameter("company", NpgsqlDbType.Integer) { Value = companyId });
        cmd.Parameters.Add(new NpgsqlParameter("apikey", NpgsqlDbType.Bigint) { Value = apiKeyId });
        cmd.Parameters.Add(new NpgsqlParameter("key", NpgsqlDbType.Text) { Value = key });
        cmd.Parameters.Add(new NpgsqlParameter("hash", NpgsqlDbType.Text) { Value = requestHash });
        cmd.Parameters.Add(new NpgsqlParameter("status", NpgsqlDbType.Integer) { Value = (object?)responseStatus ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("body", NpgsqlDbType.Text) { Value = (object?)responseBody ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("headers", NpgsqlDbType.Jsonb) { Value = (object?)responseHeaders ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("created", NpgsqlDbType.TimestampTz) { Value = createdAt });
        cmd.Parameters.Add(new NpgsqlParameter("expires", NpgsqlDbType.TimestampTz) { Value = expiresAt });
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    // ── T1 — I1/I3: storm-create, same key + same body ──────────────────────

    [SkippableFact]
    public async Task Storm_create_same_key_same_body_yields_exactly_one_document()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var customerId = await SeedCustomerAsync(1);
        var (apiKey, _) = await MintKeyAsync(1, 1, ["sales.quotation.create"]);
        await using var factory = new IdempotencyApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();

        var marker = $"t1-{Guid.NewGuid():N}";
        var key = $"t1key-{Guid.NewGuid():N}";
        var body = BuildQuotationJson(customerId, marker);
        var sw = Stopwatch.StartNew();

        var tasks = Enumerable.Range(0, 20).Select(async _ =>
        {
            var t0 = sw.Elapsed;
            var resp = await PostAsync(http, "/api/v1/quotations", apiKey, key, body);
            var respBody = await resp.Content.ReadAsStringAsync();
            var replayed = resp.Headers.TryGetValues("Idempotency-Replayed", out var v) && v.Contains("true");
            return (Status: (int)resp.StatusCode, Body: respBody, Replayed: replayed, Elapsed: sw.Elapsed - t0);
        }).ToArray();
        var results = await Task.WhenAll(tasks);

        results.Where(r => r.Status >= 500).Should().BeEmpty("zero 5xx expected under contention");
        results.Where(r => r.Status == 409).Should().BeEmpty(
            "if any 409 in_progress leaks through, the 2s WaitFor was exceeded — elapsed: " +
            string.Join(", ", results.Select(r => $"{r.Elapsed.TotalMilliseconds:F0}ms")));
        results.Should().OnlyContain(r => r.Status == 201, "every response must be 201 (winner or replay)");
        results.Select(r => r.Body).Distinct().Should().HaveCount(1,
            "every response body must be byte-identical — replays reproduce the winner's exact bytes");
        results.Count(r => r.Replayed).Should().BeGreaterThanOrEqualTo(19,
            "at most one request is the true winner; the rest must be marked replayed");

        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, 1, 1);
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        (await db.Quotations.CountAsync(q => q.Notes == marker)).Should().Be(1,
            "CRITICAL-01: exactly one business execution despite 20 concurrent identical requests");
    }

    // ── T2 — I1/I2(ii)/HIGH-01: storm-transition on the 204 endpoint ────────

    [SkippableFact]
    public async Task Storm_send_same_key_yields_single_transition_and_null_body_record()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var customerId = await SeedCustomerAsync(1);
        var (apiKey, apiKeyId) = await MintKeyAsync(1, 1, ["sales.quotation.create", "sales.quotation.send"]);
        await using var factory = new IdempotencyApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();

        var marker = $"t2-{Guid.NewGuid():N}";
        var createResp = await PostAsync(http, "/api/v1/quotations", apiKey,
            $"t2create-{Guid.NewGuid():N}", BuildQuotationJson(customerId, marker));
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var quotationId = long.Parse(createResp.Headers.Location!.ToString().Split('/').Last());

        var sendKey = $"t2send-{Guid.NewGuid():N}";
        var tasks = Enumerable.Range(0, 20)
            .Select(_ => PostAsync(http, $"/api/v1/quotations/{quotationId}/send", apiKey, sendKey, null))
            .ToArray();
        var responses = await Task.WhenAll(tasks);

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.NoContent, "all 20 must resolve to 204");
        var replayedCount = responses.Count(r =>
            r.Headers.TryGetValues("Idempotency-Replayed", out var v) && v.Contains("true"));
        replayedCount.Should().BeGreaterThanOrEqualTo(19);

        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, 1, 1);
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var status = await db.Quotations.Where(q => q.QuotationId == quotationId).Select(q => q.Status).SingleAsync();
        status.Should().Be(QuotationStatus.Sent);

        var row = await ReadRowAsync(1, apiKeyId, sendKey);
        row.Should().NotBeNull();
        row!.ResponseStatus.Should().Be(204);
        row.ResponseBody.Should().BeNull("HIGH-01: a 204's empty body must persist as NULL text, not fail jsonb NOT NULL");
    }

    // ── T3 — I1/I2: concurrent same key, two different bodies ───────────────

    [SkippableFact]
    public async Task Concurrent_same_key_different_bodies_one_execution_rest_409_mismatch()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var customerId = await SeedCustomerAsync(1);
        var (apiKey, _) = await MintKeyAsync(1, 1, ["sales.quotation.create"]);
        await using var factory = new IdempotencyApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();

        var key = $"t3-{Guid.NewGuid():N}";
        var markerA = $"t3a-{Guid.NewGuid():N}";
        var markerB = $"t3b-{Guid.NewGuid():N}";
        var bodyA = BuildQuotationJson(customerId, markerA);
        var bodyB = BuildQuotationJson(customerId, markerB);

        var tasks = new List<Task<HttpResponseMessage>>();
        for (var i = 0; i < 10; i++) tasks.Add(PostAsync(http, "/api/v1/quotations", apiKey, key, bodyA));
        for (var i = 0; i < 10; i++) tasks.Add(PostAsync(http, "/api/v1/quotations", apiKey, key, bodyB));
        var responses = await Task.WhenAll(tasks);

        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, 1, 1);
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var countA = await db.Quotations.CountAsync(q => q.Notes == markerA);
        var countB = await db.Quotations.CountAsync(q => q.Notes == markerB);
        (countA + countB).Should().Be(1, "exactly one execution across the two competing bodies");
        var winnerIsA = countA == 1;

        var winnerResponses = new List<HttpResponseMessage>();
        var loserResponses = new List<HttpResponseMessage>();
        for (var i = 0; i < 20; i++)
            (i < 10 == winnerIsA ? winnerResponses : loserResponses).Add(responses[i]);

        winnerResponses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created);
        var winnerBodies = new List<string>();
        foreach (var r in winnerResponses) winnerBodies.Add(await r.Content.ReadAsStringAsync());
        winnerBodies.Distinct().Should().HaveCount(1, "the winner group's responses must be byte-identical replays");

        loserResponses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Conflict);
        foreach (var r in loserResponses)
        {
            using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
            doc.RootElement.GetProperty("error").GetProperty("code").GetString()
                .Should().Be("idempotency.body_mismatch");
        }
    }

    // ── T4 — §3.4 key contract ────────────────────────────────────────────

    public static IEnumerable<object[]> InvalidKeyHttpCases()
    {
        yield return new object[] { new string('a', 129), "idempotency.invalid_key" };
        yield return new object[] { "abc def", "idempotency.invalid_key" };
        yield return new object[] { "", "idempotency.required" };
    }

    [SkippableTheory]
    [MemberData(nameof(InvalidKeyHttpCases))]
    public async Task InvalidKey_http_rejects_before_execution(string keyValue, string expectedCode)
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var customerId = await SeedCustomerAsync(1);
        var (apiKey, apiKeyId) = await MintKeyAsync(1, 1, ["sales.quotation.create"]);
        await using var factory = new IdempotencyApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();

        var marker = $"t4-{Guid.NewGuid():N}";
        var resp = await PostAsync(http, "/api/v1/quotations", apiKey, keyValue, BuildQuotationJson(customerId, marker));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be(expectedCode);

        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, 1, 1);
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        (await db.Quotations.CountAsync(q => q.Notes == marker)).Should().Be(0,
            "an invalid/missing key must be rejected BEFORE execution");

        (await RawCountAsync(
            "SELECT COUNT(*) FROM sys.idempotency_keys WHERE company_id=@p0 AND api_key_id=@p1 AND \"key\"=@p2",
            1, apiKeyId, keyValue)).Should().Be(0, "an invalid key must never be persisted");
    }

    [Theory]
    [InlineData("ก", false)]
    [InlineData("a\tb", false)]
    [InlineData("", false)]
    public void IsValidKey_rejects_non_ascii_control_and_empty(string key, bool expected) =>
        IdempotencyMiddleware.IsValidKey(key).Should().Be(expected);

    [Fact]
    public void IsValidKey_accepts_128_chars_rejects_129()
    {
        IdempotencyMiddleware.IsValidKey(new string('a', 128)).Should().BeTrue();
        IdempotencyMiddleware.IsValidKey(new string('a', 129)).Should().BeFalse();
    }

    // ── T5 — I6 on the 204 path ──────────────────────────────────────────

    [SkippableFact]
    public async Task Replay_of_204_has_no_content_type_and_empty_body()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var customerId = await SeedCustomerAsync(1);
        var (apiKey, _) = await MintKeyAsync(1, 1, ["sales.quotation.create", "sales.quotation.send"]);
        await using var factory = new IdempotencyApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();

        var marker = $"t5-{Guid.NewGuid():N}";
        var createResp = await PostAsync(http, "/api/v1/quotations", apiKey,
            $"t5create-{Guid.NewGuid():N}", BuildQuotationJson(customerId, marker));
        var quotationId = long.Parse(createResp.Headers.Location!.ToString().Split('/').Last());

        var sendKey = $"t5send-{Guid.NewGuid():N}";
        var first = await PostAsync(http, $"/api/v1/quotations/{quotationId}/send", apiKey, sendKey, null);
        first.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var replay = await PostAsync(http, $"/api/v1/quotations/{quotationId}/send", apiKey, sendKey, null);
        replay.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (replay.Headers.TryGetValues("Idempotency-Replayed", out var v) && v.Contains("true")).Should().BeTrue();
        (await replay.Content.ReadAsByteArrayAsync()).Should().BeEmpty("a replayed 204 must have an empty body");
        replay.Content.Headers.Contains("Content-Type").Should().BeFalse("no Content-Type on a bodyless 204 replay");
    }

    // ── T6 — I3/I4: failure releases the claim ──────────────────────────

    [SkippableFact]
    public async Task Failed_execution_releases_claim_and_retry_executes()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var customerId = await SeedCustomerAsync(1);
        var (apiKey, apiKeyId) = await MintKeyAsync(1, 1, ["sales.quotation.create"]);
        await using var factory = new IdempotencyApiFactory(_fx.ConnectionString) { FailureMarker = "boom" };
        using var http = factory.CreateClient();

        var key = $"t6-{Guid.NewGuid():N}";
        HttpResponseMessage? resp1 = null;
        try
        {
            resp1 = await PostAsync(http, "/api/v1/quotations", apiKey, key, BuildQuotationJson(customerId, "boom"));
        }
        catch (HttpRequestException)
        {
            // TestServer may surface an unhandled exception as a transport failure instead of a
            // translated 500 response — either way the claim must still have been released.
        }
        if (resp1 is not null)
            ((int)resp1.StatusCode).Should().Be(500, "the decorator throws for the boom marker");

        (await RawCountAsync(
            "SELECT COUNT(*) FROM sys.idempotency_keys WHERE company_id=@p0 AND api_key_id=@p1 AND \"key\"=@p2",
            1, apiKeyId, key)).Should().Be(0, "I4: a 5xx/exception must leave NO claim row behind");

        await using (var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, 1, 1))
        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
            (await db.Quotations.CountAsync(q => q.Notes == "boom")).Should().Be(0,
                "the decorator throws before the inner service ever runs — nothing may be committed");
        }

        factory.FailureMarker = null;
        var resp2 = await PostAsync(http, "/api/v1/quotations", apiKey, key, BuildQuotationJson(customerId, "retry-ok"));
        resp2.StatusCode.Should().Be(HttpStatusCode.Created, "the released claim must let a retry execute");
    }

    [SkippableFact]
    public async Task CompleteAsync_with_invalid_jsonb_propagates_not_swallowed()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (_, apiKeyId) = await MintKeyAsync(1, 1, ["sales.quotation.create"]);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, 1, 1);
        await using var scope = sp.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IIdempotencyStore>();

        var key = $"t6c-{Guid.NewGuid():N}";
        var claim = await store.ClaimAsync(1, apiKeyId, key, "hash", DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(5), CancellationToken.None);
        claim.Outcome.Should().Be(ClaimOutcome.Claimed);

        var act = async () => await store.CompleteAsync(
            claim.ClaimId!.Value, 200, "{}", "not-valid-json{{{", CancellationToken.None);
        await act.Should().ThrowAsync<Exception>(
            "I5: a persistence error (malformed jsonb) must propagate, never be reinterpreted as contention");
    }

    // ── T7 — I6 on the 201 create path ───────────────────────────────────

    [SkippableFact]
    public async Task Replay_of_201_preserves_location_and_content_type_byte_equal()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var customerId = await SeedCustomerAsync(1);
        var (apiKey, _) = await MintKeyAsync(1, 1, ["sales.quotation.create"]);
        await using var factory = new IdempotencyApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();

        var marker = $"t7-{Guid.NewGuid():N}";
        var key = $"t7-{Guid.NewGuid():N}";
        var body = BuildQuotationJson(customerId, marker);

        var first = await PostAsync(http, "/api/v1/quotations", apiKey, key, body);
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var firstLocation = first.Headers.Location!.ToString();
        var firstContentType = first.Content.Headers.GetValues("Content-Type").Single();

        var replay = await PostAsync(http, "/api/v1/quotations", apiKey, key, body);
        replay.StatusCode.Should().Be(HttpStatusCode.Created);
        (replay.Headers.TryGetValues("Idempotency-Replayed", out var v) && v.Contains("true")).Should().BeTrue();
        replay.Headers.Location!.ToString().Should().Be(firstLocation, "I6: Location must replay byte-equal");
        replay.Content.Headers.GetValues("Content-Type").Single().Should().Be(firstContentType,
            "I6: Content-Type must replay byte-equal");
    }

    // ── T8 — §3.2 stale takeover (delete + re-insert) ────────────────────

    [SkippableFact]
    public async Task StaleTakeover_stale_processing_row_is_replaced_by_fresh_execution()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var customerId = await SeedCustomerAsync(1);
        var (apiKey, apiKeyId) = await MintKeyAsync(1, 1, ["sales.quotation.create"]);
        await using var factory = new IdempotencyApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();

        var key = $"t8a-{Guid.NewGuid():N}";
        var marker = $"t8a-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        var seededId = await SeedRowAsync(1, apiKeyId, key, "fake-hash-a", null, null, null,
            now.AddMinutes(-10), now.AddHours(24));

        var resp = await PostAsync(http, "/api/v1/quotations", apiKey, key, BuildQuotationJson(customerId, marker));
        resp.StatusCode.Should().Be(HttpStatusCode.Created, "a stale (>5min) PROCESSING row must be taken over");

        var row = await ReadRowAsync(1, apiKeyId, key);
        row.Should().NotBeNull();
        row!.Id.Should().NotBe(seededId, "takeover must delete+re-insert — a NEW idempotency_key_id (I9)");
        row.ResponseStatus.Should().Be(201);

        (await RawCountAsync("SELECT COUNT(*) FROM sys.idempotency_keys WHERE idempotency_key_id=@p0", seededId))
            .Should().Be(0, "the seeded stale row must no longer exist after takeover");
    }

    [SkippableFact]
    public async Task StaleTakeover_expired_completed_row_is_replaced_with_new_id_and_hash()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var customerId = await SeedCustomerAsync(1);
        var (apiKey, apiKeyId) = await MintKeyAsync(1, 1, ["sales.quotation.create"]);
        await using var factory = new IdempotencyApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();

        var key = $"t8c-{Guid.NewGuid():N}";
        var marker = $"t8c-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        var seededId = await SeedRowAsync(1, apiKeyId, key, "fake-hash-c", 200, "{}", "{}",
            now.AddHours(-25), now.AddHours(-1));

        var resp = await PostAsync(http, "/api/v1/quotations", apiKey, key, BuildQuotationJson(customerId, marker));
        resp.StatusCode.Should().Be(HttpStatusCode.Created, "an EXPIRED completed row must be taken over, not replayed");

        var row = await ReadRowAsync(1, apiKeyId, key);
        row.Should().NotBeNull();
        row!.Id.Should().NotBe(seededId);
        row.RequestHash.Should().NotBe("fake-hash-c", "the surviving row must carry the NEW request's real hash");
        row.ExpiresAt.Should().BeAfter(now, "the surviving row's expiry is the new 24h window, not the stale one");
    }

    [SkippableFact]
    public async Task StaleTakeover_fresh_processing_row_yields_in_progress_then_409_after_wait()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var customerId = await SeedCustomerAsync(1);
        var (apiKey, apiKeyId) = await MintKeyAsync(1, 1, ["sales.quotation.create"]);
        await using var factory = new IdempotencyApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();

        var marker = $"t8b-{Guid.NewGuid():N}";
        var body = BuildQuotationJson(customerId, marker);

        // Probe: one real round-trip to learn the REAL request_hash for this exact body — the
        // blind rule forbids reverse-engineering the hash's exact encoding from the middleware, so
        // it is obtained empirically instead of guessed.
        var probeKey = $"t8bprobe-{Guid.NewGuid():N}";
        var probeResp = await PostAsync(http, "/api/v1/quotations", apiKey, probeKey, body);
        probeResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var probeRow = await ReadRowAsync(1, apiKeyId, probeKey);
        probeRow.Should().NotBeNull();
        var realHash = probeRow!.RequestHash;

        var testKey = $"t8btest-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        await SeedRowAsync(1, apiKeyId, testKey, realHash, null, null, null, now, now.AddHours(24));

        var sw = Stopwatch.StartNew();
        var resp = await PostAsync(http, "/api/v1/quotations", apiKey, testKey, body);
        sw.Stop();

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "a fresh (non-stale) in-progress claim must yield 409, never execute concurrently");
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("idempotency.in_progress");
        resp.Headers.TryGetValues("Retry-After", out var ra).Should().BeTrue();
        ra!.Single().Should().Be("1");
        sw.Elapsed.Should().BeCloseTo(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1.5),
            "the wait loop polls for up to WaitFor≈2s before giving up (D3)");
    }

    // ── T11 — §3.2/§3.6: takeover cannot let a stale owner clobber the new claim ──

    [SkippableFact]
    public async Task Takeover_stale_owner_cannot_clobber_new_claim()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (_, apiKeyId) = await MintKeyAsync(1, 1, ["sales.quotation.create"]);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, 1, 1);
        await using var scope = sp.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IIdempotencyStore>();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();

        var key = $"t11-{Guid.NewGuid():N}";
        var hash = $"hash-{Guid.NewGuid():N}";
        var staleAfter = TimeSpan.FromMinutes(5);

        var claimA = await store.ClaimAsync(1, apiKeyId, key, hash, DateTimeOffset.UtcNow, staleAfter, CancellationToken.None);
        claimA.Outcome.Should().Be(ClaimOutcome.Claimed);
        var idA = claimA.ClaimId!.Value;

        await db.Database.ExecuteSqlRawAsync(
            "UPDATE sys.idempotency_keys SET created_at = now() - interval '10 minutes' WHERE idempotency_key_id = {0}", idA);

        var claimB = await store.ClaimAsync(1, apiKeyId, key, hash, DateTimeOffset.UtcNow, staleAfter, CancellationToken.None);
        claimB.Outcome.Should().Be(ClaimOutcome.Claimed);
        var idB = claimB.ClaimId!.Value;
        idB.Should().NotBe(idA, "the takeover must delete+re-insert — a genuinely new claim token (I9)");

        var completeA = await store.CompleteAsync(idA, 201, "{\"a\":1}", "{}", CancellationToken.None);
        completeA.Should().Be(0, "the stale owner's id no longer exists — Complete must affect 0 rows, never idB's row");

        var rowAfterA = await ReadRowAsync(1, apiKeyId, key);
        rowAfterA.Should().NotBeNull();
        rowAfterA!.ResponseStatus.Should().BeNull("idB's claim must still be live — A's Complete must not have touched it");

        await store.ReleaseAsync(idA, CancellationToken.None);
        var rowAfterRelease = await ReadRowAsync(1, apiKeyId, key);
        rowAfterRelease.Should().NotBeNull("the stale owner's Release must not delete the NEW owner's (idB) row (I9)");
        rowAfterRelease!.Id.Should().Be(idB);

        var completeB = await store.CompleteAsync(idB, 201, "{\"b\":1}", "{}", CancellationToken.None);
        completeB.Should().Be(1, "the real (new) owner's Complete must succeed");

        var claimThird = await store.ClaimAsync(1, apiKeyId, key, hash, DateTimeOffset.UtcNow, staleAfter, CancellationToken.None);
        claimThird.Outcome.Should().Be(ClaimOutcome.Completed);
        claimThird.Record!.ResponseBody.Should().Be("{\"b\":1}");
    }

    // ── T9 — I8: CORS preflight allows Idempotency-Key ───────────────────

    [SkippableFact]
    public async Task Cors_preflight_allows_idempotency_key_header()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var factory = new IdempotencyApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();

        var req = new HttpRequestMessage(HttpMethod.Options, "/api/v1/quotations");
        req.Headers.TryAddWithoutValidation("Origin", "http://localhost:3000");
        req.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "POST");
        req.Headers.TryAddWithoutValidation("Access-Control-Request-Headers", "X-Api-Key, Content-Type, Idempotency-Key");

        var resp = await http.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent, "a CORS preflight is answered 204 by the CORS middleware");
        resp.Headers.TryGetValues("Access-Control-Allow-Headers", out var allowed).Should().BeTrue();
        allowed!.SelectMany(v => v.Split(',').Select(s => s.Trim()))
            .Should().Contain("Idempotency-Key", "I8: the CORS typo fix must expose the REAL header name");
    }

    // ── T10 — H1: RLS-scoped claim under NOBYPASSRLS ─────────────────────
    // Uses the pg_database_owner idiom from ApiKeyResolverRlsTests.cs (an allowed harness file),
    // NOT PostgresFixture.RlsTestRole (teas_rls_test) — the latter needs CREATEROLE on the test
    // connection's role, which this environment's `accounting` user does NOT have (verified:
    // rolcreaterole=false), so it Skips via RlsRoleSkip and never actually runs. pg_database_owner
    // is a built-in role needing no CREATEROLE — membership is implicit for the DB owner — so this
    // variant actually executes here instead of silently skipping the promise under test.
    [SkippableFact]
    public async Task Rls_claim_scoped_to_pinned_company_under_nobypassrls()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        var coA = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var coB = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var (_, apiKeyIdA) = await MintKeyAsync(coA.CompanyId, coA.BranchId, ["sales.quotation.create"]);
        var (_, apiKeyIdB) = await MintKeyAsync(coB.CompanyId, coB.BranchId, ["sales.quotation.create"]);

        var key = $"rls-{Guid.NewGuid():N}";
        var hash = $"hash-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        var staleAfter = TimeSpan.FromMinutes(5);

        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, coA.CompanyId, coA.BranchId);
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var store = scope.ServiceProvider.GetRequiredService<IIdempotencyStore>();

        await db.Database.OpenConnectionAsync();
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "GRANT USAGE ON SCHEMA sys TO pg_database_owner; " +
                "GRANT SELECT, INSERT, UPDATE, DELETE ON sys.idempotency_keys TO pg_database_owner; " +
                "GRANT USAGE, SELECT ON SEQUENCE sys.idempotency_keys_idempotency_key_id_seq TO pg_database_owner;");

            await db.Database.ExecuteSqlRawAsync(
                "SELECT set_config('app.company_id', {0}, false)", coA.CompanyId.ToString());
            await db.Database.ExecuteSqlRawAsync("SET ROLE pg_database_owner");

            var claimA = await store.ClaimAsync(coA.CompanyId, apiKeyIdA, key, hash, now, staleAfter, CancellationToken.None);
            claimA.Outcome.Should().Be(ClaimOutcome.Claimed, "the raw claim INSERT must be RLS-safe under the pinned company (H1)");
            (await store.CompleteAsync(claimA.ClaimId!.Value, 201, "{}", "{}", CancellationToken.None)).Should().Be(1);
            var replay = await store.ClaimAsync(coA.CompanyId, apiKeyIdA, key, hash, now, staleAfter, CancellationToken.None);
            replay.Outcome.Should().Be(ClaimOutcome.Completed, "claim/complete/replay round-trips under NOBYPASSRLS");

            await db.Database.ExecuteSqlRawAsync(
                "SELECT set_config('app.company_id', {0}, false)", coB.CompanyId.ToString());
            var claimB = await store.ClaimAsync(coB.CompanyId, apiKeyIdB, key, hash, now, staleAfter, CancellationToken.None);
            claimB.Outcome.Should().Be(ClaimOutcome.Claimed, "the SAME key text under a different company is a separate row, not a conflict");
            claimB.ClaimId.Should().NotBe(claimA.ClaimId);

            var conn = (System.Data.Common.DbConnection)db.Database.GetDbConnection();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT company_id FROM sys.idempotency_keys WHERE \"key\" = @k";
            var p = cmd.CreateParameter();
            p.ParameterName = "k";
            p.Value = key;
            cmd.Parameters.Add(p);
            await using var reader = await cmd.ExecuteReaderAsync();
            var seenCompanies = new List<int>();
            while (await reader.ReadAsync()) seenCompanies.Add(reader.GetInt32(0));
            seenCompanies.Should().Equal([coB.CompanyId],
                "pinned as company B, a company-agnostic SELECT must see ONLY company B's row — RLS-invisible, not merely param-scoped");
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("RESET ROLE");
            await db.Database.CloseConnectionAsync();
        }
    }
}
