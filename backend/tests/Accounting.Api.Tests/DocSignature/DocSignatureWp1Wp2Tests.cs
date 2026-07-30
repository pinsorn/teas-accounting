using System.Net.Http.Headers;
using Accounting.Api.Tests.Fixtures;
using Accounting.Api.Tests.Rbac;
using Accounting.Application.Abstractions;
using Accounting.Application.Attachments;
using Accounting.Application.Identity;
using Accounting.Application.Master;
using Accounting.Application.Pdf;
using Accounting.Application.Sales;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Identity;
using Accounting.Domain.Entities.Sys;
using Accounting.Domain.Enums;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Identity;
using Accounting.Infrastructure.Persistence;
using Accounting.TestKit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.DocSignature;

/// <summary>
/// doc-signature-and-foot-layout spec — WP-1 (schema + actor capture) and WP-2 (upload
/// endpoints + forgery guard) test coverage. WP-3's renderer/resolution (PaperSignatureSource,
/// PaperDocModel.Signatures) is a LATER work package and is intentionally NOT exercised here.
///
/// Storage-footgun discipline: any assertion that reaches AttachmentService.UploadAsync's real
/// file write uses <see cref="Provider"/> with the per-test <see cref="_root"/> override (mirrors
/// Sprint11AttachmentTests). HTTP-level (RbacApiFactory) assertions are restricted to DENY paths
/// (403) and a "reachable but no file" 400 probe — neither ever calls storage.SaveAsync, so
/// RbacApiFactory needs no storage override.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class DocSignatureWp1Wp2Tests : IDisposable
{
    private readonly PostgresFixture _fx;
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "teas-it-" + Guid.NewGuid().ToString("N")[..8]);

    public DocSignatureWp1Wp2Tests(PostgresFixture fx) => _fx = fx;
    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));

    // Tier-2 finding (2026-07-30, MED) — SetUserSignatureAsync/UpdateStampAsync now verify the
    // real image magic number (not just the caller-supplied MIME string), so any test that goes
    // THROUGH those two methods (not the generic IAttachmentService.UploadAsync, which is
    // unaffected) needs real, decodable bytes rather than arbitrary filler.
    private static readonly byte[] MinimalPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    private ServiceProvider Provider(int companyId, int branchId, long userId, string? storageRoot = null)
    {
        var dict = new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = _fx.ConnectionString };
        if (storageRoot is not null) dict["FileStorage:StorageRoot"] = storageRoot;
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        var s = new ServiceCollection();
        s.AddLogging();
        return s.AddInfrastructure(cfg)
            .AddSingleton<ITenantContext>(new StubTenant
            { CompanyId = companyId, BranchId = branchId, UserId = userId, IsSuperAdmin = false })
            .BuildServiceProvider();
    }

    private async Task<int> RoleIdAsync(int companyId, string roleCode)
    {
        await using var sp = Provider(companyId, 1, userId: 1);
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        return await db.Roles.Where(r => r.CompanyId == companyId && r.RoleCode == roleCode)
            .Select(r => r.RoleId).FirstAsync();
    }

    /// <summary>A real sys.users row with a UserRole in <paramref name="companyId"/> — the shape
    /// AttachmentService.ParentExistsAsync's UserSignature arm and RbacAdminService's
    /// GuardManageUserAsync both need (§E3).</summary>
    private async Task<long> CreateUserAsync(int companyId, params int[] roleIds)
    {
        await using var sp = Provider(companyId, 1, userId: 1);
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "SELECT setval(pg_get_serial_sequence('sys.users','user_id'), " +
            "(SELECT COALESCE(MAX(user_id),0)+1 FROM sys.users), false);");
        var user = new User
        {
            Username = "u-" + TestIds.Suffix(), Email = TestIds.Email(), PasswordHash = "x",
            FullName = TestIds.Name(), IsActive = true,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        foreach (var rid in roleIds)
            db.UserRoles.Add(new UserRole
            {
                UserId = user.UserId, RoleId = rid, CompanyId = companyId,
                BranchId = 0, ValidFrom = today, ValidTo = null,
            });
        if (roleIds.Length > 0) await db.SaveChangesAsync();
        return user.UserId;
    }

    // ── HTTP helpers ─────────────────────────────────────────────────────────────
    private static string Token(long userId, int companyId, IEnumerable<string> perms) =>
        new JwtTokenIssuer(new StaticOptionsMonitor<JwtOptions>(new JwtOptions
        {
            Issuer = RbacApiFactory.JwtIssuer, Audience = RbacApiFactory.JwtAudience,
            SigningKey = RbacApiFactory.JwtSigningKey, AccessTokenMinutes = 60,
        })).Issue(new TokenClaims(
            UserId: userId, Username: $"docsig-{userId}", CompanyId: companyId, BranchId: 1,
            IsSuperAdmin: false, Roles: [], Permissions: perms.ToList())).Token;

    private static async Task<HttpResponseMessage> PostAttachmentAsync(
        HttpClient client, string token, string parentType, long parentId)
    {
        var content = new MultipartFormDataContent
        {
            { new StringContent(parentType), "parent_type" },
            { new StringContent(parentId.ToString()), "parent_id" },
            { new StringContent("OTHER"), "category" },
            { new StringContent("test upload"), "description" },
        };
        var file = new ByteArrayContent([1, 2, 3, 4]);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(file, "file", "sig.png");

        using var req = new HttpRequestMessage(HttpMethod.Post, "/attachments") { Content = content };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(req);
    }

    /// <summary>DENY/reachability probe only — deliberately omits the file part so it NEVER
    /// reaches storage.SaveAsync (storage-footgun, see class doc). A 403 means the permission
    /// gate blocked it; a 400 means it was authorized but rejected for the missing file part —
    /// proving the route is mapped and reachable.</summary>
    private static async Task<HttpResponseMessage> PostMultipartNoFileAsync(
        HttpClient client, string token, string path)
    {
        using var content = new MultipartFormDataContent
        {
            { new StringContent("x"), "unused" },
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(req);
    }

    private static async Task<HttpResponseMessage> PutJsonAsync(
        HttpClient client, string token, string path, string json)
    {
        using var req = new HttpRequestMessage(HttpMethod.Put, path)
        { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(req);
    }

    // ═══════════════════════ WP-1 — T16: actor capture ═════════════════════════════

    [SkippableFact]
    public async Task T16_Quotation_SentBy_and_BillingNote_IssuedBy_capture_the_acting_user()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        const long actingUserId = 555_001;

        long qId;
        await using (var sp = Provider(co.CompanyId, co.BranchId, actingUserId))
        await using (var scope = sp.CreateAsyncScope())
        {
            var qsvc = scope.ServiceProvider.GetRequiredService<IQuotationService>();
            var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
            qId = await qsvc.CreateDraftAsync(new CreateQuotationRequest(
                Today, Today.AddDays(30), co.CustomerId, null, "THB", 1m, null, null,
                [new ChainLineInput(null, "งานทดสอบลายเซ็น", 1m, "งาน", 1000m, 0m, 1, "VAT7", 0.07m)]),
                default);

            (await db.Quotations.AsNoTracking().Where(x => x.QuotationId == qId)
                    .Select(x => x.SentBy).FirstAsync())
                .Should().BeNull("a Draft has no signer yet (I2)");

            await qsvc.SendAsync(qId, default);

            (await db.Quotations.AsNoTracking().Where(x => x.QuotationId == qId)
                    .Select(x => x.SentBy).FirstAsync())
                .Should().Be(actingUserId);
        }

        long bnId;
        await using (var sp = Provider(co.CompanyId, co.BranchId, actingUserId))
        await using (var scope = sp.CreateAsyncScope())
        {
            var bnsvc = scope.ServiceProvider.GetRequiredService<IBillingNoteService>();
            var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
            bnId = await bnsvc.CreateDraftAsync(new CreateBillingNoteRequest(
                Today, Today.AddDays(30), co.CustomerId, null, null, null, "THB", 1m, null, null,
                [new BillingLineInput(null, null, "งานทดสอบลายเซ็น", 1m, "งาน", 1000m, 0m, 1, "VAT7", 0.07m)]),
                default);

            (await db.BillingNotes.AsNoTracking().Where(x => x.BillingNoteId == bnId)
                    .Select(x => x.IssuedBy).FirstAsync())
                .Should().BeNull("a Draft has no signer yet (I2)");

            await bnsvc.IssueAsync(bnId, default);

            (await db.BillingNotes.AsNoTracking().Where(x => x.BillingNoteId == bnId)
                    .Select(x => x.IssuedBy).FirstAsync())
                .Should().Be(actingUserId);
        }
    }

    // ═══════════════ §16 F1 — super-admin self-sign, still bounded to self ════════

    [SkippableFact]
    public async Task F1_super_admin_may_self_sign_in_a_company_with_no_membership_but_not_stamp_others()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        // "Switched to" company: the super-admin has NO sys.user_roles row here at all
        // (CompanySwitchService.cs performs no membership check on switch, per Fable's §16 F1
        // premise verification) — this is the one case a non-member can legitimately be a
        // document actor, so ParentExistsAsync must allow exactly this and nothing wider.
        var coB = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        const long superAdminUserId = 900_555;

        // Provider() always builds a non-super StubTenant; F1 needs IsSuperAdmin=true, so
        // resolve IAttachmentService against a purpose-built super-admin tenant instead.
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = _fx.ConnectionString,
            ["FileStorage:StorageRoot"] = _root,
        }).Build();
        await using var superSp = new ServiceCollection().AddLogging()
            .AddInfrastructure(cfg)
            .AddSingleton<ITenantContext>(new StubTenant
            { CompanyId = coB.CompanyId, BranchId = 1, UserId = superAdminUserId, IsSuperAdmin = true })
            .BuildServiceProvider();
        await using var superScope = superSp.CreateAsyncScope();
        var svc = superScope.ServiceProvider.GetRequiredService<IAttachmentService>();

        // Self: allowed despite zero user_roles rows in coB.
        var uploaded = await svc.UploadAsync("USER_SIGNATURE", superAdminUserId, "OTHER", "sig",
            "sig.png", "image/png", 4, new MemoryStream([1, 2, 3, 4]), default);
        uploaded.AttachmentId.Should().BeGreaterThan(0, "a super-admin may self-sign in any company (§16 F1)");

        // Non-self, non-member target: the widened rule must NOT extend to anyone else.
        var strangerId = superAdminUserId + 1;
        var act = () => svc.UploadAsync("USER_SIGNATURE", strangerId, "OTHER", "sig",
            "sig.png", "image/png", 4, new MemoryStream([1, 2, 3, 4]), default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("attachment.parent_not_found",
            "F1 widens the guard only for SELF — a non-member non-self target must still be rejected");
    }

    // ═══════════════════════ WP-2 — T11: forgery guard (I7) ════════════════════════

    [SkippableFact]
    public async Task T11_signature_attach_enforces_company_membership_and_the_permission_guard()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var coA = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var coB = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var accountantRoleA = await RoleIdAsync(coA.CompanyId, Role.SystemRoles.Accountant);
        var targetUserA = await CreateUserAsync(coA.CompanyId, accountantRoleA);
        var targetUserB = await CreateUserAsync(coB.CompanyId);

        // (a) same-company target → succeeds; row shape matches the (company, user) key (§E1).
        await using (var sp = Provider(coA.CompanyId, coA.BranchId, userId: 1, storageRoot: _root))
        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IAttachmentService>();
            var uploaded = await svc.UploadAsync("USER_SIGNATURE", targetUserA, "OTHER", "sig",
                "sig.png", "image/png", 4, new MemoryStream([1, 2, 3, 4]), default);
            uploaded.AttachmentId.Should().BeGreaterThan(0);

            var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var row = await db.Attachments.AsNoTracking().FirstAsync(a => a.AttachmentId == uploaded.AttachmentId);
            row.ParentType.Should().Be(AttachmentParentType.UserSignature);
            row.ParentId.Should().Be(targetUserA);
            row.CompanyId.Should().Be(coA.CompanyId);
        }

        // (b) cross-company target → attachment.parent_not_found (422 default), no row created.
        await using (var sp = Provider(coA.CompanyId, coA.BranchId, userId: 1, storageRoot: _root))
        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IAttachmentService>();
            var act = () => svc.UploadAsync("USER_SIGNATURE", targetUserB, "OTHER", "sig",
                "sig.png", "image/png", 4, new MemoryStream([1, 2, 3, 4]), default);
            (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("attachment.parent_not_found");

            var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
            (await db.Attachments.AsNoTracking().CountAsync(a =>
                    a.ParentType == AttachmentParentType.UserSignature && a.ParentId == targetUserB))
                .Should().Be(0, "the rejected cross-company upload must not create a row");
        }

        // (c) HTTP: caller holds sys.attachment.upload (passes the STATIC route policy) but NOT
        // sys.user.manage → 403 from ParentGuard's OWN (fresh, DB-backed) permission lookup. This
        // request is a DENY case — it never reaches storage.SaveAsync, so no storage override
        // is needed for RbacApiFactory here (storage-footgun, class doc).
        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();
        var plainToken = Token(userId: 990_777, coA.CompanyId, ["sys.attachment.upload"]);
        using var deny = await PostAttachmentAsync(client, plainToken, "USER_SIGNATURE", targetUserA);
        ((int)deny.StatusCode).Should().Be(403, "the caller holds sys.attachment.upload but lacks sys.user.manage");
    }

    // ═══════════════════════ WP-2 — T12: new endpoints ═════════════════════════════

    [SkippableFact]
    public async Task T12_signature_profile_and_stamp_endpoints_are_gated_and_wired()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var accountantRole = await RoleIdAsync(co.CompanyId, Role.SystemRoles.Accountant);
        var targetUser = await CreateUserAsync(co.CompanyId, accountantRole);

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();
        // Synthetic callers: the route policy is JWT-claims-only (PermissionHandler), so no real
        // DB-backed role is needed here — only the routes' OWN permission gate is under test.
        var adminToken = Token(990_001, co.CompanyId, ["sys.user.manage", "master.company_profile.manage"]);
        var plainToken = Token(990_002, co.CompanyId, []);

        // --- POST /admin/rbac/users/{id}/signature — gated on sys.user.manage ---
        using (var deny = await PostMultipartNoFileAsync(client, plainToken, $"/admin/rbac/users/{targetUser}/signature"))
            ((int)deny.StatusCode).Should().Be(403);
        using (var reachable = await PostMultipartNoFileAsync(client, adminToken, $"/admin/rbac/users/{targetUser}/signature"))
            ((int)reachable.StatusCode).Should().Be(400, "authorized but no file part — proves the route is mapped, not 403");

        // Actual upload + created-row shape — service level, storage-root override (footgun).
        await using (var sp = Provider(co.CompanyId, co.BranchId, userId: 1, storageRoot: _root))
        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IRbacAdminService>();
            var url = await svc.SetUserSignatureAsync(
                targetUser, "sig.png", "image/png", MinimalPng.Length, new MemoryStream(MinimalPng), default);
            url.Should().MatchRegex(@"^/attachments/\d+/download$");
            var attachmentId = long.Parse(url.Split('/')[2]);

            var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var row = await db.Attachments.AsNoTracking().FirstAsync(a => a.AttachmentId == attachmentId);
            row.ParentId.Should().Be(targetUser);
            row.CompanyId.Should().Be(co.CompanyId);
            row.ParentType.Should().Be(AttachmentParentType.UserSignature);
        }

        // --- PUT /admin/rbac/users/{id}/profile — no storage; full HTTP round-trip ---
        using (var deny = await PutJsonAsync(client, plainToken, $"/admin/rbac/users/{targetUser}/profile",
                   "{\"position\":\"ผู้จัดการ\"}"))
            ((int)deny.StatusCode).Should().Be(403);
        using (var ok = await PutJsonAsync(client, adminToken, $"/admin/rbac/users/{targetUser}/profile",
                   "{\"position\":\"ผู้จัดการฝ่ายขาย\"}"))
            ((int)ok.StatusCode).Should().Be(204);

        await using (var sp = Provider(co.CompanyId, co.BranchId, userId: 1))
        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
            (await db.Users.AsNoTracking().Where(u => u.UserId == targetUser)
                    .Select(u => u.Position).FirstAsync())
                .Should().Be("ผู้จัดการฝ่ายขาย");
        }

        // --- POST /company-profile/stamp — gated on master.company_profile.manage ---
        using (var deny = await PostMultipartNoFileAsync(client, plainToken, "/company-profile/stamp"))
            ((int)deny.StatusCode).Should().Be(403);
        using (var reachable = await PostMultipartNoFileAsync(client, adminToken, "/company-profile/stamp"))
            ((int)reachable.StatusCode).Should().Be(400, "authorized but no file part — proves the route is mapped, not 403");

        await using (var sp = Provider(co.CompanyId, co.BranchId, userId: 1, storageRoot: _root))
        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<ICompanyProfileService>();
            var url = await svc.UpdateStampAsync("stamp.png", "image/png", MinimalPng.Length, new MemoryStream(MinimalPng), default);
            url.Should().MatchRegex(@"^/attachments/\d+/download$");

            var profile = await svc.GetAsync(default);
            profile!.StampUrl.Should().Be(url, "no CompanyProfile.StampUrl column (§E5) — GetAsync resolves latest-wins");
        }
    }

    // ═══════════════════════ §16 F2/F3/F4/F5 ═══════════════════════════════════════

    [SkippableFact]
    public async Task F2_signature_and_profile_changes_are_audited()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var role = await RoleIdAsync(co.CompanyId, Role.SystemRoles.Accountant);
        var targetUser = await CreateUserAsync(co.CompanyId, role);

        await using var sp = Provider(co.CompanyId, co.BranchId, userId: 1, storageRoot: _root);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IRbacAdminService>();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();

        await svc.SetUserSignatureAsync(targetUser, "sig.png", "image/png", MinimalPng.Length, new MemoryStream(MinimalPng), default);
        (await db.ActivityLogs.AsNoTracking().AnyAsync(a =>
                a.EntityType == "user" && a.EntityId == targetUser && a.ActivityType == "user_signature_uploaded"))
            .Should().BeTrue("§16 F2 — the signature upload must leave an audit trail");

        await svc.SetUserProfileAsync(targetUser, "ผู้จัดการ", default);
        (await db.ActivityLogs.AsNoTracking().AnyAsync(a =>
                a.EntityType == "user" && a.EntityId == targetUser && a.ActivityType == "user_profile_changed"))
            .Should().BeTrue("§16 F2 — the position change must leave an audit trail");
    }

    [SkippableFact]
    public async Task F3_position_over_100_chars_is_rejected_not_500()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var role = await RoleIdAsync(co.CompanyId, Role.SystemRoles.Accountant);
        var targetUser = await CreateUserAsync(co.CompanyId, role);
        var tooLong = new string('ก', 120);

        await using (var sp = Provider(co.CompanyId, co.BranchId, userId: 1))
        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IRbacAdminService>();
            var act = () => svc.SetUserProfileAsync(targetUser, tooLong, default);
            (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("user.position_too_long");
        }

        // HTTP: confirm the shape error resolves to a well-formed 4xx, never an unhandled 500
        // (the coordinator's "(400)" target vs the DomainExceptionMiddleware's actual default-422
        // code→status map is called out in the attempt log for Fable's confirmation).
        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();
        var adminToken = Token(990_001, co.CompanyId, ["sys.user.manage"]);
        using var resp = await PutJsonAsync(client, adminToken, $"/admin/rbac/users/{targetUser}/profile",
            $"{{\"position\":\"{tooLong}\"}}");
        ((int)resp.StatusCode).Should().BeInRange(400, 499, "a shape error must never surface as an unhandled 500");
    }

    [SkippableFact]
    public async Task F4_ListUsersAsync_only_resolves_signature_url_for_the_session_company()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var coA = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var coB = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var roleB = await RoleIdAsync(coB.CompanyId, Role.SystemRoles.Accountant);
        var userB = await CreateUserAsync(coB.CompanyId, roleB);

        await using (var sp = Provider(coB.CompanyId, coB.BranchId, userId: 1, storageRoot: _root))
        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IAttachmentService>();
            await svc.UploadAsync("USER_SIGNATURE", userB, "OTHER", "sig", "sig.png", "image/png", 4,
                new MemoryStream([1, 2, 3, 4]), default);
        }

        // Own-company listing (session == coB) → resolves the URL.
        await using (var sp = Provider(coB.CompanyId, coB.BranchId, userId: 1))
        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IRbacAdminService>();
            var list = await svc.ListUsersAsync(null, default);
            list.Should().Contain(u => u.UserId == userB && u.SignatureUrl != null);
        }

        // Cross-company: a super-admin whose SESSION is coA lists coB's users (companyId param)
        // → SignatureUrl must be null (§16 F4) — G1 FORCE RLS makes a real resolve unreliable.
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:Postgres"] = _fx.ConnectionString }).Build();
        await using var crossSp = new ServiceCollection().AddLogging()
            .AddInfrastructure(cfg)
            .AddSingleton<ITenantContext>(new StubTenant
            { CompanyId = coA.CompanyId, BranchId = 1, UserId = 1, IsSuperAdmin = true })
            .BuildServiceProvider();
        await using var crossScope = crossSp.CreateAsyncScope();
        var crossSvc = crossScope.ServiceProvider.GetRequiredService<IRbacAdminService>();
        var crossList = await crossSvc.ListUsersAsync(coB.CompanyId, default);
        crossList.Should().Contain(u => u.UserId == userB);
        crossList.First(u => u.UserId == userB).SignatureUrl.Should().BeNull(
            "§16 F4 — cross-company resolution is silently wrong under G1's FORCE RLS, never right");
    }

    [SkippableFact]
    public async Task F5_latest_wins_tiebreaks_on_attachment_id_when_uploaded_at_ties()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var role = await RoleIdAsync(co.CompanyId, Role.SystemRoles.Accountant);
        var targetUser = await CreateUserAsync(co.CompanyId, role);

        long firstId, secondId;
        await using (var sp = Provider(co.CompanyId, co.BranchId, userId: 1))
        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var tie = DateTimeOffset.UtcNow;
            var a1 = new Attachment
            {
                CompanyId = co.CompanyId, ParentType = AttachmentParentType.UserSignature, ParentId = targetUser,
                Category = AttachmentCategory.Other, FileName = "a1.png", MimeType = "image/png", SizeBytes = 4,
                StoragePath = "test/a1.png", UploadedAt = tie, UploadedBy = 1, Description = "first",
            };
            db.Attachments.Add(a1);
            await db.SaveChangesAsync();
            firstId = a1.AttachmentId;

            var a2 = new Attachment
            {
                CompanyId = co.CompanyId, ParentType = AttachmentParentType.UserSignature, ParentId = targetUser,
                Category = AttachmentCategory.Other, FileName = "a2.png", MimeType = "image/png", SizeBytes = 4,
                StoragePath = "test/a2.png", UploadedAt = tie, UploadedBy = 1, Description = "second",
            };
            db.Attachments.Add(a2);
            await db.SaveChangesAsync();
            secondId = a2.AttachmentId;
        }
        secondId.Should().BeGreaterThan(firstId);

        await using var sp2 = Provider(co.CompanyId, co.BranchId, userId: 1);
        await using var scope2 = sp2.CreateAsyncScope();
        var svc = scope2.ServiceProvider.GetRequiredService<IRbacAdminService>();
        var list = await svc.ListUsersAsync(null, default);
        list.First(u => u.UserId == targetUser).SignatureUrl.Should().Be($"/attachments/{secondId}/download",
            "§16 F5 — same-timestamp tiebreak must deterministically pick the higher attachment id");
    }

    // ═══════════════════════ WP-2 — T13: MIME/size ═════════════════════════════════

    [Fact]
    public void T13_SignatureImage_validator_rejects_svg_and_oversize_but_allows_png_jpeg_webp()
    {
        var badMime = () => SignatureImage.Validate("image/svg+xml", 100, "user.signature");
        badMime.Should().Throw<DomainException>().Which.Code.Should().Be("user.signature.bad_mime");

        var tooLarge = () => SignatureImage.Validate("image/png", 2L * 1024 * 1024, "user.signature");
        tooLarge.Should().Throw<DomainException>().Which.Code.Should().Be("user.signature.too_large");

        var ok = () => SignatureImage.Validate("image/webp", 1024, "user.signature");
        ok.Should().NotThrow();
    }

    [SkippableFact]
    public async Task T13_our_validator_runs_before_the_generic_attachment_mime_check()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var role = await RoleIdAsync(co.CompanyId, Role.SystemRoles.Accountant);
        var targetUser = await CreateUserAsync(co.CompanyId, role);

        await using var sp = Provider(co.CompanyId, co.BranchId, userId: 1, storageRoot: _root);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IRbacAdminService>();

        var act = () => svc.SetUserSignatureAsync(
            targetUser, "sig.svg", "image/svg+xml", 100, new MemoryStream([1]), default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("user.signature.bad_mime",
            "SignatureImage.Validate must reject this before AttachmentService.UploadAsync's own " +
            "check ever runs (which would instead throw attachment.bad_mime — §1.9.1)");
    }

    // ═══════════ Tier-2 finding (2026-07-30, MED, I8) — real magic-number check ═══════

    [SkippableFact]
    public async Task Upload_rejects_bytes_that_do_not_match_their_declared_image_mime()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var role = await RoleIdAsync(co.CompanyId, Role.SystemRoles.Accountant);
        var targetUser = await CreateUserAsync(co.CompanyId, role);

        await using var sp = Provider(co.CompanyId, co.BranchId, userId: 1, storageRoot: _root);
        await using var scope = sp.CreateAsyncScope();

        // A Content-Type of image/png with garbage bytes — SignatureImage.Validate's MIME-STRING
        // check alone would pass this; only the magic-number check catches it.
        var rbacSvc = scope.ServiceProvider.GetRequiredService<IRbacAdminService>();
        var sigAct = () => rbacSvc.SetUserSignatureAsync(
            targetUser, "sig.png", "image/png", 4, new MemoryStream([1, 2, 3, 4]), default);
        (await sigAct.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("user.signature.bad_mime",
            "the Content-Type claims PNG but the bytes have no PNG magic number");

        var profileSvc = scope.ServiceProvider.GetRequiredService<ICompanyProfileService>();
        var stampAct = () => profileSvc.UpdateStampAsync(
            "stamp.png", "image/png", 4, new MemoryStream([1, 2, 3, 4]), default);
        (await stampAct.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("company_profile.stamp_bad_mime",
            "the Content-Type claims PNG but the bytes have no PNG magic number");
    }

    // ═══════════════════ WP-2 — T14/T15(backend half): default notes ══════════════

    [SkippableFact]
    public async Task T14_default_doc_notes_round_trip_through_soft_update_and_get()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = Provider(co.CompanyId, co.BranchId, userId: 1);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICompanyProfileService>();

        var before = await svc.GetAsync(default);
        before!.DefaultDocNotes.Should().BeNull("a fresh company has no defaults yet");

        var notes = new DefaultDocNotes(
            Quotation: "หมายเหตุใบเสนอราคา", SalesOrder: "   ", DeliveryOrder: null,
            TaxInvoice: "หมายเหตุใบกำกับภาษี", Receipt: "", BillingNote: "หมายเหตุใบแจ้งหนี้",
            CreditNote: "note-cn", DebitNote: "note-dn", PurchaseOrder: "note-po", PaymentVoucher: "note-pv");

        await svc.UpdateSoftAsync(new UpdateCompanyProfileSoftRequest(
            before.TradeName, before.LogoUrl, before.Phone, before.Email, before.Website,
            before.ContactName, before.BankName, before.BankAccountNo, before.BankAccountName,
            before.SsoEmployerAccountNo, DefaultDocNotes: notes), default);

        var after = await svc.GetAsync(default);
        after!.DefaultDocNotes.Should().NotBeNull();
        after.DefaultDocNotes!.Quotation.Should().Be("หมายเหตุใบเสนอราคา");
        after.DefaultDocNotes.SalesOrder.Should().BeNull("whitespace normalises to null");
        after.DefaultDocNotes.DeliveryOrder.Should().BeNull();
        after.DefaultDocNotes.TaxInvoice.Should().Be("หมายเหตุใบกำกับภาษี");
        after.DefaultDocNotes.Receipt.Should().BeNull("empty string normalises to null");
        after.DefaultDocNotes.BillingNote.Should().Be("หมายเหตุใบแจ้งหนี้");
        after.DefaultDocNotes.CreditNote.Should().Be("note-cn");
        after.DefaultDocNotes.DebitNote.Should().Be("note-dn");
        after.DefaultDocNotes.PurchaseOrder.Should().Be("note-po");
        after.DefaultDocNotes.PaymentVoucher.Should().Be("note-pv");
    }

    [SkippableFact]
    public async Task T15_backend_default_note_is_a_create_time_prefill_never_a_linkage()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = Provider(co.CompanyId, co.BranchId, userId: 1);
        await using var scope = sp.CreateAsyncScope();

        var qsvc = scope.ServiceProvider.GetRequiredService<IQuotationService>();
        var qId = await qsvc.CreateDraftAsync(new CreateQuotationRequest(
            Today, Today.AddDays(30), co.CustomerId, null, "THB", 1m,
            "หมายเหตุต้นฉบับของเอกสารนี้ ไม่ควรถูกแก้ไข", null,
            [new ChainLineInput(null, "งานทดสอบ", 1m, "งาน", 1000m, 0m, 1, "VAT7", 0.07m)]), default);

        var profileSvc = scope.ServiceProvider.GetRequiredService<ICompanyProfileService>();
        var before = await profileSvc.GetAsync(default);
        await profileSvc.UpdateSoftAsync(new UpdateCompanyProfileSoftRequest(
            before!.TradeName, before.LogoUrl, before.Phone, before.Email, before.Website,
            before.ContactName, before.BankName, before.BankAccountNo, before.BankAccountName,
            before.SsoEmployerAccountNo,
            DefaultDocNotes: new DefaultDocNotes(Quotation: "หมายเหตุ default ใหม่ที่เพิ่งตั้งค่า")), default);

        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        (await db.Quotations.AsNoTracking().Where(x => x.QuotationId == qId).Select(x => x.Notes).FirstAsync())
            .Should().Be("หมายเหตุต้นฉบับของเอกสารนี้ ไม่ควรถูกแก้ไข",
                "changing the company default must never touch an existing document's stored note (I9)");
    }
}
