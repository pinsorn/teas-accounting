using System.Net.Http.Headers;
using System.Text;
using Accounting.Api.Tests.Fixtures;
using Accounting.Api.Tests.Rbac;
using Accounting.Application.Abstractions;
using Accounting.Application.Attachments;
using Accounting.Application.Bank;
using Accounting.Application.Identity;
using Accounting.Application.Sales;
using Accounting.Domain.Entities.Bank;
using Accounting.Domain.Entities.Identity;
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

namespace Accounting.Api.Tests.Hardening;

/// <summary>
/// R3/H4 — attachment download/delete IDOR fix. Upload and list both gate on
/// AttachmentEndpoints.ParentGuard (caller must hold the attachment's PARENT read
/// permission, or be super-admin). Download did not, and delete's "any attachment"
/// branch (sys.attachment.delete) did not either — so a caller holding only the
/// broadly-granted sys.attachment.read (every role, 280_seed_attachment_perms.sql)
/// could read/soft-delete ANY attachment in the tenant by walking ids, even for a
/// parent document they have no read access to. Cross-company is unaffected (EF
/// global query filter on Attachment.CompanyId already 404s it).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class AttachmentDownloadDeleteGuardTests : IDisposable
{
    private readonly PostgresFixture _fx;
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "teas-it-" + Guid.NewGuid().ToString("N")[..8]);

    public AttachmentDownloadDeleteGuardTests(PostgresFixture fx) => _fx = fx;
    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));

    private ServiceProvider Provider(int companyId, long userId = 1)
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = _fx.ConnectionString,
            ["FileStorage:StorageRoot"] = _root,
        }).Build();
        var s = new ServiceCollection();
        s.AddLogging();
        return s.AddInfrastructure(cfg)
            .AddSingleton<ITenantContext>(new StubTenant
            { CompanyId = companyId, BranchId = 1, UserId = userId, IsSuperAdmin = false })
            .BuildServiceProvider();
    }

    private async Task<long> PostTaxInvoiceAsync(int companyId, long customerId)
    {
        await using var sp = Provider(companyId);
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        var id = await svc.CreateDraftAsync(new CreateTaxInvoiceRequest(
            Today, customerId, false, "THB", 1m, null, null, null,
            [new TaxInvoiceLineInput(null, null, "svc", 1m, 1, "ชิ้น", 100m, 0m, 1, "VAT7", 0.07m)],
            null), default);
        await svc.PostAsync(id, default);
        return id;
    }

    private static async Task<decimal> GetTiTotalAsync(ServiceProvider sp, long tiId)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        return await db.TaxInvoices.Where(x => x.TaxInvoiceId == tiId)
            .Select(x => x.TotalAmount).FirstAsync();
    }

    /// <summary>Fix 2 fail-open type (RECEIPT) — a DRAFT receipt settling the given posted TI.
    /// ParentExistsAsync only requires the row to exist (draft or posted), so posting isn't
    /// needed here — keeps the fixture minimal.</summary>
    private async Task<long> CreateDraftReceiptAsync(int companyId, long customerId, long tiId)
    {
        await using var sp = Provider(companyId);
        var total = await GetTiTotalAsync(sp, tiId);
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IReceiptService>();
        return await svc.CreateDraftAsync(new CreateReceiptRequest(
            Today, customerId, PaymentMethod.Cash, null, null, null, "THB", 1m, null,
            [new ReceiptApplicationInput(tiId, total)]), default);
    }

    /// <summary>Fix 2 fail-open type (TAX_ADJUSTMENT_NOTE, CN+DN share one parent type) — a
    /// DRAFT credit note against the given posted TI. Draft-only, same reasoning as above.</summary>
    private async Task<long> CreateDraftCreditNoteAsync(int companyId, long tiId)
    {
        await using var sp = Provider(companyId);
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<ITaxAdjustmentNoteService>();
        return await svc.CreateDraftAsync(new CreateTaxAdjustmentNoteRequest(
            NoteType: TaxAdjustmentNoteType.Credit,
            DocDate: Today,
            OriginalTaxInvoiceId: tiId,
            ReasonCode: nameof(CreditNoteReasonCode.AmountError),
            Reason: "R3/H4 remediation — attachment guard fixture",
            AdjustmentSubtotal: 10m,
            TaxRate: 0.07m,
            CurrencyCode: "THB",
            ExchangeRate: 1m,
            Notes: null), default);
    }

    /// <summary>Fix 2 fail-open type (BANK_STATEMENT) — a statement_imports row created
    /// DIRECTLY via EF (mirrors BankReconciliationServiceTests.SeedStatementLineAsync), not
    /// through the CSV import pipeline — the attachment guard only cares that the parent row
    /// exists, not how it got there.</summary>
    private async Task<long> CreateBankStatementImportAsync(int companyId, int branchId)
    {
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, companyId, branchId);
        await using var s = sp.CreateAsyncScope();
        var bankSvc = s.ServiceProvider.GetRequiredService<IBankAccountService>();
        var bankAccountId = await bankSvc.CreateAsync(new CreateBankAccountRequest(
            "KBANK", "Kasikornbank", "999-9-99999-9", null, null, null, "THB"), default);

        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var import = new StatementImport
        {
            CompanyId = companyId, BankAccountId = bankAccountId, AdapterCode = "TEST",
            SourceFileName = "test.csv", PeriodStart = Today, PeriodEnd = Today,
            OpeningBalance = 0m, ClosingBalance = 0m, LineCount = 0,
            Status = ImportStatus.Parsed, ImportedAt = DateTimeOffset.UtcNow, ImportedBy = 1,
        };
        db.StatementImports.Add(import);
        await db.SaveChangesAsync();
        return import.StatementImportId;
    }

    private const string FileBytesText = "PDF-bytes";

    private async Task<long> UploadAsync(int companyId, long parentId, long userId = 1, string parentType = "TAX_INVOICE")
    {
        await using var sp = Provider(companyId, userId);
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IAttachmentService>();
        var up = await svc.UploadAsync(parentType, parentId, "OTHER", "test upload",
            "bill.pdf", "application/pdf", Encoding.UTF8.GetByteCount(FileBytesText),
            new MemoryStream(Encoding.UTF8.GetBytes(FileBytesText)), default);
        return up.AttachmentId;
    }

    private async Task<int> RoleIdAsync(int companyId, string roleCode)
    {
        await using var sp = Provider(companyId);
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        return await db.Roles.Where(r => r.CompanyId == companyId && r.RoleCode == roleCode)
            .Select(r => r.RoleId).FirstAsync();
    }

    /// <summary>A custom per-company role holding exactly the given (already-seeded)
    /// permission codes — no new permission code, no seed/migration change; wires
    /// EXISTING catalog permissions the same way the real RBAC admin UI would.</summary>
    private async Task<int> CreateCustomRoleAsync(int companyId, string roleCode, params string[] permCodes)
    {
        await using var sp = Provider(companyId);
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var role = new Role
        { CompanyId = companyId, RoleCode = roleCode, RoleName = roleCode, IsSystem = false };
        db.Roles.Add(role);
        await db.SaveChangesAsync();

        var permIds = await db.Permissions.AsNoTracking()
            .Where(p => permCodes.Contains(p.PermissionCode))
            .Select(p => p.PermissionId).ToListAsync();
        foreach (var pid in permIds)
            db.RolePermissions.Add(new RolePermission
            { RoleId = role.RoleId, PermissionId = pid, CompanyId = companyId });
        await db.SaveChangesAsync();
        return role.RoleId;
    }

    /// <summary>A real sys.users row with a UserRole in <paramref name="companyId"/> — the
    /// shape IPermissionLookup.LoadAsync needs (mirrors DocSignatureWp1Wp2Tests.CreateUserAsync).</summary>
    private async Task<long> CreateUserAsync(int companyId, params int[] roleIds)
    {
        await using var sp = Provider(companyId);
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
            UserId: userId, Username: $"attgd-{userId}", CompanyId: companyId, BranchId: 1,
            IsSuperAdmin: false, Roles: [], Permissions: perms.ToList())).Token;

    private static async Task<HttpResponseMessage> GetDownloadAsync(HttpClient client, string token, long id)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"/attachments/{id}/download");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(req);
    }

    private static async Task<HttpResponseMessage> DeleteAttachmentAsync(HttpClient client, string token, long id)
    {
        using var req = new HttpRequestMessage(HttpMethod.Delete, $"/attachments/{id}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(req);
    }

    private static async Task<HttpResponseMessage> GetListAsync(
        HttpClient client, string token, string parentType, long parentId)
    {
        using var req = new HttpRequestMessage(
            HttpMethod.Get, $"/attachments/?parent_type={parentType}&parent_id={parentId}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(req);
    }

    // ═══════════════════════ download ═══════════════════════════════════════════

    [SkippableFact]
    public async Task Download_denies_reader_without_the_parents_read_permission()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var tiId = await PostTaxInvoiceAsync(co.CompanyId, co.CustomerId);
        var attachmentId = await UploadAsync(co.CompanyId, tiId);

        // AP_CLERK: real DB role, granted sys.attachment.read (280_seed_attachment_perms.sql —
        // "every role") but NOT sales.tax_invoice.read (320_seed_chapter3_rbac.sql grants that
        // only to COMPANY_ADMIN/CHIEF_ACCOUNTANT/ACCOUNTANT/AR_CLERK/SALES_STAFF/AUDITOR).
        var apRole = await RoleIdAsync(co.CompanyId, Role.SystemRoles.ApClerk);
        var apUserId = await CreateUserAsync(co.CompanyId, apRole);

        await using var factory = new RbacApiFactory(_fx.ConnectionString, storageRoot: _root);
        using var client = factory.CreateClient();
        var token = Token(apUserId, co.CompanyId, ["sys.attachment.read"]);

        using var resp = await GetDownloadAsync(client, token, attachmentId);
        // Tier-2 (R3/H4 remediation) — the deny path always returns 403 (ParentGuard), never
        // 404; BeOneOf([403,404]) was loose enough to pass for the wrong reason.
        ((int)resp.StatusCode).Should().Be(403,
            "sys.attachment.read is broadly granted, but the caller cannot read the parent tax invoice");
    }

    [SkippableFact]
    public async Task Download_succeeds_for_a_reader_who_holds_the_parents_read_permission()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var tiId = await PostTaxInvoiceAsync(co.CompanyId, co.CustomerId);
        var attachmentId = await UploadAsync(co.CompanyId, tiId);

        var accountantRole = await RoleIdAsync(co.CompanyId, Role.SystemRoles.Accountant);
        var accountantUserId = await CreateUserAsync(co.CompanyId, accountantRole);

        await using var factory = new RbacApiFactory(_fx.ConnectionString, storageRoot: _root);
        using var client = factory.CreateClient();
        var token = Token(accountantUserId, co.CompanyId, ["sys.attachment.read"]);

        using var resp = await GetDownloadAsync(client, token, attachmentId);
        ((int)resp.StatusCode).Should().Be(200, "ACCOUNTANT holds sales.tax_invoice.read — the fix must not break the normal path");
        var bytes = await resp.Content.ReadAsByteArrayAsync();
        Encoding.UTF8.GetString(bytes).Should().Be(FileBytesText);
    }

    [SkippableFact]
    public async Task Download_cross_company_attachment_id_is_not_found()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var coA = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var coB = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var tiId = await PostTaxInvoiceAsync(coA.CompanyId, coA.CustomerId);
        var attachmentId = await UploadAsync(coA.CompanyId, tiId);

        // Company-B caller, who WOULD hold the parent perm — but in their own company.
        var accountantRoleB = await RoleIdAsync(coB.CompanyId, Role.SystemRoles.Accountant);
        var userB = await CreateUserAsync(coB.CompanyId, accountantRoleB);

        await using var factory = new RbacApiFactory(_fx.ConnectionString, storageRoot: _root);
        using var client = factory.CreateClient();
        var token = Token(userB, coB.CompanyId, ["sys.attachment.read"]);

        using var resp = await GetDownloadAsync(client, token, attachmentId);
        ((int)resp.StatusCode).Should().Be(404, "cross-company ids must still 404 (global query filter) — unchanged by this fix");
    }

    // ═════════════════ Tier-2 FIX 1 — brand/identity assets stay tenant-wide readable ══════

    /// <summary>COMPANY_PROFILE/COMPANY_STAMP/USER_SIGNATURE's ParentReadPermission entries
    /// (master.company.manage / master.company_profile.manage / sys.user.manage) gate who may
    /// CHANGE the asset — reusing them as the DOWNLOAD gate 403s the company logo/stamp/
    /// signature for nearly every non-admin user, including COMPANY_ADMIN (master.company.manage
    /// is SUPER_ADMIN-only, 530_seed_rbac_grant_reconcile.sql). ACCOUNTANT holds NONE of the
    /// three manage perms, proving the exemption, not just a coincidentally-broad grant.</summary>
    [SkippableTheory]
    [InlineData("COMPANY_PROFILE")]
    [InlineData("COMPANY_STAMP")]
    [InlineData("USER_SIGNATURE")]
    public async Task Download_of_a_brand_asset_succeeds_for_a_user_holding_no_admin_permission(string parentType)
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var accountantRole = await RoleIdAsync(co.CompanyId, Role.SystemRoles.Accountant);
        var accountantUserId = await CreateUserAsync(co.CompanyId, accountantRole);

        var parentId = parentType == "USER_SIGNATURE" ? accountantUserId : co.CompanyId;
        var attachmentId = await UploadAsync(co.CompanyId, parentId, accountantUserId, parentType);

        await using var factory = new RbacApiFactory(_fx.ConnectionString, storageRoot: _root);
        using var client = factory.CreateClient();
        var token = Token(accountantUserId, co.CompanyId, ["sys.attachment.read"]);

        using var resp = await GetDownloadAsync(client, token, attachmentId);
        ((int)resp.StatusCode).Should().Be(200,
            $"{parentType} is a tenant-wide brand/identity asset rendered on every page for every " +
            "role — its ParentReadPermission entry gates who may CHANGE it, not who may VIEW it");
    }

    /// <summary>The download exemption above must NOT leak into delete — removing a brand
    /// asset stays exactly the manage question the ParentReadPermission entries encode.</summary>
    [SkippableTheory]
    [InlineData("COMPANY_PROFILE")]
    [InlineData("COMPANY_STAMP")]
    [InlineData("USER_SIGNATURE")]
    public async Task Delete_of_a_brand_asset_is_still_denied_for_a_user_holding_no_admin_permission(string parentType)
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var accountantRole = await RoleIdAsync(co.CompanyId, Role.SystemRoles.Accountant);
        var accountantUserId = await CreateUserAsync(co.CompanyId, accountantRole);

        var parentId = parentType == "USER_SIGNATURE" ? accountantUserId : co.CompanyId;
        var attachmentId = await UploadAsync(co.CompanyId, parentId, accountantUserId, parentType);

        await using var factory = new RbacApiFactory(_fx.ConnectionString, storageRoot: _root);
        using var client = factory.CreateClient();
        var token = Token(accountantUserId, co.CompanyId, ["sys.attachment.read"]);

        using var resp = await DeleteAttachmentAsync(client, token, attachmentId);
        ((int)resp.StatusCode).Should().Be(403,
            "the download exemption is download-only — ACCOUNTANT holds none of " +
            "master.company.manage / master.company_profile.manage / sys.user.manage");
    }

    // ═════════════ Tier-2 FIX 2 — the other 2/3 of the original hole (Receipt/CN-DN/BankStatement) ══

    /// <summary>Before the fix, RECEIPT fell through ParentReadPermission's `_ => null` — ANY
    /// holder of the broadly-granted sys.attachment.read could download ANY receipt's
    /// attachment. AP_CLERK holds sys.attachment.read but NOT sales.receipt.read
    /// (330_seed_receipt_adjnote_rbac.sql) — the same shape as the original TAX_INVOICE test.</summary>
    [SkippableFact]
    public async Task Download_denies_reader_without_receipts_own_read_permission()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var tiId = await PostTaxInvoiceAsync(co.CompanyId, co.CustomerId);
        var rcId = await CreateDraftReceiptAsync(co.CompanyId, co.CustomerId, tiId);
        var attachmentId = await UploadAsync(co.CompanyId, rcId, parentType: "RECEIPT");

        var apRole = await RoleIdAsync(co.CompanyId, Role.SystemRoles.ApClerk);
        var apUserId = await CreateUserAsync(co.CompanyId, apRole);

        await using var factory = new RbacApiFactory(_fx.ConnectionString, storageRoot: _root);
        using var client = factory.CreateClient();
        var token = Token(apUserId, co.CompanyId, ["sys.attachment.read"]);

        using var resp = await GetDownloadAsync(client, token, attachmentId);
        ((int)resp.StatusCode).Should().Be(403,
            "sys.attachment.read is broadly granted, but AP_CLERK holds no sales.receipt.read");
    }

    /// <summary>No dead end — every role that already holds sales.receipt.read (the read-tier
    /// list in 330_seed_receipt_adjnote_rbac.sql: COMPANY_ADMIN/CHIEF_ACCOUNTANT/ACCOUNTANT/
    /// AR_CLERK/SALES_STAFF/AUDITOR) can still see a receipt's attachment after the fix.</summary>
    [SkippableFact]
    public async Task Download_succeeds_for_a_reader_who_holds_receipt_read_permission()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var tiId = await PostTaxInvoiceAsync(co.CompanyId, co.CustomerId);
        var rcId = await CreateDraftReceiptAsync(co.CompanyId, co.CustomerId, tiId);
        var attachmentId = await UploadAsync(co.CompanyId, rcId, parentType: "RECEIPT");

        var arRole = await RoleIdAsync(co.CompanyId, Role.SystemRoles.ArClerk);
        var arUserId = await CreateUserAsync(co.CompanyId, arRole);

        await using var factory = new RbacApiFactory(_fx.ConnectionString, storageRoot: _root);
        using var client = factory.CreateClient();
        var token = Token(arUserId, co.CompanyId, ["sys.attachment.read"]);

        using var resp = await GetDownloadAsync(client, token, attachmentId);
        ((int)resp.StatusCode).Should().Be(200, "AR_CLERK holds sales.receipt.read — the fix must not break the normal path");
    }

    /// <summary>Before the fix, TAX_ADJUSTMENT_NOTE (CN+DN) fell through to `_ => null` too.
    /// Mapped to sales.tax_invoice.read (no single code covers both credit_note.read AND
    /// debit_note.read, and ParentReadPermission is keyed by parent TYPE alone, not the note's
    /// own NoteType) — AP_CLERK holds neither tax_invoice.read nor credit/debit_note.read
    /// (320/330 seed grant the identical role list to all three), so this is a valid probe
    /// regardless of which of the three the fix had picked.</summary>
    [SkippableFact]
    public async Task Download_denies_reader_without_tax_adjustment_notes_read_permission()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var tiId = await PostTaxInvoiceAsync(co.CompanyId, co.CustomerId);
        var noteId = await CreateDraftCreditNoteAsync(co.CompanyId, tiId);
        var attachmentId = await UploadAsync(co.CompanyId, noteId, parentType: "TAX_ADJUSTMENT_NOTE");

        var apRole = await RoleIdAsync(co.CompanyId, Role.SystemRoles.ApClerk);
        var apUserId = await CreateUserAsync(co.CompanyId, apRole);

        await using var factory = new RbacApiFactory(_fx.ConnectionString, storageRoot: _root);
        using var client = factory.CreateClient();
        var token = Token(apUserId, co.CompanyId, ["sys.attachment.read"]);

        using var resp = await GetDownloadAsync(client, token, attachmentId);
        ((int)resp.StatusCode).Should().Be(403,
            "sys.attachment.read is broadly granted, but AP_CLERK holds no sales.tax_invoice.read " +
            "(nor sales.credit_note.read/sales.debit_note.read)");
    }

    /// <summary>No dead end — ACCOUNTANT holds sales.tax_invoice.read AND sales.credit_note.read
    /// (320/330 seed — identical role list), so the choice of tax_invoice.read as the mapping
    /// doesn't strand anyone who could already see CN/DN documents.</summary>
    [SkippableFact]
    public async Task Download_succeeds_for_a_reader_who_holds_tax_invoice_read_permission_for_a_cn()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var tiId = await PostTaxInvoiceAsync(co.CompanyId, co.CustomerId);
        var noteId = await CreateDraftCreditNoteAsync(co.CompanyId, tiId);
        var attachmentId = await UploadAsync(co.CompanyId, noteId, parentType: "TAX_ADJUSTMENT_NOTE");

        var accountantRole = await RoleIdAsync(co.CompanyId, Role.SystemRoles.Accountant);
        var accountantUserId = await CreateUserAsync(co.CompanyId, accountantRole);

        await using var factory = new RbacApiFactory(_fx.ConnectionString, storageRoot: _root);
        using var client = factory.CreateClient();
        var token = Token(accountantUserId, co.CompanyId, ["sys.attachment.read"]);

        using var resp = await GetDownloadAsync(client, token, attachmentId);
        ((int)resp.StatusCode).Should().Be(200, "ACCOUNTANT holds sales.tax_invoice.read — the fix must not break the normal path");
    }

    /// <summary>Before the fix, BANK_STATEMENT fell through to `_ => null` too — mapped to
    /// bank.statement.import, matching StatementImportEndpoints' OWN list/lines routes (the
    /// real "view bank statement data" gate already used in this app), not bank.reconcile
    /// (matching actions) or bank.report.read (the aggregate report). AP_CLERK holds
    /// sys.attachment.read but no bank.* permission at all (615_seed_bank_rec_perms.sql grants
    /// only COMPANY_ADMIN/CHIEF_ACCOUNTANT/ACCOUNTANT).</summary>
    [SkippableFact]
    public async Task Download_denies_reader_without_bank_statement_import_permission()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var importId = await CreateBankStatementImportAsync(co.CompanyId, co.BranchId);
        var attachmentId = await UploadAsync(co.CompanyId, importId, parentType: "BANK_STATEMENT");

        var apRole = await RoleIdAsync(co.CompanyId, Role.SystemRoles.ApClerk);
        var apUserId = await CreateUserAsync(co.CompanyId, apRole);

        await using var factory = new RbacApiFactory(_fx.ConnectionString, storageRoot: _root);
        using var client = factory.CreateClient();
        var token = Token(apUserId, co.CompanyId, ["sys.attachment.read"]);

        using var resp = await GetDownloadAsync(client, token, attachmentId);
        ((int)resp.StatusCode).Should().Be(403,
            "sys.attachment.read is broadly granted, but AP_CLERK holds no bank.statement.import");
    }

    /// <summary>No dead end — StatementImportService.ImportAsync calls IAttachmentService
    /// .UploadAsync DIRECTLY (bypassing this HTTP endpoint's ParentGuard entirely), and the
    /// only roles that can reach the real import endpoint already hold bank.statement.import —
    /// so nobody who can create a bank-statement attachment today loses the ability to see it.</summary>
    [SkippableFact]
    public async Task Download_succeeds_for_a_reader_who_holds_bank_statement_import_permission()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var importId = await CreateBankStatementImportAsync(co.CompanyId, co.BranchId);
        var attachmentId = await UploadAsync(co.CompanyId, importId, parentType: "BANK_STATEMENT");

        var accountantRole = await RoleIdAsync(co.CompanyId, Role.SystemRoles.Accountant);
        var accountantUserId = await CreateUserAsync(co.CompanyId, accountantRole);

        await using var factory = new RbacApiFactory(_fx.ConnectionString, storageRoot: _root);
        using var client = factory.CreateClient();
        var token = Token(accountantUserId, co.CompanyId, ["sys.attachment.read"]);

        using var resp = await GetDownloadAsync(client, token, attachmentId);
        ((int)resp.StatusCode).Should().Be(200, "ACCOUNTANT holds bank.statement.import — the fix must not break the normal path");
    }

    /// <summary>The list route (GET /attachments/?parent_type=&amp;parent_id=) shares
    /// ParentReadPermission with download — the enumeration oracle the reviewer flagged
    /// (ids/filenames/sizes/uploader names) must close in the SAME fix, not just download.</summary>
    [SkippableFact]
    public async Task List_denies_reader_without_receipts_own_read_permission()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var tiId = await PostTaxInvoiceAsync(co.CompanyId, co.CustomerId);
        var rcId = await CreateDraftReceiptAsync(co.CompanyId, co.CustomerId, tiId);
        await UploadAsync(co.CompanyId, rcId, parentType: "RECEIPT");

        var apRole = await RoleIdAsync(co.CompanyId, Role.SystemRoles.ApClerk);
        var apUserId = await CreateUserAsync(co.CompanyId, apRole);

        await using var factory = new RbacApiFactory(_fx.ConnectionString, storageRoot: _root);
        using var client = factory.CreateClient();
        var token = Token(apUserId, co.CompanyId, ["sys.attachment.read"]);

        using var resp = await GetListAsync(client, token, "RECEIPT", rcId);
        ((int)resp.StatusCode).Should().Be(403,
            "the list route must close the same enumeration hole as download");
    }

    // ═══════════════════════ delete ═══════════════════════════════════════════

    [SkippableFact]
    public async Task Delete_denies_a_global_delete_holder_without_the_parents_read_permission()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var tiId = await PostTaxInvoiceAsync(co.CompanyId, co.CustomerId);
        var attachmentId = await UploadAsync(co.CompanyId, tiId);

        // A custom per-company role holding ONLY sys.attachment.delete — no read/manage
        // permission on any document parent type. None of the SEEDED roles combine
        // sys.attachment.delete without also holding sales.tax_invoice.read (SUPER_ADMIN/
        // COMPANY_ADMIN/CHIEF_ACCOUNTANT all do), so this is built directly to prove the
        // structural gap: real per-company RBAC lets an admin grant this exact combination.
        var cleanupRoleId = await CreateCustomRoleAsync(co.CompanyId, "ATTACH_CLEANUP_ONLY", "sys.attachment.delete");
        var cleanupUserId = await CreateUserAsync(co.CompanyId, cleanupRoleId);

        await using var factory = new RbacApiFactory(_fx.ConnectionString, storageRoot: _root);
        using var client = factory.CreateClient();
        var token = Token(cleanupUserId, co.CompanyId, ["sys.attachment.read"]);

        using var resp = await DeleteAttachmentAsync(client, token, attachmentId);
        // Tier-2 (R3/H4 remediation) — the deny path always returns 403 (ParentGuard), never
        // 404; BeOneOf([403,404]) was loose enough to pass for the wrong reason.
        ((int)resp.StatusCode).Should().Be(403,
            "sys.attachment.delete does not imply visibility into the tax invoice this file is attached to");

        await using var sp = Provider(co.CompanyId);
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        (await db.Attachments.AsNoTracking().Where(a => a.AttachmentId == attachmentId)
                .Select(a => a.DeletedAt).FirstAsync())
            .Should().BeNull("the denied delete must not soft-delete the row");
    }

    [SkippableFact]
    public async Task Delete_lets_the_uploader_remove_their_own_upload_without_the_global_delete_permission()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var tiId = await PostTaxInvoiceAsync(co.CompanyId, co.CustomerId);

        // ACCOUNTANT holds sales.tax_invoice.read (the parent perm) but NOT
        // sys.attachment.delete (280_seed_attachment_perms.sql grants delete only to
        // SUPER_ADMIN/COMPANY_ADMIN/CHIEF_ACCOUNTANT) — exercises AttachmentService
        // .SoftDeleteAsync's "own upload" carve-out, which the new parent guard must not
        // break for the normal case (permission unchanged since upload).
        var accountantRole = await RoleIdAsync(co.CompanyId, Role.SystemRoles.Accountant);
        var accountantUserId = await CreateUserAsync(co.CompanyId, accountantRole);
        var attachmentId = await UploadAsync(co.CompanyId, tiId, userId: accountantUserId);

        await using var factory = new RbacApiFactory(_fx.ConnectionString, storageRoot: _root);
        using var client = factory.CreateClient();
        var token = Token(accountantUserId, co.CompanyId, ["sys.attachment.read"]);

        using var resp = await DeleteAttachmentAsync(client, token, attachmentId);
        ((int)resp.StatusCode).Should().Be(204,
            "the uploader may delete their own file even without sys.attachment.delete — unchanged by this fix");
    }

    // ═══════════════ R3/H2 — upload size limit (verify-in-source, do not touch guard logic) ═══
    // AttachmentEndpoints.cs:72-74 already checks file.Length against FileStorage:MaxFileSizeMb
    // (25 in appsettings.Development.json) and returns 413 BEFORE any DB/parent-guard work. The
    // VERDICT reported the advertised 25MB limit unreachable (>5MB already 500s) — verify via a
    // real HTTP round-trip through the actual Minimal API route (RbacApiFactory/TestServer) which
    // one of these two outcomes: (a) a file just over the 25MB app-level limit gets a clean 413,
    // and (b) a file comfortably over 5MB but under 25MB uploads successfully — proving the
    // >5MB-fails behaviour is not reproducible from this application's own source.

    private static string SuperAdminToken(long userId, int companyId) =>
        new JwtTokenIssuer(new StaticOptionsMonitor<JwtOptions>(new JwtOptions
        {
            Issuer = RbacApiFactory.JwtIssuer, Audience = RbacApiFactory.JwtAudience,
            SigningKey = RbacApiFactory.JwtSigningKey, AccessTokenMinutes = 60,
        })).Issue(new TokenClaims(
            UserId: userId, Username: $"attsz-{userId}", CompanyId: companyId, BranchId: 1,
            IsSuperAdmin: true, Roles: [], Permissions: [])).Token;

    private static MultipartFormDataContent BuildUploadForm(
        byte[] bytes, long parentId, string fileName = "big.pdf")
    {
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        return new MultipartFormDataContent
        {
            { fileContent, "file", fileName },
            { new StringContent("TAX_INVOICE"), "parent_type" },
            { new StringContent(parentId.ToString()), "parent_id" },
            { new StringContent("OTHER"), "category" },
            { new StringContent("size-limit test upload"), "description" },
        };
    }

    [SkippableFact]
    public async Task Upload_over_the_configured_limit_returns_a_clean_413_not_a_raw_500()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var tiId = await PostTaxInvoiceAsync(co.CompanyId, co.CustomerId);

        await using var factory = new RbacApiFactory(_fx.ConnectionString, storageRoot: _root);
        using var client = factory.CreateClient();
        var token = SuperAdminToken(1, co.CompanyId);

        // 26MB — just over the 25MB FileStorage:MaxFileSizeMb configured in appsettings.Development.json.
        var oversized = new byte[26 * 1024 * 1024];
        using var form = BuildUploadForm(oversized, tiId);
        using var req = new HttpRequestMessage(HttpMethod.Post, "/attachments") { Content = form };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var resp = await client.SendAsync(req);
        ((int)resp.StatusCode).Should().Be(413,
            "a file over the configured limit must get a clean 413, never an unmapped 500");
    }

    [SkippableFact]
    public async Task Upload_of_a_6mb_file_well_under_the_25mb_limit_succeeds()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var tiId = await PostTaxInvoiceAsync(co.CompanyId, co.CustomerId);

        await using var factory = new RbacApiFactory(_fx.ConnectionString, storageRoot: _root);
        using var client = factory.CreateClient();
        var token = SuperAdminToken(1, co.CompanyId);

        // 6MB — well over the VERDICT's reported >5MB failure threshold, well under the
        // advertised 25MB limit.
        var sixMb = new byte[6 * 1024 * 1024];
        using var form = BuildUploadForm(sixMb, tiId);
        using var req = new HttpRequestMessage(HttpMethod.Post, "/attachments") { Content = form };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var resp = await client.SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();
        ((int)resp.StatusCode).Should().Be(201, "a 6MB file is well within the advertised 25MB limit and " +
            $"must upload cleanly. Response: {body}");
    }
}
