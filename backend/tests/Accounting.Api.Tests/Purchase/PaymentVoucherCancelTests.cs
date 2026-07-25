using System.Net;
using System.Net.Http.Headers;
using Accounting.Api.Authorization;
using Accounting.Api.Tests.Fixtures;
using Accounting.Api.Tests.Rbac;
using Accounting.Application.Abstractions;
using Accounting.Application.Purchase;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Master;
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

namespace Accounting.Api.Tests.Purchase;

/// <summary>
/// WP-B (specs/fix-army-findings-2026-07-22.md, army B-bn F1) —
/// (a) a line with WhtRate&gt;0 but no resolvable Income-Type (50ทวิ) must be rejected at
///     Draft-save AND at Approve, not just at the pre-existing Post-time check (live repro:
///     PV #19 co5, stuck Approved with no way back).
/// (b) CancelAsync — Draft/Approved escape hatch (-&gt; Voided); Posted throws;
///     a VI-linked PV's cancel frees the VI for a fresh PV (settlement is POST-only, so a
///     Draft/Approved PV never touched it). Opus Tier-2 (2026-07-25) rejected the original
///     Approved-only design (F2 — a legacy bad Draft welded shut by the ApproveAsync re-assert
///     had no way out, blocking PeriodCloseService forever) and flagged the Version token as
///     inert (F1 — no PaymentVoucher transition ever bumped it, so CancelAsync's own
///     DbUpdateConcurrencyException catch was dead code); both are covered below.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class PaymentVoucherCancelTests
{
    private readonly PostgresFixture _fx;
    public PaymentVoucherCancelTests(PostgresFixture fx) => _fx = fx;

    private ServiceProvider Provider(int companyId = 1, long userId = 1)
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:Postgres"] = _fx.ConnectionString }).Build();
        var s = new ServiceCollection();
        s.AddLogging();
        return s.AddInfrastructure(cfg)
            .AddSingleton<ITenantContext>(new StubTenant
            { CompanyId = companyId, BranchId = 1, UserId = userId, IsSuperAdmin = false })
            .BuildServiceProvider();
    }

    private static async Task<long> NewVendor(ServiceProvider sp)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var v = new Vendor
        {
            CompanyId = 1, VendorCode = TestIds.VendorCode(), NameTh = "ผู้ขายทดสอบยกเลิก",
            TaxId = TestIds.TaxId(), BranchCode = "00000",
            VendorType = CustomerType.Corporate, IsForeign = false, VatRegistered = true,
        };
        db.Vendors.Add(v);
        await db.SaveChangesAsync();
        return v.VendorId;
    }

    /// <summary>No DefaultWhtTypeId set — mirrors the live PV #19 repro (category leaves the
    /// per-line Income-Type unresolved unless the caller supplies one explicitly).</summary>
    private static async Task<(int catId, long expAcct)> NewExpenseCategory(ServiceProvider sp)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var expAcct = await db.ChartOfAccounts.Where(a => a.CompanyId == 1 && a.AccountCode == "5200")
            .Select(a => a.AccountId).FirstAsync();
        var cat = new ExpenseCategory
        {
            CompanyId = 1, CategoryCode = TestIds.ExpenseCategoryCode(),
            NameTh = "หมวดทดสอบยกเลิก", DefaultExpenseAccountId = expAcct,
            DefaultIsRecoverableVat = true,
        };
        db.ExpenseCategories.Add(cat);
        await db.SaveChangesAsync();
        return (cat.CategoryId, expAcct);
    }

    private static CreatePaymentVoucherRequest PvReq(
        long vendorId, int catId, long expAcct, int? whtTypeId = null, decimal whtRate = 0m) =>
        new(
            DocDate: new DateOnly(2026, 5, 16), VendorId: vendorId, ExpenseCategoryId: catId,
            PaymentMethod: PaymentMethod.Transfer, ChequeNo: null, ChequeDate: null,
            BankAccountId: null, CurrencyCode: "THB", ExchangeRate: 1m,
            Description: "x", Notes: null,
            Lines: [new PaymentVoucherLineInput(expAcct, "l", 1000m, null, 0m, false, whtTypeId, whtRate)]);

    // ── (a) validation seam ─────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Draft_save_rejects_a_wht_line_with_no_income_type()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var vid = await NewVendor(sp);
        var (catId, expAcct) = await NewExpenseCategory(sp);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPaymentVoucherService>();
        // whtRate 3%, whtTypeId null, category has no DefaultWhtTypeId → unresolvable.
        var act = async () => await svc.CreateDraftAsync(PvReq(vid, catId, expAcct, whtTypeId: null, whtRate: 0.03m), default);
        (await act.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("pv.wht_type_missing");
    }

    [SkippableFact]
    public async Task Draft_save_allows_a_wht_line_with_an_explicit_income_type()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var vid = await NewVendor(sp);
        var (catId, expAcct) = await NewExpenseCategory(sp);
        var whtTypeId = await SvcWhtTypeId(sp);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPaymentVoucherService>();
        var pvId = await svc.CreateDraftAsync(PvReq(vid, catId, expAcct, whtTypeId, 0.03m), default);
        pvId.Should().BeGreaterThan(0);
    }

    private static async Task<int?> SvcWhtTypeId(ServiceProvider sp)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        return await db.WhtTypes.Where(w => w.Code == "SVC" && w.EffectiveTo == null)
            .Select(w => (int?)w.WhtTypeId).FirstOrDefaultAsync();
    }

    [SkippableFact]
    public async Task Approve_rejects_an_already_persisted_bad_draft()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var vid = await NewVendor(sp);
        var (catId, expAcct) = await NewExpenseCategory(sp);

        // Simulate a row that predates the CreateDraftAsync guard (or was written directly) —
        // the only way a bad draft can exist once the create-time guard is live.
        long pvId;
        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var pv = new Accounting.Domain.Entities.Purchase.PaymentVoucher
            {
                CompanyId = 1, BranchId = 1, SubPrefix = "GEN",
                DocDate = new(2026, 5, 16), PostingDate = new(2026, 5, 16),
                VendorId = vid, ExpenseCategoryId = catId, VendorName = "bad-draft-vendor",
                VendorType = CustomerType.Corporate, PaymentMethod = PaymentMethod.Transfer,
                SubtotalAmount = 1000m, TotalPaid = 1000m, TotalAmountThb = 1000m,
                Lines =
                [
                    new Accounting.Domain.Entities.Purchase.PaymentVoucherLine
                    {
                        LineNo = 1, ExpenseAccountId = expAcct, Description = "l",
                        Amount = 1000m, WhtTypeId = null, WhtRate = 0.03m, WhtAmount = 30m,
                    },
                ],
            };
            db.PaymentVouchers.Add(pv);
            await db.SaveChangesAsync();
            pvId = pv.PaymentVoucherId;
        }

        await using var s2 = sp.CreateAsyncScope();
        var svc = s2.ServiceProvider.GetRequiredService<IPaymentVoucherService>();
        var act = async () => await svc.ApproveAsync(pvId, default);
        (await act.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("pv.wht_type_missing");
    }

    // ── (b) cancel escape hatch ──────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Approve_then_cancel_voids_the_pv()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var vid = await NewVendor(sp);
        var (catId, expAcct) = await NewExpenseCategory(sp);

        long pvId;
        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<IPaymentVoucherService>();
            pvId = await svc.CreateDraftAsync(PvReq(vid, catId, expAcct), default);
            await svc.ApproveAsync(pvId, default);
            await svc.CancelAsync(pvId, default);
        }

        await using var s2 = sp.CreateAsyncScope();
        var db = s2.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var pv = await db.PaymentVouchers.AsNoTracking().FirstAsync(p => p.PaymentVoucherId == pvId);
        pv.Status.Should().Be(DocumentStatus.Voided);
        pv.DocNo.Should().BeNull("DocNo is allocated at Post — a cancelled Approved PV wastes no number");
    }

    /// <summary>Opus Tier-2 F2 (2026-07-25) — a Draft PV must be cancellable too: the new B1(a)
    /// ApproveAsync re-assert can weld a legacy bad Draft (rate&gt;0/no-type) shut forever (no PV
    /// update/delete endpoint exists to fix it in place), which would then block
    /// PeriodCloseService on that month indefinitely (`period.draft_present`). A Draft PV has no
    /// JE/DocNo, so cancelling it is exactly as safe as cancelling an Approved one.</summary>
    [SkippableFact]
    public async Task Cancel_a_draft_pv_voids_it_with_dynamic_from_status()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var vid = await NewVendor(sp);
        var (catId, expAcct) = await NewExpenseCategory(sp);

        long pvId;
        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<IPaymentVoucherService>();
            pvId = await svc.CreateDraftAsync(PvReq(vid, catId, expAcct), default);
            await svc.CancelAsync(pvId, default);
        }

        await using var s2 = sp.CreateAsyncScope();
        var db = s2.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var pv = await db.PaymentVouchers.AsNoTracking().FirstAsync(p => p.PaymentVoucherId == pvId);
        pv.Status.Should().Be(DocumentStatus.Voided);
        // Opus Tier-2 F2 — the activity record's fromStatus must be the ACTUAL prior status
        // ("Draft" here), not a hardcoded "Approved" left over from the original design.
        var row = await db.Set<Accounting.Domain.Entities.Audit.ActivityLog>().AsNoTracking()
            .SingleAsync(a => a.EntityType == "PaymentVoucher" && a.EntityId == pvId
                            && a.ActivityType == "Voided");
        using var meta = System.Text.Json.JsonDocument.Parse(row.MetadataJson!);
        meta.RootElement.GetProperty("fromStatus").GetString().Should().Be("Draft");
        meta.RootElement.GetProperty("toStatus").GetString().Should().Be("Voided");
    }

    [SkippableFact]
    public async Task Cancel_a_posted_pv_is_rejected()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var vid = await NewVendor(sp);
        var (catId, expAcct) = await NewExpenseCategory(sp);

        long pvId;
        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<IPaymentVoucherService>();
            pvId = await svc.CreateDraftAsync(PvReq(vid, catId, expAcct), default);
            await svc.ApproveAsync(pvId, default);
            await svc.PostAsync(pvId, default);
        }

        await using var s2 = sp.CreateAsyncScope();
        var svc2 = s2.ServiceProvider.GetRequiredService<IPaymentVoucherService>();
        var act = async () => await svc2.CancelAsync(pvId, default);
        (await act.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("pv.cannot_cancel");
    }

    /// <summary>Opus Tier-2 F1 (2026-07-25) — Version was configured IsConcurrencyToken() but no
    /// PaymentVoucher transition ever incremented it, so CancelAsync's DbUpdateConcurrencyException
    /// catch was dead code (a cancel-vs-post race could mark a POSTED PV Voided — payment vanishes
    /// from bank-rec/ภ.พ.36 while its JE stays, and the VI becomes re-settleable — double payment).
    /// Same technique as ExpenseClaimServiceTests' Concurrent_Approve_second_stale_save_... tests
    /// (grepped per Fable's dispatch): a scoped DbContext + the service resolved from the SAME
    /// scope share one AccountingDbContext instance, so pre-loading the PV via the context
    /// directly BEFORE an out-of-band raw-SQL Version bump means CancelAsync's own internal
    /// re-query hits EF's identity resolution (returns the already-tracked, now-stale instance
    /// instead of re-reading) — its SaveChangesAsync then races the WHERE-version predicate
    /// against the bumped row and 0 rows match, going through the REAL service catch and proving
    /// the pv.locked_mismatch mapping end-to-end, not just the raw EF exception.</summary>
    [SkippableFact]
    public async Task Concurrent_version_bump_makes_cancel_throw_locked_mismatch()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var vid = await NewVendor(sp);
        var (catId, expAcct) = await NewExpenseCategory(sp);

        long pvId;
        await using (var s0 = sp.CreateAsyncScope())
        {
            var svc0 = s0.ServiceProvider.GetRequiredService<IPaymentVoucherService>();
            pvId = await svc0.CreateDraftAsync(PvReq(vid, catId, expAcct), default);
            await svc0.ApproveAsync(pvId, default);
        }

        await using var s1 = sp.CreateAsyncScope();
        var db1 = s1.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var svc1 = s1.ServiceProvider.GetRequiredService<IPaymentVoucherService>();
        // Pre-load into db1's change tracker at the CURRENT (post-Approve) Version — this is
        // the "original value" EF will use in CancelAsync's own SaveChangesAsync WHERE clause.
        _ = await db1.PaymentVouchers.FirstAsync(p => p.PaymentVoucherId == pvId);

        // Out-of-band write on a SEPARATE connection/context — simulates a concurrent completed
        // transition (e.g. a racing Post) bumping Version without going through db1's tracker.
        await using (var s2 = sp.CreateAsyncScope())
        {
            var db2 = s2.ServiceProvider.GetRequiredService<AccountingDbContext>();
            await db2.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE purchase.payment_vouchers SET version = version + 1 WHERE payment_voucher_id = {pvId}");
        }

        var act = async () => await svc1.CancelAsync(pvId, default);
        (await act.Should().ThrowAsync<DomainException>(
            "db1's tracked PV still carries the ORIGINAL Version loaded before the out-of-band " +
            "bump — before the F1 fix, no transition ever incremented Version, so this stale " +
            "write would have silently succeeded instead of conflicting"))
            .Which.Code.Should().Be("pv.locked_mismatch");
    }

    /// <summary>Opus Tier-2 F1 (2026-07-25) — the SAME race the entity-level docs warn about
    /// (a concurrent Post racing a Cancel) must also be caught on PostAsync's OWN save path, not
    /// just CancelAsync's: NumberedDocumentWriter.AllocateAndSaveAsync's catch only recognizes
    /// the doc_no 23505 collision (a distinct EF exception from DbUpdateConcurrencyException), so
    /// a genuine version conflict inside it would otherwise propagate as a raw, unmapped 500.
    /// Same preload/out-of-band-bump technique as the Cancel version above.</summary>
    [SkippableFact]
    public async Task Concurrent_version_bump_makes_post_throw_locked_mismatch()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var vid = await NewVendor(sp);
        var (catId, expAcct) = await NewExpenseCategory(sp);

        long pvId;
        await using (var s0 = sp.CreateAsyncScope())
        {
            var svc0 = s0.ServiceProvider.GetRequiredService<IPaymentVoucherService>();
            pvId = await svc0.CreateDraftAsync(PvReq(vid, catId, expAcct), default);
            await svc0.ApproveAsync(pvId, default);
        }

        await using var s1 = sp.CreateAsyncScope();
        var db1 = s1.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var svc1 = s1.ServiceProvider.GetRequiredService<IPaymentVoucherService>();
        _ = await db1.PaymentVouchers.FirstAsync(p => p.PaymentVoucherId == pvId);

        await using (var s2 = sp.CreateAsyncScope())
        {
            var db2 = s2.ServiceProvider.GetRequiredService<AccountingDbContext>();
            await db2.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE purchase.payment_vouchers SET version = version + 1 WHERE payment_voucher_id = {pvId}");
        }

        var act = async () => await svc1.PostAsync(pvId, default);
        (await act.Should().ThrowAsync<DomainException>(
            "before the F1 fix this either raised an unmapped raw DbUpdateConcurrencyException " +
            "(500) or, worse, silently succeeded — Version was never bumped by any transition"))
            .Which.Code.Should().Be("pv.locked_mismatch");
    }

    [SkippableFact]
    public async Task Vi_linked_pv_cancel_frees_the_vi_for_a_new_pv()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var vid = await NewVendor(sp);
        var (catId, expAcct) = await NewExpenseCategory(sp);

        long viId;
        await using (var s = sp.CreateAsyncScope())
        {
            var viSvc = s.ServiceProvider.GetRequiredService<IVendorInvoiceService>();
            viId = await viSvc.CreateDraftAsync(new CreateVendorInvoiceRequest(
                new DateOnly(2026, 5, 16), vid, "CXVI-" + TestIds.Suffix(),
                new SystemClock().TodayInBangkok(), null, "THB", 1m, null,
                [new VendorInvoiceLineInput(catId, expAcct, "svc", 1000m, 0m)]), default);
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            db.SeedViAttachment(viId); await db.SaveChangesAsync();
            await viSvc.PostAsync(viId, default);
        }

        // First PV settles the VI, gets Approved, then Cancelled — the guarded escape hatch.
        long firstPvId;
        await using (var s = sp.CreateAsyncScope())
        {
            var pvSvc = s.ServiceProvider.GetRequiredService<IPaymentVoucherService>();
            firstPvId = await pvSvc.CreateFromVendorInvoiceAsync(viId,
                new CreatePvFromViRequest(PaymentMethod.Transfer), default);
            await pvSvc.ApproveAsync(firstPvId, default);
            await pvSvc.CancelAsync(firstPvId, default);
        }

        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var firstPv = await db.PaymentVouchers.AsNoTracking().FirstAsync(p => p.PaymentVoucherId == firstPvId);
            firstPv.Status.Should().Be(DocumentStatus.Voided);
            var vi = await db.VendorInvoices.AsNoTracking().FirstAsync(v => v.VendorInvoiceId == viId);
            vi.SettledAmount.Should().Be(0m, "settlement only happens at Post — an Approved-then-Cancelled PV never touched the VI");
        }

        // A NEW PV against the SAME VI must now be creatable — the Voided PV no longer
        // counts as "active" per CreateFromVendorInvoiceAsync's `Status != Voided` guard.
        await using var s2 = sp.CreateAsyncScope();
        var pvSvc2 = s2.ServiceProvider.GetRequiredService<IPaymentVoucherService>();
        var secondPvId = await pvSvc2.CreateFromVendorInvoiceAsync(viId,
            new CreatePvFromViRequest(PaymentMethod.Transfer), default);
        secondPvId.Should().BeGreaterThan(0).And.NotBe(firstPvId);
    }

    // ── HTTP-level permission gate ───────────────────────────────────────────────────

    private static string Token(int companyId, int branchId, string[] permissions) => new JwtTokenIssuer(
        new StaticOptionsMonitor<JwtOptions>(new JwtOptions
        {
            Issuer = RbacApiFactory.JwtIssuer,
            Audience = RbacApiFactory.JwtAudience,
            SigningKey = RbacApiFactory.JwtSigningKey,
            AccessTokenMinutes = 60,
        }))
        .Issue(new TokenClaims(
            UserId: 1, Username: "pv-cancel-perm-tests", CompanyId: companyId, BranchId: branchId,
            IsSuperAdmin: false, Roles: ["test"], Permissions: permissions)).Token;

    [SkippableFact]
    public async Task Caller_lacking_approve_permission_is_403_on_cancel()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        var vid = await NewVendorInCompany(sp, co.CompanyId);
        var (catId, expAcct) = await NewExpenseCategoryInCompany(sp, co.CompanyId);

        long pvId;
        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<IPaymentVoucherService>();
            pvId = await svc.CreateDraftAsync(PvReq(vid, catId, expAcct), default);
            await svc.ApproveAsync(pvId, default);
        }

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();
        // create-only token: no purchase.payment_voucher.approve → cancel must 403.
        var token = Token(co.CompanyId, co.BranchId, [Permissions.Purchase.PaymentVoucherCreate]);
        var req = new HttpRequestMessage(HttpMethod.Post, $"/payment-vouchers/{pvId}/cancel");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static async Task<long> NewVendorInCompany(ServiceProvider sp, int companyId)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var v = new Vendor
        {
            CompanyId = companyId, VendorCode = TestIds.VendorCode(), NameTh = "ผู้ขายทดสอบสิทธิ์",
            TaxId = TestIds.TaxId(), BranchCode = "00000",
            VendorType = CustomerType.Corporate, IsForeign = false, VatRegistered = true,
        };
        db.Vendors.Add(v);
        await db.SaveChangesAsync();
        return v.VendorId;
    }

    private static async Task<(int catId, long expAcct)> NewExpenseCategoryInCompany(ServiceProvider sp, int companyId)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var expAcct = await db.ChartOfAccounts.Where(a => a.CompanyId == companyId && a.AccountCode == "5200")
            .Select(a => a.AccountId).FirstAsync();
        var cat = new ExpenseCategory
        {
            CompanyId = companyId, CategoryCode = TestIds.ExpenseCategoryCode(),
            NameTh = "หมวดทดสอบสิทธิ์", DefaultExpenseAccountId = expAcct,
            DefaultIsRecoverableVat = true,
        };
        db.ExpenseCategories.Add(cat);
        await db.SaveChangesAsync();
        return (cat.CategoryId, expAcct);
    }
}
