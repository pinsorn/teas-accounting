using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Accounting.Api.Authorization;
using Accounting.Api.Tests.Fixtures;
using Accounting.Api.Tests.Rbac;
using Accounting.Application.Abstractions;
using Accounting.Application.Expense;
using Accounting.Domain.Entities.Master;
using Accounting.Domain.Entities.Sys;
using Accounting.Domain.Enums;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Identity;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Expense;

/// <summary>
/// Cycle C (specs/expense-claims.md §6, §2 role split) — HTTP-level (real pipeline,
/// RbacApiFactory, mirrors GeneralLedgerEndpointTests) RBAC gating: an ACCOUNTANT-shaped token
/// (read+create only, per 617_seed_expense_claim_perms.sql) is 403 on approve/pay; a
/// CHIEF_ACCOUNTANT-shaped token (also has approve+pay) succeeds. Tokens carry explicit
/// Permissions claims (same technique as GeneralLedgerEndpointTests) — this asserts the
/// ENDPOINT's permission requirement, not the SQL seed's DB fan-out (that's the separate
/// deploy-probe row-count check in the verification gates).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class ExpenseClaimPermissionTests
{
    private readonly PostgresFixture _fx;
    public ExpenseClaimPermissionTests(PostgresFixture fx) => _fx = fx;

    private static string Token(int companyId, int branchId, string[] permissions) => new JwtTokenIssuer(
        new StaticOptionsMonitor<JwtOptions>(new JwtOptions
        {
            Issuer = RbacApiFactory.JwtIssuer,
            Audience = RbacApiFactory.JwtAudience,
            SigningKey = RbacApiFactory.JwtSigningKey,
            AccessTokenMinutes = 60,
        }))
        .Issue(new TokenClaims(
            UserId: 1, Username: "expense-claim-perm-tests", CompanyId: companyId, BranchId: branchId,
            IsSuperAdmin: false, Roles: ["test"], Permissions: permissions)).Token;

    private static readonly string[] AccountantPerms =
        [Permissions.Expense.ClaimRead, Permissions.Expense.ClaimCreate];
    private static readonly string[] ChiefAccountantPerms =
        [Permissions.Expense.ClaimRead, Permissions.Expense.ClaimCreate,
         Permissions.Expense.ClaimApprove, Permissions.Expense.ClaimPay];

    private static HttpRequestMessage Post(string path, string token, object? body = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) req.Content = JsonContent.Create(body);
        return req;
    }

    /// <summary>Seeds employee+category+two claims (one Submitted for the approve-403 case, one
    /// Approved for the pay-403 case) directly via the service (StubTenant), bypassing HTTP —
    /// only the approve/pay CALLS themselves go through the real HTTP pipeline.</summary>
    private async Task<(int CompanyId, int BranchId, long SubmittedId, long ApprovedId)> SeedAsync()
    {
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        var today = new SystemClock().TodayInBangkok();

        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();

        var emp = new Employee
        {
            CompanyId = co.CompanyId, EmployeeCode = "EMP-PERM-" + Guid.NewGuid().ToString("N")[..8],
            FirstNameTh = "ทดสอบ", LastNameTh = "สิทธิ์",
            NationalId = Random.Shared.NextInt64(1_000_000_000_000, 9_999_999_999_999).ToString(),
            HireDate = today.AddYears(-1),
        };
        db.Employees.Add(emp);

        var acct = new Accounting.Domain.Entities.Master.ChartOfAccount
        {
            CompanyId = co.CompanyId, AccountCode = "6199", AccountNameTh = "ทดสอบสิทธิ์บัญชี",
            AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit,
            IsActive = true, CreatedAt = DateTimeOffset.UtcNow,
        };
        db.ChartOfAccounts.Add(acct);
        await db.SaveChangesAsync();

        var cat = new ExpenseCategory
        {
            CompanyId = co.CompanyId, CategoryCode = "EXPPERM" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(),
            NameTh = "ทดสอบสิทธิ์", DefaultExpenseAccountId = acct.AccountId, DefaultIsRecoverableVat = true,
        };
        db.ExpenseCategories.Add(cat);
        await db.SaveChangesAsync();

        var svc = s.ServiceProvider.GetRequiredService<IExpenseClaimService>();
        var lines = new[] { new ExpenseClaimLineInput(cat.CategoryId, null, "perm-test", today, 10m, null, 0m, true) };

        var submittedId = await svc.CreateDraftAsync(
            new CreateExpenseClaimRequest(emp.EmployeeId, today, "t1", null, lines), default);
        await svc.SubmitAsync(submittedId, default);

        var approvedId = await svc.CreateDraftAsync(
            new CreateExpenseClaimRequest(emp.EmployeeId, today, "t2", null, lines), default);
        await svc.SubmitAsync(approvedId, default);
        await svc.ApproveAsync(approvedId, default);

        return (co.CompanyId, co.BranchId, submittedId, approvedId);
    }

    [SkippableFact]
    public async Task Accountant_shaped_token_is_403_on_approve_and_pay()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (companyId, branchId, submittedId, approvedId) = await SeedAsync();
        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();
        var token = Token(companyId, branchId, AccountantPerms);

        using var approveResp = await client.SendAsync(Post($"/expense-claims/{submittedId}/approve", token));
        approveResp.StatusCode.Should().Be(HttpStatusCode.Forbidden, "ACCOUNTANT has no expense.claim.approve");

        using var payResp = await client.SendAsync(Post($"/expense-claims/{approvedId}/pay", token,
            new { paymentMethod = "CASH", bankAccountId = (int?)null }));
        payResp.StatusCode.Should().Be(HttpStatusCode.Forbidden, "ACCOUNTANT has no expense.claim.pay");
    }

    [SkippableFact]
    public async Task ChiefAccountant_shaped_token_succeeds_on_approve_and_pay()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (companyId, branchId, submittedId, approvedId) = await SeedAsync();
        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();
        var token = Token(companyId, branchId, ChiefAccountantPerms);

        using var approveResp = await client.SendAsync(Post($"/expense-claims/{submittedId}/approve", token));
        approveResp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var payResp = await client.SendAsync(Post($"/expense-claims/{approvedId}/pay", token,
            new { paymentMethod = "CASH", bankAccountId = (int?)null }));
        payResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
