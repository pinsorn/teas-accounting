using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Audit;
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
/// WP-3 acceptance tests for specs/fix-idempotency-document-fence.md, written BLIND from the
/// spec's §0, §1, §3.1, §3.2, §3.6, §3.9-J4, §4 and §5 alone (acceptance-tester role) — never
/// opened QuotationChainServices.cs, ReceiptService.cs, TaxInvoiceService.cs,
/// IdempotencyMiddleware.cs or IdempotencyFenceLock.cs, and never ran a diff/log on this branch.
/// Covers §4 T-F1, T-F1b, T-F2, T-J3..T-J9.
///
/// Harness copied from IdempotencyClaimFirstTests.cs (IdempotencyApiFactory + the
/// descriptor-swap decorator pattern, the T11 claim back-dating idiom, and the
/// pg_database_owner RLS leg) per the dispatch's explicit permission to reuse it.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class IdempotencyDocumentFenceTests
{
    private readonly PostgresFixture _fx;
    public IdempotencyDocumentFenceTests(PostgresFixture fx) => _fx = fx;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    // ── Harness — decorators ─────────────────────────────────────────────────

    /// <summary>Pauses CreateDraftAsync BEFORE delegating to the real service, but only for the
    /// FIRST call whose Notes matches <see cref="PauseMarker"/> — a same-key/same-body retry
    /// (T-F1b's "B") must run straight through once "A" has already been paused, or the test
    /// would deadlock both callers on the same gate. NOTE: the "already consumed" guard MUST live
    /// on the factory (via <paramref name="tryConsume"/>), not as an instance field on this
    /// decorator — ASP.NET creates a NEW scoped instance of this decorator per HTTP request, so an
    /// instance field resets to unconsumed for every request and cannot serialise across them.</summary>
    private sealed class PausingQuotationDecorator(
        IQuotationService inner,
        Func<(string? Marker, TaskCompletionSource<bool>? Gate, TaskCompletionSource<bool>? Reached, Func<bool> TryConsume)> state)
        : IQuotationService
    {
        public async Task<long> CreateDraftAsync(CreateQuotationRequest req, CancellationToken ct)
        {
            var (marker, gate, reached, tryConsume) = state();
            if (marker is not null && gate is not null && req.Notes == marker && tryConsume())
            {
                // The claim INSERT already ran in IdempotencyMiddleware BEFORE the endpoint, so
                // reaching this pause proves the pausing owner has a LIVE claim row and is parked
                // ahead of the fence. Signal it so the test can back-date that row deterministically
                // (see F1b's cold-start-race comment) instead of guessing with a fixed delay.
                reached?.TrySetResult(true);
                await gate.Task;
            }
            return await inner.CreateDraftAsync(req, ct);
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

    /// <summary>Throws exactly once from CompleteAsync (T-F2's "crash after commit") — the flag
    /// is consumed on first use so a subsequent retry's Complete succeeds normally.</summary>
    private sealed class ThrowOnceCompleteDecorator(IIdempotencyStore inner, Func<bool> shouldThrow, Action consume)
        : IIdempotencyStore
    {
        public Task<ClaimResult> ClaimAsync(int companyId, long apiKeyId, string key, string requestHash,
            DateTimeOffset now, TimeSpan staleAfter, CancellationToken ct) =>
            inner.ClaimAsync(companyId, apiKeyId, key, requestHash, now, staleAfter, ct);

        public Task<int> CompleteAsync(long claimId, int status, string? body, string? headersJson, CancellationToken ct)
        {
            if (shouldThrow())
            {
                consume();
                throw new InvalidOperationException("T-F2 forced CompleteAsync failure (crash-after-commit)");
            }
            return inner.CompleteAsync(claimId, status, body, headersJson, ct);
        }

        public Task ReleaseAsync(long claimId, CancellationToken ct) => inner.ReleaseAsync(claimId, ct);
        public Task<int> PurgeExpiredAsync(DateTimeOffset now, CancellationToken ct) => inner.PurgeExpiredAsync(now, ct);
    }

    /// <summary>Throws from Record for entityType=="Quotation" && action=="Created" while armed
    /// (T-J5a's atomicity leg). Record never sees the request body/Notes, per spec §4 T-J5(a).</summary>
    private sealed class ThrowingActivityRecorderDecorator(IActivityRecorder inner, Func<bool> armed) : IActivityRecorder
    {
        public void Record(string entityType, long entityId, string? docNo, int companyId, string action,
            string? fromStatus = null, string? toStatus = null, string? note = null, string module = "sales")
        {
            if (armed() && entityType == "Quotation" && action == "Created")
                throw new InvalidOperationException("T-J5a forced ActivityRecorder failure");
            inner.Record(entityType, entityId, docNo, companyId, action, fromStatus, toStatus, note, module);
        }
    }

    /// <summary>One factory for the whole file: every decorator is always installed but is a
    /// no-op pass-through unless its toggle is set for a given test — mirrors the
    /// IdempotencyApiFactory.FailureMarker pattern in IdempotencyClaimFirstTests.cs.</summary>
    private sealed class DocumentFenceApiFactory(string connectionString) : WebApplicationFactory<Program>
    {
        public bool ThrowOnNextComplete;
        public bool ThrowOnQuotationCreatedActivity;
        public string? PauseMarker;
        public TaskCompletionSource<bool>? PauseGate;
        public TaskCompletionSource<bool>? PauseReachedSignal;
        private int _pauseConsumed;

        /// <summary>Global (factory-lifetime) "first caller wins" latch for the pause gate — see
        /// the deadlock note on <see cref="PausingQuotationDecorator"/>.</summary>
        private bool TryConsumePause() => Interlocked.Exchange(ref _pauseConsumed, 1) == 0;

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
                var quotationDescriptor = services.Last(d => d.ServiceType == typeof(IQuotationService));
                services.Remove(quotationDescriptor);
                services.AddScoped<IQuotationService>(sp =>
                {
                    IQuotationService real = quotationDescriptor.ImplementationFactory is not null
                        ? (IQuotationService)quotationDescriptor.ImplementationFactory(sp)
                        : (IQuotationService)ActivatorUtilities.CreateInstance(sp, quotationDescriptor.ImplementationType!);
                    return new PausingQuotationDecorator(real, () => (PauseMarker, PauseGate, PauseReachedSignal, TryConsumePause));
                });

                var storeDescriptor = services.Last(d => d.ServiceType == typeof(IIdempotencyStore));
                services.Remove(storeDescriptor);
                services.AddScoped<IIdempotencyStore>(sp =>
                {
                    IIdempotencyStore real = storeDescriptor.ImplementationFactory is not null
                        ? (IIdempotencyStore)storeDescriptor.ImplementationFactory(sp)
                        : (IIdempotencyStore)ActivatorUtilities.CreateInstance(sp, storeDescriptor.ImplementationType!);
                    return new ThrowOnceCompleteDecorator(real, () => ThrowOnNextComplete, () => ThrowOnNextComplete = false);
                });

                var activityDescriptor = services.Last(d => d.ServiceType == typeof(IActivityRecorder));
                services.Remove(activityDescriptor);
                services.AddScoped<IActivityRecorder>(sp =>
                {
                    IActivityRecorder real = activityDescriptor.ImplementationFactory is not null
                        ? (IActivityRecorder)activityDescriptor.ImplementationFactory(sp)
                        : (IActivityRecorder)ActivatorUtilities.CreateInstance(sp, activityDescriptor.ImplementationType!);
                    return new ThrowingActivityRecorderDecorator(real, () => ThrowOnQuotationCreatedActivity);
                });
            });
        }
    }

    // ── Harness — helpers ────────────────────────────────────────────────────

    private async Task<(string Plaintext, long ApiKeyId)> MintKeyAsync(
        int companyId, int branchId, IReadOnlyList<string> scopes)
    {
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, companyId, branchId);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
        var created = await svc.CreateAsync(new CreateApiKeyRequest(TestIds.Name("idemdoc"), scopes), default);
        return (created.Plaintext, created.ApiKeyId);
    }

    private async Task<long> SeedCustomerAsync(int companyId, int branchId = 1)
    {
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, companyId, branchId);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICustomerService>();
        return await svc.CreateAsync(new CreateCustomerRequest(
            TestIds.CustomerCode(), CustomerType.Corporate, "ลูกค้า Idempotency Fence", null,
            null, null, null, VatRegistered: false, null, null, null, null,
            CreditLimit: 0m, PaymentTermDays: 30, DefaultCurrency: "THB"), default);
    }

    /// <summary>Tax invoices/receipts need a VAT-registered company + customer (spec dispatch).</summary>
    private Task<TestCompanyFactory.SeededCompany> SeedVatCompanyAsync() =>
        TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);

    private static string BuildQuotationJson(long customerId, string notes)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var req = new CreateQuotationRequest(
            today, today.AddDays(30), customerId, null, "THB", 1m, notes, null,
            [new ChainLineInput(null, "idempotency fence line", 1m, "หน่วย", 100m, 0m, null, null, 0m, null)]);
        return JsonSerializer.Serialize(req, JsonOpts);
    }

    private static string BuildTaxInvoiceJson(long customerId, string notes)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var req = new CreateTaxInvoiceRequest(
            today, customerId, false, "THB", 1m, notes, null, null,
            [new TaxInvoiceLineInput(null, null, "idempotency fence ti line", 1m, 1, "หน่วย", 1000m, 0m, 1, "VAT7", 0.07m)]);
        return JsonSerializer.Serialize(req, JsonOpts);
    }

    private static string BuildReceiptJson(long customerId, string notes)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var req = new CreateReceiptRequest(
            today, customerId, PaymentMethod.Cash, null, null, null, "THB", 1m, notes,
            Applications: [], Lines: [new ReceiptLineInput("idempotency fence receipt line", 1m, 100m, 100m)]);
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

    /// <summary>Swallows a transport-level failure (TestServer can surface an unhandled server
    /// exception as HttpRequestException instead of a translated 5xx response — see T6's comment
    /// in IdempotencyClaimFirstTests.cs) so a caller can treat null as "A got some flavour of 5xx".</summary>
    private static async Task<HttpResponseMessage?> SafePostAsync(
        HttpClient http, string path, string apiKey, string? idempotencyKey, string? jsonBody)
    {
        try { return await PostAsync(http, path, apiKey, idempotencyKey, jsonBody); }
        catch (HttpRequestException) { return null; }
    }

    private static long ExtractId(HttpResponseMessage resp) =>
        long.Parse(resp.Headers.Location!.ToString().Split('/').Last());

    private async Task<int> ExecuteRawAsync(string sql, Action<NpgsqlCommand> bind)
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        bind(cmd);
        return await cmd.ExecuteNonQueryAsync();
    }

    private Task<int> DeleteClaimRowAsync(int companyId, long apiKeyId, string key) =>
        ExecuteRawAsync(
            "DELETE FROM sys.idempotency_keys WHERE company_id=@c AND api_key_id=@a AND \"key\"=@k",
            cmd =>
            {
                cmd.Parameters.AddWithValue("c", companyId);
                cmd.Parameters.AddWithValue("a", apiKeyId);
                cmd.Parameters.AddWithValue("k", key);
            });

    private Task<int> BackdateClaimAsync(int companyId, long apiKeyId, string key, TimeSpan by) =>
        ExecuteRawAsync(
            "UPDATE sys.idempotency_keys SET created_at = now() - @iv WHERE company_id=@c AND api_key_id=@a AND \"key\"=@k",
            cmd =>
            {
                cmd.Parameters.Add(new NpgsqlParameter("iv", NpgsqlDbType.Interval) { Value = by });
                cmd.Parameters.AddWithValue("c", companyId);
                cmd.Parameters.AddWithValue("a", apiKeyId);
                cmd.Parameters.AddWithValue("k", key);
            });

    private async Task<(bool Exists, int? ResponseStatus)> ReadClaimStatusAsync(int companyId, long apiKeyId, string key)
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT response_status FROM sys.idempotency_keys WHERE company_id=@c AND api_key_id=@a AND \"key\"=@k", conn);
        cmd.Parameters.AddWithValue("c", companyId);
        cmd.Parameters.AddWithValue("a", apiKeyId);
        cmd.Parameters.AddWithValue("k", key);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return (false, null);
        return (true, reader.IsDBNull(0) ? null : reader.GetInt32(0));
    }

    // ── Harness — cross-type parametrization for T-F2/T-J8 (scope extension: J1/J2/J2b cover
    // each fenced type, but only quotations have ever executed keyed in any environment) ──────

    public enum DocType { Quotation, TaxInvoice, Receipt }

    private static string RouteFor(DocType t) => t switch
    {
        DocType.Quotation => "/api/v1/quotations",
        DocType.TaxInvoice => "/api/v1/tax-invoices",
        DocType.Receipt => "/api/v1/receipts",
        _ => throw new ArgumentOutOfRangeException(nameof(t)),
    };

    private static string CreateScopeFor(DocType t) => t switch
    {
        DocType.Quotation => "sales.quotation.create",
        DocType.TaxInvoice => "sales.tax_invoice.create",
        DocType.Receipt => "sales.receipt.create",
        _ => throw new ArgumentOutOfRangeException(nameof(t)),
    };

    /// <summary>Quotations use the shared company 1 (matching the rest of this file / the
    /// claim-first harness); tax invoices and receipts need a fresh VAT-registered company
    /// (spec dispatch).</summary>
    private async Task<(int CompanyId, int BranchId, long CustomerId)> SetupTenantAsync(DocType t)
    {
        if (t == DocType.Quotation)
        {
            var customerId = await SeedCustomerAsync(1);
            return (1, 1, customerId);
        }
        var co = await SeedVatCompanyAsync();
        return (co.CompanyId, co.BranchId, co.CustomerId);
    }

    private static string BuildBody(DocType t, long customerId, string notes) => t switch
    {
        DocType.Quotation => BuildQuotationJson(customerId, notes),
        DocType.TaxInvoice => BuildTaxInvoiceJson(customerId, notes),
        DocType.Receipt => BuildReceiptJson(customerId, notes),
        _ => throw new ArgumentOutOfRangeException(nameof(t)),
    };

    private static Task<int> CountByMarkerAsync(DocType t, AccountingDbContext db, string marker) => t switch
    {
        DocType.Quotation => db.Quotations.CountAsync(q => q.Notes == marker),
        DocType.TaxInvoice => db.TaxInvoices.CountAsync(x => x.Notes == marker),
        DocType.Receipt => db.Receipts.CountAsync(x => x.Notes == marker),
        _ => throw new ArgumentOutOfRangeException(nameof(t)),
    };

    // ── T-F1 (Codex; real F1 interleaving, external advisory-lock harness, no production seam) ──

    [SkippableFact]
    public async Task F1_External_lock_holder_serialises_two_stale_takeover_owners_onto_one_document()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var customerId = await SeedCustomerAsync(1);
        var (apiKey, apiKeyId) = await MintKeyAsync(1, 1, ["sales.quotation.create"]);
        await using var factory = new DocumentFenceApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();

        var marker = $"f1-{Guid.NewGuid():N}";
        var key = $"f1key-{Guid.NewGuid():N}";
        var body = BuildQuotationJson(customerId, marker);

        await using var lockConn = new NpgsqlConnection(_fx.ConnectionString);
        await lockConn.OpenAsync();
        var lockTx = await lockConn.BeginTransactionAsync();

        Task<HttpResponseMessage?> taskA = null!;
        Task<HttpResponseMessage?> taskB = null!;
        try
        {
            await using (var lockCmd = new NpgsqlCommand("SELECT pg_advisory_xact_lock(@company, @lock)", lockConn, lockTx))
            {
                lockCmd.Parameters.Add(new NpgsqlParameter("company", NpgsqlDbType.Integer) { Value = 1 });
                lockCmd.Parameters.Add(new NpgsqlParameter("lock", NpgsqlDbType.Integer)
                { Value = IdempotencyFenceLock.LockKey(apiKeyId, key) });
                await lockCmd.ExecuteNonQueryAsync();
            }

            // (2) Request A claims and must block inside the service on the SAME lock.
            taskA = SafePostAsync(http, "/api/v1/quotations", apiKey, key, body);
            await Task.WhenAny(taskA, Task.Delay(500));
            taskA.IsCompleted.Should().BeFalse("A must be blocked on the advisory lock before any work happens");

            // (3) Back-date A's claim past StaleAfter (T11 pattern).
            await BackdateClaimAsync(1, apiKeyId, key, TimeSpan.FromMinutes(10));

            // (4) Request B takes the now-stale claim over and must ALSO block on the SAME lock —
            // two live owners at once is exactly the F1 window this fence must close.
            taskB = SafePostAsync(http, "/api/v1/quotations", apiKey, key, body);
            await Task.WhenAny(taskB, Task.Delay(500));
            taskB.IsCompleted.Should().BeFalse("B must ALSO be blocked on the same lock");
        }
        finally
        {
            // (5) Always release the external lock, pass or fail, so the shared DB isn't left locked.
            await lockTx.CommitAsync();
            await lockTx.DisposeAsync();
            await lockConn.CloseAsync();
        }

        // (6) Await both with a timeout. Either arrival order converges (spec).
        await Task.WhenAll(taskA, taskB).WaitAsync(TimeSpan.FromSeconds(20));
        var respA = await taskA;
        var respB = await taskB;

        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, 1, 1);
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        (await db.Quotations.CountAsync(q => q.Notes == marker)).Should().Be(1,
            "J1: exactly one document must exist regardless of which owner won the lock");

        var twoXxIds = new List<long>();
        if (respA is not null && (int)respA.StatusCode is >= 200 and < 300)
            twoXxIds.Add(ExtractId(respA));
        else
            (respA is null ? 599 : (int)respA.StatusCode).Should().BeGreaterThanOrEqualTo(500,
                "spec §4 T-F1 tolerates ONLY a 5xx/transport-failure from A, never anything else");

        respB.Should().NotBeNull("B must not suffer a transport failure — it must converge cleanly");
        ((int)respB!.StatusCode).Should().BeInRange(200, 299,
            "B must succeed with a 2xx — the spec's tolerated 5xx applies to A only");
        twoXxIds.Add(ExtractId(respB));

        twoXxIds.Distinct().Should().HaveCount(1, "J2: every 2xx response across A and B must carry the SAME document id");
    }

    // ── T-F1b (optional, cheap) — late owner after a full takeover: convergence, not the F1 race ──
    // The earlier skip blamed a "harness-only" B→409. Root cause (opus-debugger 2026-09-05): the
    // old harness fired A, waited a fixed Task.Delay(500), then asserted only taskA.IsCompleted ==
    // false before back-dating A's claim. That assertion is satisfied by A merely being SLOW (the
    // first request to a cold WebApplicationFactory pays JIT + DI graph build + Npgsql warmup +
    // deliberately-slow API-key hashing — easily >500ms), NOT by A having claimed and parked. In
    // that window A had not yet inserted its claim row, so BackdateClaimAsync hit 0 rows and B
    // raced A for the SAME key: whichever request reached the pipeline first claimed and (Notes ==
    // marker) consumed the pause latch. Two race outcomes: a spurious B→409 in_progress (A claims
    // first; the tester saw this), or an outright deadlock (B wins the latch and parks holding the
    // only claim while the test awaits B; the diagnostic run reproduced this). The back-date hit 0
    // rows — there was no stale row, so no takeover occurred; the probe's "fresh claim nobody
    // serviced" is consistent with a live claim, not a takeover artifact. IdempotencyStore /
    // IdempotencyMiddleware were never at fault.
    // Fix is deterministic + test-only: PauseReachedSignal fires from the decorator only
    // AFTER the pausing owner has claimed (the claim INSERT precedes the endpoint), so awaiting it
    // guarantees A's claim row exists before the back-date. No product change — IdempotencyStore /
    // IdempotencyMiddleware are correct; the takeover path is exercised here and in T-F1.
    [SkippableFact]
    public async Task F1b_Late_owner_after_full_takeover_converges_on_existing_document()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var customerId = await SeedCustomerAsync(1);
        var (apiKey, apiKeyId) = await MintKeyAsync(1, 1, ["sales.quotation.create"]);
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reached = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var marker = $"f1b-{Guid.NewGuid():N}";
        var key = $"f1bkey-{Guid.NewGuid():N}";
        var body = BuildQuotationJson(customerId, marker);

        await using var factory = new DocumentFenceApiFactory(_fx.ConnectionString)
        { PauseMarker = marker, PauseGate = gate, PauseReachedSignal = reached };
        using var http = factory.CreateClient();

        var taskA = SafePostAsync(http, "/api/v1/quotations", apiKey, key, body);
        // Wait until A has genuinely CLAIMED and parked at the interface pause — not merely until
        // some fixed delay elapsed (which could not distinguish "parked" from "still cold-starting"
        // and let the back-date below hit 0 rows). A is the only in-flight request here, so it is
        // necessarily the pause-latch owner.
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
        taskA.IsCompleted.Should().BeFalse("A must be paused BEFORE it ever enters the fence (interface-level pause)");

        (await BackdateClaimAsync(1, apiKeyId, key, TimeSpan.FromMinutes(10)))
            .Should().Be(1, "A's live claim row must exist to be back-dated — guards against the cold-start race");

        var respB = await PostAsync(http, "/api/v1/quotations", apiKey, key, body);
        ((int)respB.StatusCode).Should().BeInRange(200, 299, "B must run to completion cleanly — A hasn't touched the fence yet");
        var idX = ExtractId(respB);

        gate.SetResult(true);
        await Task.WhenAny(taskA, Task.Delay(TimeSpan.FromSeconds(15)));
        var respA = await taskA;

        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, 1, 1);
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        (await db.Quotations.CountAsync(q => q.Notes == marker)).Should().Be(1,
            "only ONE document despite the late-released owner entering the fence after B already committed");

        if (respA is not null && (int)respA.StatusCode is >= 200 and < 300)
            ExtractId(respA).Should().Be(idX, "a converging A must return the SAME id as B");
        else if (respA is not null)
            ((int)respA.StatusCode).Should().BeGreaterThanOrEqualTo(500, "spec tolerates a 5xx from the late-released A, never a second document");
    }

    // ── T-F2 (Codex) — crash-after-commit: Complete throws once. Run for ALL THREE fenced
    // types (scope extension) — J1/J2 (§3.6) cover each type, and only quotations have ever
    // executed keyed in any environment before this fence existed. ──────────────────────────

    [SkippableTheory]
    [InlineData(DocType.Quotation)]
    [InlineData(DocType.TaxInvoice)]
    [InlineData(DocType.Receipt)]
    public async Task F2_Complete_throw_leaves_claim_processing_and_retry_converges(DocType docType)
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (companyId, branchId, customerId) = await SetupTenantAsync(docType);
        var (apiKey, apiKeyId) = await MintKeyAsync(companyId, branchId, [CreateScopeFor(docType)]);
        await using var factory = new DocumentFenceApiFactory(_fx.ConnectionString) { ThrowOnNextComplete = true };
        using var http = factory.CreateClient();
        var route = RouteFor(docType);

        var marker = $"f2-{docType}-{Guid.NewGuid():N}";
        var key = $"f2key-{docType}-{Guid.NewGuid():N}";
        var body = BuildBody(docType, customerId, marker);

        var first = await PostAsync(http, route, apiKey, key, body);
        first.StatusCode.Should().Be(HttpStatusCode.Created, "§3.4: a Complete throw must still emit the fresh 201");
        var idX = ExtractId(first);

        var (exists, responseStatus) = await ReadClaimStatusAsync(companyId, apiKeyId, key);
        exists.Should().BeTrue("the claim row must still exist after a Complete throw");
        responseStatus.Should().BeNull("§3.4: Complete-failure policy is UNCHANGED — the claim stays PROCESSING, never Released");

        await BackdateClaimAsync(companyId, apiKeyId, key, TimeSpan.FromMinutes(10));

        var retry = await PostAsync(http, route, apiKey, key, body);
        ((int)retry.StatusCode).Should().BeInRange(200, 299, "a fresh execution must converge, not error");
        ExtractId(retry).Should().Be(idX, "J2: the retry must return the SAME document id");

        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, companyId, branchId);
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        (await CountByMarkerAsync(docType, db, marker)).Should().Be(1,
            "J1: still exactly one document despite the Complete throw + stale retry");
    }

    // ── T-J3 — index truth (schema), no raw INSERT ───────────────────────────

    [SkippableFact]
    public async Task J3_Unique_index_rejects_a_forced_duplicate_tuple()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var customerId = await SeedCustomerAsync(1);
        var (apiKey, _) = await MintKeyAsync(1, 1, ["sales.quotation.create"]);
        await using var factory = new DocumentFenceApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();

        var key1 = $"j3a-{Guid.NewGuid():N}";
        var key2 = $"j3b-{Guid.NewGuid():N}";
        var resp1 = await PostAsync(http, "/api/v1/quotations", apiKey, key1, BuildQuotationJson(customerId, $"j3a-{Guid.NewGuid():N}"));
        var resp2 = await PostAsync(http, "/api/v1/quotations", apiKey, key2, BuildQuotationJson(customerId, $"j3b-{Guid.NewGuid():N}"));
        resp1.StatusCode.Should().Be(HttpStatusCode.Created);
        resp2.StatusCode.Should().Be(HttpStatusCode.Created);
        var id2 = ExtractId(resp2);

        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "UPDATE sales.quotations SET idempotency_key = @k1 WHERE quotation_id = @id2", conn);
        cmd.Parameters.AddWithValue("k1", key1);
        cmd.Parameters.AddWithValue("id2", id2);

        var act = async () => await cmd.ExecuteNonQueryAsync();
        var ex = await act.Should().ThrowAsync<PostgresException>(
            "the partial unique index must reject a forced duplicate (company, api_key, key) tuple");
        ex.Which.SqlState.Should().Be("23505");
        ex.Which.ConstraintName.Should().Be("ux_quotations_idem");
    }

    // ── T-J4 — claim-row loss, run for ALL THREE fenced types ────────────────

    [SkippableFact]
    public async Task J4_Quotation_claim_row_loss_still_converges()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var customerId = await SeedCustomerAsync(1);
        var (apiKey, apiKeyId) = await MintKeyAsync(1, 1, ["sales.quotation.create"]);
        await using var factory = new DocumentFenceApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();

        var marker = $"j4q-{Guid.NewGuid():N}";
        var key = $"j4qkey-{Guid.NewGuid():N}";
        var body = BuildQuotationJson(customerId, marker);

        var first = await PostAsync(http, "/api/v1/quotations", apiKey, key, body);
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var idX = ExtractId(first);

        (await DeleteClaimRowAsync(1, apiKeyId, key)).Should().Be(1, "the claim row must exist to delete");

        var retry = await PostAsync(http, "/api/v1/quotations", apiKey, key, body);
        retry.StatusCode.Should().Be(HttpStatusCode.Created);
        ExtractId(retry).Should().Be(idX, "J1/J2: claim-row loss must still converge on the SAME document");

        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, 1, 1);
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        (await db.Quotations.CountAsync(q => q.Notes == marker)).Should().Be(1);
    }

    [SkippableFact]
    public async Task J4_TaxInvoice_claim_row_loss_still_converges()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await SeedVatCompanyAsync();
        var (apiKey, apiKeyId) = await MintKeyAsync(co.CompanyId, co.BranchId, ["sales.tax_invoice.create"]);
        await using var factory = new DocumentFenceApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();

        var marker = $"j4ti-{Guid.NewGuid():N}";
        var key = $"j4tikey-{Guid.NewGuid():N}";
        var body = BuildTaxInvoiceJson(co.CustomerId, marker);

        var first = await PostAsync(http, "/api/v1/tax-invoices", apiKey, key, body);
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var idX = ExtractId(first);

        (await DeleteClaimRowAsync(co.CompanyId, apiKeyId, key)).Should().Be(1);

        var retry = await PostAsync(http, "/api/v1/tax-invoices", apiKey, key, body);
        retry.StatusCode.Should().Be(HttpStatusCode.Created);
        ExtractId(retry).Should().Be(idX);

        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        (await db.TaxInvoices.CountAsync(t => t.Notes == marker)).Should().Be(1);
    }

    [SkippableFact]
    public async Task J4_Receipt_claim_row_loss_still_converges()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await SeedVatCompanyAsync();
        var (apiKey, apiKeyId) = await MintKeyAsync(co.CompanyId, co.BranchId, ["sales.receipt.create"]);
        await using var factory = new DocumentFenceApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();

        var marker = $"j4rc-{Guid.NewGuid():N}";
        var key = $"j4rckey-{Guid.NewGuid():N}";
        var body = BuildReceiptJson(co.CustomerId, marker);

        var first = await PostAsync(http, "/api/v1/receipts", apiKey, key, body);
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var idX = ExtractId(first);

        (await DeleteClaimRowAsync(co.CompanyId, apiKeyId, key)).Should().Be(1);

        var retry = await PostAsync(http, "/api/v1/receipts", apiKey, key, body);
        retry.StatusCode.Should().Be(HttpStatusCode.Created);
        ExtractId(retry).Should().Be(idX);

        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        (await db.Receipts.CountAsync(r => r.Notes == marker)).Should().Be(1);
    }

    // ── T-J5(a) — atomicity: a mid-transaction failure rolls back the WHOLE tx ─

    [SkippableFact]
    public async Task J5a_ActivityRecorder_failure_rolls_back_the_whole_transaction()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var customerId = await SeedCustomerAsync(1);
        var (apiKey, _) = await MintKeyAsync(1, 1, ["sales.quotation.create"]);
        await using var factory = new DocumentFenceApiFactory(_fx.ConnectionString) { ThrowOnQuotationCreatedActivity = true };
        using var http = factory.CreateClient();

        var marker = $"j5a-{Guid.NewGuid():N}";
        var key = $"j5akey-{Guid.NewGuid():N}";

        HttpResponseMessage? resp = null;
        try { resp = await PostAsync(http, "/api/v1/quotations", apiKey, key, BuildQuotationJson(customerId, marker)); }
        catch (HttpRequestException) { /* TestServer may surface the unhandled exception as a transport failure */ }
        if (resp is not null)
            ((int)resp.StatusCode).Should().BeGreaterThanOrEqualTo(500,
                "the forced ActivityRecorder failure must surface as a server error, not a partial success");

        factory.ThrowOnQuotationCreatedActivity = false;

        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, 1, 1);
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        (await db.Quotations.CountAsync(q => q.Notes == marker)).Should().Be(0,
            "the whole create transaction must roll back — a failure between the two SaveChanges must not leave a document behind");
    }

    // ── T-J5(b) — unkeyed in-process create leaves the three columns NULL ────

    [SkippableFact]
    public async Task J5b_Unkeyed_inprocess_create_leaves_fence_columns_null()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var customerId = await SeedCustomerAsync(1);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var marker = $"j5b-{Guid.NewGuid():N}";

        long id;
        await using (var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, 1, 1))
        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IQuotationService>();
            id = await svc.CreateDraftAsync(new CreateQuotationRequest(
                today, today.AddDays(30), customerId, null, "THB", 1m, marker, null,
                [new ChainLineInput(null, "j5b unfenced line", 1m, "หน่วย", 100m, 0m, null, null, 0m, null)]), default);
        }

        await using var sp2 = TestCompanyFactory.BuildProvider(_fx.ConnectionString, 1, 1);
        await using var scope2 = sp2.CreateAsyncScope();
        var db = scope2.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var row = await db.Quotations.AsNoTracking().Where(q => q.QuotationId == id)
            .Select(q => new { q.CreatedViaApiKeyId, q.IdempotencyKey, q.IdempotencyRequestHash })
            .SingleAsync();
        row.CreatedViaApiKeyId.Should().BeNull("J3: an unkeyed create must never stamp the audit id");
        row.IdempotencyKey.Should().BeNull();
        row.IdempotencyRequestHash.Should().BeNull();

        (await db.ActivityLogs.CountAsync(a => a.EntityType == "Quotation" && a.EntityId == id)).Should().BeGreaterThan(0,
            "J3: document + activity row must both exist — I10 closed, atomic commit");
    }

    // ── T-J6 — numbering: DocNo null on create (fenced or not); Send still allocates ─

    [SkippableFact]
    public async Task J6_Drafts_have_null_docno_fenced_or_not_send_still_allocates()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var customerId = await SeedCustomerAsync(1);
        var (apiKey, _) = await MintKeyAsync(1, 1, ["sales.quotation.create", "sales.quotation.send"]);
        await using var factory = new DocumentFenceApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();

        var markerFenced = $"j6f-{Guid.NewGuid():N}";
        var createResp = await PostAsync(http, "/api/v1/quotations", apiKey,
            $"j6create-{Guid.NewGuid():N}", BuildQuotationJson(customerId, markerFenced));
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var fencedId = ExtractId(createResp);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        long unfencedId;
        await using (var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, 1, 1))
        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IQuotationService>();
            unfencedId = await svc.CreateDraftAsync(new CreateQuotationRequest(
                today, today.AddDays(30), customerId, null, "THB", 1m, $"j6u-{Guid.NewGuid():N}", null,
                [new ChainLineInput(null, "j6 unfenced line", 1m, "หน่วย", 100m, 0m, null, null, 0m, null)]), default);
        }

        await using (var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, 1, 1))
        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
            (await db.Quotations.Where(q => q.QuotationId == fencedId).Select(q => q.DocNo).SingleAsync())
                .Should().BeNull("J4: no DocNo is consumed by a fenced create");
            (await db.Quotations.Where(q => q.QuotationId == unfencedId).Select(q => q.DocNo).SingleAsync())
                .Should().BeNull("J4: no DocNo is consumed by an unfenced create either");
        }

        var sendResp = await PostAsync(http, $"/api/v1/quotations/{fencedId}/send", apiKey,
            $"j6send-{Guid.NewGuid():N}", null);
        sendResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using (var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, 1, 1))
        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
            (await db.Quotations.Where(q => q.QuotationId == fencedId).Select(q => q.DocNo).SingleAsync())
                .Should().NotBeNull("Send must still allocate a DocNo exactly as before the fence");
        }
    }

    // ── T-J7 — RLS leg: pg_database_owner + explicit GRANT, company-agnostic SELECT ──

    [SkippableFact]
    public async Task J7_Rls_scopes_the_fence_lookup_to_the_pinned_company()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var coA = await SeedVatCompanyAsync();
        var coB = await SeedVatCompanyAsync();
        var (apiKeyA, apiKeyIdA) = await MintKeyAsync(coA.CompanyId, coA.BranchId, ["sales.quotation.create"]);

        await using var factory = new DocumentFenceApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        var marker = $"j7-{Guid.NewGuid():N}";
        var key = $"j7key-{Guid.NewGuid():N}";
        var resp = await PostAsync(http, "/api/v1/quotations", apiKeyA, key, BuildQuotationJson(coA.CustomerId, marker));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);

        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, coB.CompanyId, coB.BranchId);
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();

        await db.Database.OpenConnectionAsync();
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "GRANT USAGE ON SCHEMA sales TO pg_database_owner; " +
                "GRANT SELECT ON sales.quotations TO pg_database_owner;");

            await db.Database.ExecuteSqlRawAsync(
                "SELECT set_config('app.company_id', {0}, false)", coB.CompanyId.ToString());
            await db.Database.ExecuteSqlRawAsync("SET ROLE pg_database_owner");

            var conn = (System.Data.Common.DbConnection)db.Database.GetDbConnection();
            var seenAsB = new List<int>();
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT company_id FROM sales.quotations WHERE created_via_api_key_id = @a AND idempotency_key = @k";
                var pa = cmd.CreateParameter(); pa.ParameterName = "a"; pa.Value = apiKeyIdA; cmd.Parameters.Add(pa);
                var pk = cmd.CreateParameter(); pk.ParameterName = "k"; pk.Value = key; cmd.Parameters.Add(pk);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync()) seenAsB.Add(reader.GetInt32(0));
            }
            seenAsB.Should().BeEmpty(
                "J6: pinned as company B, a company-agnostic SELECT for company A's exact fence tuple must return NOTHING — RLS-invisible, not merely un-matched by other predicates");

            // Sanity: pinned back to company A, the SAME query DOES find the row.
            await db.Database.ExecuteSqlRawAsync(
                "SELECT set_config('app.company_id', {0}, false)", coA.CompanyId.ToString());
            var seenAsA = new List<int>();
            await using (var cmd2 = conn.CreateCommand())
            {
                cmd2.CommandText = "SELECT company_id FROM sales.quotations WHERE created_via_api_key_id = @a AND idempotency_key = @k";
                var pa2 = cmd2.CreateParameter(); pa2.ParameterName = "a"; pa2.Value = apiKeyIdA; cmd2.Parameters.Add(pa2);
                var pk2 = cmd2.CreateParameter(); pk2.ParameterName = "k"; pk2.Value = key; cmd2.Parameters.Add(pk2);
                await using var reader2 = await cmd2.ExecuteReaderAsync();
                while (await reader2.ReadAsync()) seenAsA.Add(reader2.GetInt32(0));
            }
            seenAsA.Should().Equal([coA.CompanyId], "sanity: pinned to the OWNING company, the same query finds exactly the one row");
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("RESET ROLE");
            await db.Database.CloseConnectionAsync();
        }
    }

    // ── T-J8 — hash fence (J2b): permanent 409 after claim-row loss, then original body
    // converges. Run for ALL THREE fenced types (scope extension) — J2b (§3.6) covers each. ──

    [SkippableTheory]
    [InlineData(DocType.Quotation)]
    [InlineData(DocType.TaxInvoice)]
    [InlineData(DocType.Receipt)]
    public async Task J8_Hash_mismatch_is_a_permanent_409_then_original_body_converges(DocType docType)
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (companyId, branchId, customerId) = await SetupTenantAsync(docType);
        var (apiKey, apiKeyId) = await MintKeyAsync(companyId, branchId, [CreateScopeFor(docType)]);
        await using var factory = new DocumentFenceApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        var route = RouteFor(docType);

        var key = $"j8key-{docType}-{Guid.NewGuid():N}";
        var markerB = $"j8b-{docType}-{Guid.NewGuid():N}";
        var markerBPrime = $"j8bp-{docType}-{Guid.NewGuid():N}";
        var bodyB = BuildBody(docType, customerId, markerB);
        var bodyBPrime = BuildBody(docType, customerId, markerBPrime);

        var first = await PostAsync(http, route, apiKey, key, bodyB);
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var idX = ExtractId(first);

        (await DeleteClaimRowAsync(companyId, apiKeyId, key)).Should().Be(1, "simulates the 24h claim-row purge");

        var mismatch = await PostAsync(http, route, apiKey, key, bodyBPrime);
        mismatch.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "J2b: same key + different body after claim-row loss must be a permanent 409, even past the replay window");
        using (var doc = JsonDocument.Parse(await mismatch.Content.ReadAsStringAsync()))
            doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("idempotency.body_mismatch");

        await using (var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, companyId, branchId))
        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
            (await CountByMarkerAsync(docType, db, markerB)).Should().Be(1);
            (await CountByMarkerAsync(docType, db, markerBPrime)).Should().Be(0,
                "the mismatched body must create NOTHING");
        }

        var retryOriginal = await PostAsync(http, route, apiKey, key, bodyB);
        ((int)retryOriginal.StatusCode).Should().BeInRange(200, 299,
            "the mismatch releases the claim on its way out (pipeline order, §1) — a retry with the ORIGINAL body is a fresh execution that converges");
        ExtractId(retryOriginal).Should().Be(idX);

        await using (var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, companyId, branchId))
        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
            (await CountByMarkerAsync(docType, db, markerB)).Should().Be(1, "still exactly one document overall");
        }
    }

    // ── T-J9 — ambient columns stamped for ALL THREE types ───────────────────

    [SkippableFact]
    public async Task J9_Quotation_stamps_ambient_columns()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var customerId = await SeedCustomerAsync(1);
        var (apiKey, apiKeyId) = await MintKeyAsync(1, 1, ["sales.quotation.create"]);
        await using var factory = new DocumentFenceApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();

        var marker = $"j9q-{Guid.NewGuid():N}";
        var key = $"j9qkey-{Guid.NewGuid():N}";
        var resp = await PostAsync(http, "/api/v1/quotations", apiKey, key, BuildQuotationJson(customerId, marker));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = ExtractId(resp);

        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, 1, 1);
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var row = await db.Quotations.AsNoTracking().Where(q => q.QuotationId == id)
            .Select(q => new { q.CreatedViaApiKeyId, q.IdempotencyKey, q.IdempotencyRequestHash })
            .SingleAsync();
        row.CreatedViaApiKeyId.Should().Be(apiKeyId);
        row.IdempotencyKey.Should().Be(key);
        row.IdempotencyRequestHash.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [SkippableFact]
    public async Task J9_TaxInvoice_stamps_ambient_columns()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await SeedVatCompanyAsync();
        var (apiKey, apiKeyId) = await MintKeyAsync(co.CompanyId, co.BranchId, ["sales.tax_invoice.create"]);
        await using var factory = new DocumentFenceApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();

        var marker = $"j9ti-{Guid.NewGuid():N}";
        var key = $"j9tikey-{Guid.NewGuid():N}";
        var resp = await PostAsync(http, "/api/v1/tax-invoices", apiKey, key, BuildTaxInvoiceJson(co.CustomerId, marker));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = ExtractId(resp);

        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var row = await db.TaxInvoices.AsNoTracking().Where(t => t.TaxInvoiceId == id)
            .Select(t => new { t.CreatedViaApiKeyId, t.IdempotencyKey, t.IdempotencyRequestHash })
            .SingleAsync();
        row.CreatedViaApiKeyId.Should().Be(apiKeyId, "proves the ambient middleware→service channel is live for tax invoices too");
        row.IdempotencyKey.Should().Be(key);
        row.IdempotencyRequestHash.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [SkippableFact]
    public async Task J9_Receipt_stamps_ambient_columns()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await SeedVatCompanyAsync();
        var (apiKey, apiKeyId) = await MintKeyAsync(co.CompanyId, co.BranchId, ["sales.receipt.create"]);
        await using var factory = new DocumentFenceApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();

        var marker = $"j9rc-{Guid.NewGuid():N}";
        var key = $"j9rckey-{Guid.NewGuid():N}";
        var resp = await PostAsync(http, "/api/v1/receipts", apiKey, key, BuildReceiptJson(co.CustomerId, marker));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = ExtractId(resp);

        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var row = await db.Receipts.AsNoTracking().Where(r => r.ReceiptId == id)
            .Select(r => new { r.CreatedViaApiKeyId, r.IdempotencyKey, r.IdempotencyRequestHash })
            .SingleAsync();
        row.CreatedViaApiKeyId.Should().Be(apiKeyId, "proves the ambient middleware→service channel is live for receipts too");
        row.IdempotencyKey.Should().Be(key);
        row.IdempotencyRequestHash.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    // ── T-J10 (scope extension) — IsFenceCollision: pure unit test, no DB. Contract per spec
    // §3.3's own published code: true iff InnerException is a PostgresException with
    // SqlState=="23505" AND a non-null ConstraintName ENDING WITH "_idem". ────────────────────

    [Theory]
    [InlineData("23505", "ux_quotations_idem", true)]
    [InlineData("23505", "ux_tax_invoices_idem", true)]
    [InlineData("23505", "ux_receipts_idem", true)]
    [InlineData("23505", "ix_quotations_company_id_doc_no", false)]      // a doc-no unique, not the fence's
    [InlineData("23505", "ux_idemp_company_apikey_key", false)]          // the CLAIM-row unique, not the document fence's
    [InlineData("23503", "ux_quotations_idem", false)]                  // right name, wrong SqlState (FK violation)
    public void IsFenceCollision_matches_23505_on_a_fence_index_name_only(
        string sqlState, string constraintName, bool expected)
    {
        var pg = new PostgresException("duplicate key value violates unique constraint",
            "ERROR", "ERROR", sqlState, constraintName: constraintName);
        var ex = new DbUpdateException("db update failed", pg);
        IdempotencyFenceLock.IsFenceCollision(ex).Should().Be(expected);
    }

    [Fact]
    public void IsFenceCollision_false_when_inner_exception_is_not_postgres()
    {
        var ex = new DbUpdateException("db update failed", new InvalidOperationException("not a postgres exception"));
        IdempotencyFenceLock.IsFenceCollision(ex).Should().BeFalse();
    }

    // ── T-J11 (scope extension) — LockKey is a pure, culture-invariant function of its inputs.
    // Spec §3.3: "PINNED FOREVER: changing this derivation splits the lock space" — it must
    // never depend on ambient state such as the thread's current culture. ────────────────────

    [Fact]
    public void LockKey_is_pure_and_culture_invariant()
    {
        var a = IdempotencyFenceLock.LockKey(42, "key-abc");
        var b = IdempotencyFenceLock.LockKey(42, "key-abc");
        a.Should().Be(b, "LockKey must be a pure function of its inputs — same call, same result");

        var differentKey = IdempotencyFenceLock.LockKey(42, "key-xyz");
        differentKey.Should().NotBe(a, "spot check: a different key for the same api key should (almost always) differ");

        var differentApiKey = IdempotencyFenceLock.LockKey(99, "key-abc");
        differentApiKey.Should().NotBe(a, "spot check: a different api key for the same key string should (almost always) differ");

        var spotCheck = new HashSet<int>();
        for (var i = 0; i < 10; i++) spotCheck.Add(IdempotencyFenceLock.LockKey(5000 + i, $"spot-key-{i}"));
        spotCheck.Should().HaveCount(10, "spot check: 10 distinct (apiKeyId, key) pairs should (almost always) yield 10 distinct lock ints");

        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("th-TH");
            var th = IdempotencyFenceLock.LockKey(42, "key-abc");
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            var ar = IdempotencyFenceLock.LockKey(42, "key-abc");

            th.Should().Be(a, "the lock-key derivation is PINNED FOREVER (§3.3) — it must not depend on the thread's current culture (th-TH)");
            ar.Should().Be(a, "same — Arabic culture (ar-SA) must not change the derivation either");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
