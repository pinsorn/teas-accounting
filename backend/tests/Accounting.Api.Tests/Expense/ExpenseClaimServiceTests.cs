using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Attachments;
using Accounting.Application.Bank;
using Accounting.Application.Expense;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Master;
using Accounting.Domain.Entities.Sys;
using Accounting.Domain.Enums;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Persistence;
using Accounting.TestKit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Expense;

/// <summary>
/// Cycle C (specs/expense-claims.md §6) — money invariants (worked example verbatim), state
/// transitions, race safety (Version++ actually fires, unlike PV's inert token), tenant scope
/// (EF query-filter, labelled per FOOTGUN 6 — not RLS), and attachment wiring. Dates are
/// ALWAYS today/future (IClock) per FOOTGUN 5 — never a hardcoded past period.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class ExpenseClaimServiceTests
{
    private readonly PostgresFixture _fx;
    public ExpenseClaimServiceTests(PostgresFixture fx) => _fx = fx;

    private static DateOnly Today => new SystemClock().TodayInBangkok();

    private async Task<(TestCompanyFactory.SeededCompany Co, ServiceProvider Sp)> SetupAsync()
    {
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        return (co, sp);
    }

    private static async Task<long> SeedEmployeeAsync(ServiceProvider sp, int companyId)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var e = new Employee
        {
            CompanyId = companyId,
            EmployeeCode = "EMP-" + TestIds.Suffix(),
            FirstNameTh = "สมชาย", LastNameTh = "ทดสอบ",
            NationalId = Random.Shared.NextInt64(1_000_000_000_000, 9_999_999_999_999).ToString(),
            HireDate = Today.AddYears(-1),
        };
        db.Employees.Add(e);
        await db.SaveChangesAsync();
        return e.EmployeeId;
    }

    /// <summary>New CoA account + a per-line ExpenseCategory pointing to it (mirrors
    /// PurchaseBusinessUnitTests.NewExpenseCategory but with a fresh account per category so
    /// the worked-example test can assert distinct Dr lines).</summary>
    private static async Task<(int CategoryId, long AccountId)> SeedCategoryAsync(
        ServiceProvider sp, int companyId, string accountCode, string nameTh)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var acct = new ChartOfAccount
        {
            CompanyId = companyId, AccountCode = accountCode, AccountNameTh = nameTh,
            AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit,
            IsActive = true, CreatedAt = DateTimeOffset.UtcNow,
        };
        db.ChartOfAccounts.Add(acct);
        await db.SaveChangesAsync();

        var cat = new ExpenseCategory
        {
            CompanyId = companyId, CategoryCode = TestIds.ExpenseCategoryCode(),
            NameTh = nameTh, DefaultExpenseAccountId = acct.AccountId,
            DefaultIsRecoverableVat = true,
        };
        db.ExpenseCategories.Add(cat);
        await db.SaveChangesAsync();
        return (cat.CategoryId, acct.AccountId);
    }

    private static async Task<int> SeedBankAccountAsync(ServiceProvider sp)
    {
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IBankAccountService>();
        return await svc.CreateAsync(new CreateBankAccountRequest(
            "KBANK", "Kasikornbank", "999-9-" + TestIds.Suffix(), null, null, null, "THB"), default);
    }

    private static async Task<long> AccountIdByCode(AccountingDbContext db, int companyId, string code) =>
        await db.ChartOfAccounts.Where(a => a.CompanyId == companyId && a.AccountCode == code)
            .Select(a => a.AccountId).FirstAsync();

    private static CreateExpenseClaimRequest Req(
        long employeeId, IReadOnlyList<ExpenseClaimLineInput> lines) =>
        new(employeeId, Today, "ทริปลูกค้า", null, lines);

    // ── Money invariants (§6) — worked example verbatim ────────────────────────────

    [SkippableFact]
    public async Task Pay_reproduces_the_worked_example_JE_exactly()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp) = await SetupAsync();
        var employeeId = await SeedEmployeeAsync(sp, co.CompanyId);
        var (travelCat, travelAcct) = await SeedCategoryAsync(sp, co.CompanyId, "6120", "ค่าเดินทาง");
        var (hotelCat, hotelAcct) = await SeedCategoryAsync(sp, co.CompanyId, "6130", "ค่าที่พัก");
        var (mealCat, mealAcct) = await SeedCategoryAsync(sp, co.CompanyId, "6140", "ค่ารับรอง");
        var bankAccountId = await SeedBankAccountAsync(sp);

        var req = Req(employeeId, [
            new ExpenseClaimLineInput(travelCat, null, "แท็กซี่", Today, 500.00m, null, 0m, true),
            new ExpenseClaimLineInput(hotelCat, null, "โรงแรม", Today, 1000.00m, null, 0.07m, true),
            new ExpenseClaimLineInput(mealCat, null, "เลี้ยงลูกค้า", Today, 200.00m, null, 0.07m, false),
        ]);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IExpenseClaimService>();
        var id = await svc.CreateDraftAsync(req, default);

        var detail = await svc.GetDetailAsync(id, default);
        detail!.SubtotalAmount.Should().Be(1700.00m);
        detail.VatAmount.Should().Be(84.00m);
        detail.TotalAmount.Should().Be(1784.00m);
        detail.DocNo.Should().BeNull();

        await svc.SubmitAsync(id, default);
        await svc.ApproveAsync(id, default);
        var paid = await svc.PayAsync(id, new PayExpenseClaimRequest("TRANSFER", bankAccountId), default);

        paid.TotalAmount.Should().Be(1784.00m);
        paid.DocNo.Should().Contain("EX", "the EX prefix is embedded in the MM-YYYY-EX-NNNN doc number, mirroring PV's MM-YYYY-PV-{CATEGORY}-NNNN shape");

        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var je = await db.JournalEntries.AsNoTracking().Include(j => j.Lines)
            .FirstAsync(j => j.JournalId == paid.JournalEntryId);

        je.TotalDebit.Should().Be(1784.00m);
        je.TotalCredit.Should().Be(1784.00m);
        je.Lines.Should().HaveCount(5, "3 expense lines + 1 recoverable Input VAT + 1 Cash/Bank credit");

        je.Lines.Should().ContainSingle(l => l.AccountId == travelAcct)
            .Which.DebitAmount.Should().Be(500.00m);
        je.Lines.Should().ContainSingle(l => l.AccountId == hotelAcct)
            .Which.DebitAmount.Should().Be(1000.00m);
        // Non-recoverable line: 200 net + 14 non-recoverable VAT folded into the expense Dr.
        je.Lines.Should().ContainSingle(l => l.AccountId == mealAcct)
            .Which.DebitAmount.Should().Be(214.00m);

        var inputVatAcctId = await AccountIdByCode(db, co.CompanyId, "1170");
        je.Lines.Should().ContainSingle(l => l.AccountId == inputVatAcctId)
            .Which.DebitAmount.Should().Be(70.00m, "only line 2 (hotel) is recoverable");

        var bankGlAcctId = await db.BankAccounts.AsNoTracking()
            .Where(b => b.BankAccountId == bankAccountId).Select(b => b.GlCashAccountId).FirstAsync();
        je.Lines.Should().ContainSingle(l => l.AccountId == bankGlAcctId)
            .Which.CreditAmount.Should().Be(1784.00m);

        // WHT: NONE — no WhtPayableAccount line, no 50-ทวิ certificate.
        var whtPayableAcctId = await AccountIdByCode(db, co.CompanyId, "2152");
        je.Lines.Should().NotContain(l => l.AccountId == whtPayableAcctId);
        (await db.Set<Accounting.Domain.Entities.Tax.WhtCertificate>()
            .CountAsync(w => w.CompanyId == co.CompanyId)).Should().Be(0);
    }

    [SkippableFact]
    public async Task Pay_with_CASH_credits_the_company_cash_account()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp) = await SetupAsync();
        var employeeId = await SeedEmployeeAsync(sp, co.CompanyId);
        var (catId, acctId) = await SeedCategoryAsync(sp, co.CompanyId, "6150", "ค่าใช้จ่ายเบ็ดเตล็ด");

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IExpenseClaimService>();
        var id = await svc.CreateDraftAsync(
            Req(employeeId, [new ExpenseClaimLineInput(catId, null, "ค่าจิปาถะ", Today, 300m, null, 0m, true)]),
            default);
        await svc.SubmitAsync(id, default);
        await svc.ApproveAsync(id, default);
        var paid = await svc.PayAsync(id, new PayExpenseClaimRequest("CASH", null), default);

        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var je = await db.JournalEntries.AsNoTracking().Include(j => j.Lines)
            .FirstAsync(j => j.JournalId == paid.JournalEntryId);
        var cashAcctId = await AccountIdByCode(db, co.CompanyId, "1110");
        je.Lines.Should().ContainSingle(l => l.AccountId == cashAcctId)
            .Which.CreditAmount.Should().Be(300m);
        je.Lines.Should().ContainSingle(l => l.AccountId == acctId)
            .Which.DebitAmount.Should().Be(300m);
    }

    // ── State transitions (§6) ──────────────────────────────────────────────────────

    [SkippableFact]
    public async Task State_transitions_happy_path_docno_and_journal_only_set_at_pay()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp) = await SetupAsync();
        var employeeId = await SeedEmployeeAsync(sp, co.CompanyId);
        var (catId, _) = await SeedCategoryAsync(sp, co.CompanyId, "6160", "ค่าใช้จ่ายทดสอบ");

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IExpenseClaimService>();
        var id = await svc.CreateDraftAsync(
            Req(employeeId, [new ExpenseClaimLineInput(catId, null, "x", Today, 100m, null, 0m, true)]),
            default);

        (await svc.GetDetailAsync(id, default))!.Status.Should().Be(nameof(ExpenseClaimStatus.Draft));

        await svc.SubmitAsync(id, default);
        (await svc.GetDetailAsync(id, default))!.Status.Should().Be(nameof(ExpenseClaimStatus.Submitted));

        var approved = await svc.ApproveAsync(id, default);
        approved.ExpenseClaimId.Should().Be(id);
        var afterApprove = (await svc.GetDetailAsync(id, default))!;
        afterApprove.Status.Should().Be(nameof(ExpenseClaimStatus.Approved));
        afterApprove.DocNo.Should().BeNull("doc_no is assigned only at pay");
        afterApprove.JournalEntryId.Should().BeNull();

        var paid = await svc.PayAsync(id, new PayExpenseClaimRequest("CASH", null), default);
        paid.DocNo.Should().Contain("EX", "the EX prefix is embedded in the MM-YYYY-EX-NNNN doc number, mirroring PV's MM-YYYY-PV-{CATEGORY}-NNNN shape");
        var afterPay = (await svc.GetDetailAsync(id, default))!;
        afterPay.Status.Should().Be(nameof(ExpenseClaimStatus.Paid));
        afterPay.DocNo.Should().NotBeNullOrEmpty();
        afterPay.JournalEntryId.Should().NotBeNull();
    }

    [SkippableFact]
    public async Task Illegal_transitions_throw_the_named_domain_error()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp) = await SetupAsync();
        var employeeId = await SeedEmployeeAsync(sp, co.CompanyId);
        var (catId, _) = await SeedCategoryAsync(sp, co.CompanyId, "6170", "ค่าใช้จ่ายทดสอบ2");

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IExpenseClaimService>();
        var draftId = await svc.CreateDraftAsync(
            Req(employeeId, [new ExpenseClaimLineInput(catId, null, "y", Today, 100m, null, 0m, true)]),
            default);

        // Pay-on-Draft
        (await Assert.ThrowsAsync<DomainException>(() =>
            svc.PayAsync(draftId, new PayExpenseClaimRequest("CASH", null), default))).Code
            .Should().Be("expense_claim.not_approved");

        // Approve-on-Draft
        (await Assert.ThrowsAsync<DomainException>(() => svc.ApproveAsync(draftId, default))).Code
            .Should().Be("expense_claim.not_submitted");

        // Reject-on-Draft
        (await Assert.ThrowsAsync<DomainException>(() =>
            svc.RejectAsync(draftId, new RejectExpenseClaimRequest("no receipts"), default))).Code
            .Should().Be("expense_claim.not_submitted");

        await svc.SubmitAsync(draftId, default);
        await svc.ApproveAsync(draftId, default);

        // Submit-on-Approved
        (await Assert.ThrowsAsync<DomainException>(() => svc.SubmitAsync(draftId, default))).Code
            .Should().Be("expense_claim.not_draft");

        // Cancel-on-Approved (only Draft/Rejected are cancellable)
        (await Assert.ThrowsAsync<DomainException>(() => svc.CancelAsync(draftId, default))).Code
            .Should().Be("expense_claim.cannot_cancel");
    }

    [SkippableFact]
    public async Task Cancel_is_legal_from_Draft_and_Rejected()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp) = await SetupAsync();
        var employeeId = await SeedEmployeeAsync(sp, co.CompanyId);
        var (catId, _) = await SeedCategoryAsync(sp, co.CompanyId, "6180", "ค่าใช้จ่ายทดสอบ3");

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IExpenseClaimService>();

        var draftId = await svc.CreateDraftAsync(
            Req(employeeId, [new ExpenseClaimLineInput(catId, null, "z", Today, 50m, null, 0m, true)]),
            default);
        await svc.CancelAsync(draftId, default);
        (await svc.GetDetailAsync(draftId, default))!.Status.Should().Be(nameof(ExpenseClaimStatus.Cancelled));

        var rejId = await svc.CreateDraftAsync(
            Req(employeeId, [new ExpenseClaimLineInput(catId, null, "z2", Today, 50m, null, 0m, true)]),
            default);
        await svc.SubmitAsync(rejId, default);
        await svc.RejectAsync(rejId, new RejectExpenseClaimRequest("missing receipt"), default);
        (await svc.GetDetailAsync(rejId, default))!.Status.Should().Be(nameof(ExpenseClaimStatus.Rejected));
        await svc.CancelAsync(rejId, default);
        (await svc.GetDetailAsync(rejId, default))!.Status.Should().Be(nameof(ExpenseClaimStatus.Cancelled));
    }

    // ── Race safety (§6) — FOOTGUN 8: Version++ must actually fire, unlike PV's inert token ──

    /// <summary>Deterministic EF-level proof that Version++ actually fires the optimistic-
    /// concurrency check (FOOTGUN 8 — unlike PV's inert token): two DbContexts load the SAME
    /// row (same original Version), both mutate in-memory, the first SaveChangesAsync commits,
    /// the second's WHERE-version predicate then matches zero rows -> DbUpdateConcurrencyException.
    /// A Task.WhenAll race on the SERVICE call was tried first and found unreliable on a fast
    /// local Postgres (the loser's fresh re-SELECT sometimes already observed the winner's
    /// commit, hitting the ordinary status guard instead of the concurrency guard) — this
    /// two-preloaded-context form is the standard, non-flaky way to test an EF concurrency
    /// token and is what ExpenseClaimService.SaveGuardedAsync's catch(DbUpdateConcurrencyException)
    /// exists to translate into expense_claim.locked_mismatch (-> 409).</summary>
    [SkippableFact]
    public async Task Concurrent_Approve_second_stale_save_throws_DbUpdateConcurrencyException()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp) = await SetupAsync();
        var employeeId = await SeedEmployeeAsync(sp, co.CompanyId);
        var (catId, _) = await SeedCategoryAsync(sp, co.CompanyId, "6190", "ค่าใช้จ่ายแข่งขัน");

        long id;
        await using (var s0 = sp.CreateAsyncScope())
        {
            var svc0 = s0.ServiceProvider.GetRequiredService<IExpenseClaimService>();
            id = await svc0.CreateDraftAsync(
                Req(employeeId, [new ExpenseClaimLineInput(catId, null, "race", Today, 100m, null, 0m, true)]),
                default);
            await svc0.SubmitAsync(id, default);
        }

        await using var s1 = sp.CreateAsyncScope();
        var db1 = s1.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var claim1 = await db1.ExpenseClaims.FirstAsync(c => c.ExpenseClaimId == id);

        await using var s2 = sp.CreateAsyncScope();
        var db2 = s2.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var claim2 = await db2.ExpenseClaims.FirstAsync(c => c.ExpenseClaimId == id);

        // Both contexts loaded the SAME original Version — simulates two concurrent Approve
        // callers each having read the row before either wrote.
        claim1.Approve(1, DateTimeOffset.UtcNow);
        claim2.Approve(1, DateTimeOffset.UtcNow);

        await db1.SaveChangesAsync();   // winner — Version 1 -> 2

        var loser = async () => await db2.SaveChangesAsync();
        await loser.Should().ThrowAsync<DbUpdateConcurrencyException>(
            "db2's tracked original Version no longer matches the row db1 just committed");

        await using var s3 = sp.CreateAsyncScope();
        var db3 = s3.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var final = await db3.ExpenseClaims.AsNoTracking().FirstAsync(c => c.ExpenseClaimId == id);
        final.Status.Should().Be(ExpenseClaimStatus.Approved);
        final.Version.Should().Be(2, "Draft(0) -> Submit(1) -> the single winning Approve(2)");
    }

    /// <summary>Genuine Task.Run concurrency against the real PayAsync service (tx + number
    /// allocation + GL post). The loser may fail via either the concurrency guard
    /// (expense_claim.locked_mismatch, if it truly overlapped the winner) or the ordinary
    /// not_approved status guard (if its fresh re-load already observed the winner's commit) —
    /// both are a CORRECT rejection of the loser, so both are accepted here. The invariant this
    /// proves either way: never two successes, never two journal entries.</summary>
    [SkippableFact]
    public async Task Double_pay_race_posts_exactly_one_journal_entry()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp) = await SetupAsync();
        var employeeId = await SeedEmployeeAsync(sp, co.CompanyId);
        var (catId, _) = await SeedCategoryAsync(sp, co.CompanyId, "6191", "ค่าใช้จ่ายแข่งขัน2");

        long id;
        await using (var s0 = sp.CreateAsyncScope())
        {
            var svc0 = s0.ServiceProvider.GetRequiredService<IExpenseClaimService>();
            id = await svc0.CreateDraftAsync(
                Req(employeeId, [new ExpenseClaimLineInput(catId, null, "race-pay", Today, 100m, null, 0m, true)]),
                default);
            await svc0.SubmitAsync(id, default);
            await svc0.ApproveAsync(id, default);
        }

        async Task<bool> TryPay()
        {
            await using var s = sp.CreateAsyncScope();
            var svc = s.ServiceProvider.GetRequiredService<IExpenseClaimService>();
            try { await svc.PayAsync(id, new PayExpenseClaimRequest("CASH", null), default); return true; }
            catch (DomainException) { return false; }
        }

        var results = await Task.WhenAll(Task.Run(TryPay), Task.Run(TryPay));
        results.Count(r => r).Should().Be(1, "a double-pay race must not post two journal entries");

        await using var s2 = sp.CreateAsyncScope();
        var db = s2.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var claim = await db.ExpenseClaims.AsNoTracking().FirstAsync(c => c.ExpenseClaimId == id);
        claim.Status.Should().Be(ExpenseClaimStatus.Paid);
        (await db.JournalEntries.CountAsync(j => j.CompanyId == co.CompanyId && j.Reference == claim.DocNo))
            .Should().Be(1);
    }

    // ── Tenant scope (§6, FOOTGUN 6 — labelled query-filter, NOT RLS) ──────────────

    [SkippableFact]
    public async Task Claim_from_company_A_is_invisible_to_company_B_via_query_filter()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (coA, spA) = await SetupAsync();
        var employeeIdA = await SeedEmployeeAsync(spA, coA.CompanyId);
        var (catId, _) = await SeedCategoryAsync(spA, coA.CompanyId, "6192", "ทดสอบสิทธิ์");

        long id;
        await using (var sA = spA.CreateAsyncScope())
        {
            var svcA = sA.ServiceProvider.GetRequiredService<IExpenseClaimService>();
            id = await svcA.CreateDraftAsync(
                Req(employeeIdA, [new ExpenseClaimLineInput(catId, null, "scoped", Today, 10m, null, 0m, true)]),
                default);
        }

        var (coB, _) = await SetupAsync();
        var spB = TestCompanyFactory.BuildProvider(_fx.ConnectionString, coB.CompanyId, coB.BranchId);
        await using var sB = spB.CreateAsyncScope();
        var svcB = sB.ServiceProvider.GetRequiredService<IExpenseClaimService>();

        // EF global query filter (ITenantOwned -> CompanyId), NOT a DB-level RLS assertion —
        // teas_test connects as superuser (RLS bypassed), see FOOTGUN 6.
        (await svcB.GetDetailAsync(id, default)).Should().BeNull();
        var dbB = sB.ServiceProvider.GetRequiredService<AccountingDbContext>();
        (await dbB.ExpenseClaims.AnyAsync(c => c.ExpenseClaimId == id)).Should().BeFalse();
    }

    // ── Attachment wiring (§5/§6) ───────────────────────────────────────────────────

    [SkippableFact]
    public async Task Attachment_upload_and_ParentExistsAsync_arm()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var root = Path.Combine(Path.GetTempPath(), "teas-it-expense-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var (co, _) = await SetupAsync();
            var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = _fx.ConnectionString,
                ["FileStorage:StorageRoot"] = root,
                ["FileStorage:MaxFileSizeMb"] = "25",
            }).Build();
            var services = new ServiceCollection();
            services.AddLogging();
            await using var sp = services.AddInfrastructure(cfg)
                .AddSingleton<ITenantContext>(new StubTenant
                { CompanyId = co.CompanyId, BranchId = co.BranchId, UserId = 1, IsSuperAdmin = false })
                .BuildServiceProvider();

            var employeeId = await SeedEmployeeAsync(sp, co.CompanyId);
            var (catId, _) = await SeedCategoryAsync(sp, co.CompanyId, "6193", "ทดสอบไฟล์แนบ");

            await using var s = sp.CreateAsyncScope();
            var claimSvc = s.ServiceProvider.GetRequiredService<IExpenseClaimService>();
            var id = await claimSvc.CreateDraftAsync(
                Req(employeeId, [new ExpenseClaimLineInput(catId, null, "receipt", Today, 20m, null, 0m, true)]),
                default);

            var attachSvc = s.ServiceProvider.GetRequiredService<IAttachmentService>();
            var upload = await attachSvc.UploadAsync(
                "EXPENSE_CLAIM", id, "EXPENSE_CLAIM_FORM", "line_no:1", "receipt.jpg",
                "image/jpeg", 9, new MemoryStream(System.Text.Encoding.UTF8.GetBytes("jpg-bytes")), default);
            upload.FileName.Should().Be("receipt.jpg");

            var missingUpload = () => attachSvc.UploadAsync(
                "EXPENSE_CLAIM", 999_999_999, "EXPENSE_CLAIM_FORM", null, "x.jpg",
                "image/jpeg", 3, new MemoryStream(System.Text.Encoding.UTF8.GetBytes("abc")), default);
            (await Assert.ThrowsAsync<DomainException>(missingUpload)).Code
                .Should().Be("attachment.parent_not_found");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
