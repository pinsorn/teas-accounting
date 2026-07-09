using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Bank;
using Accounting.Domain.Common;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Bank;

/// <summary>
/// Bank reconciliation (specs/bank-reconciliation.md B2.5, T2) — StatementImportService
/// integration tests. T2: a synthetic CSV whose amount contradicts the balance delta must FAIL
/// the import and persist NOTHING (no StatementImport/StatementLine rows).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class StatementImportServiceTests
{
    private readonly PostgresFixture _fx;
    public StatementImportServiceTests(PostgresFixture fx) => _fx = fx;

    private async Task<(int BankAccountId, ServiceProvider Sp)> SeedBankAccountAsync()
    {
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        await using var s = sp.CreateAsyncScope();
        var bankSvc = s.ServiceProvider.GetRequiredService<IBankAccountService>();
        var id = await bankSvc.CreateAsync(new CreateBankAccountRequest(
            "KBANK", "Kasikornbank", "999-9-99999-9", null, null, null, "THB"), default);
        return (id, sp);
    }

    [SkippableFact]
    public async Task ImportAsync_happy_path_persists_import_and_lines()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (bankAccountId, sp) = await SeedBankAccountAsync();

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IStatementImportService>();
        var result = await svc.ImportAsync(
            bankAccountId, "test-statement.csv", "text/csv", 1000,
            KBizCsvAdapterTests.Utf8BomStream(KBizCsvAdapterTests.GoodCsv), null, default);

        result.LineCount.Should().Be(4);
        result.OverlapWarning.Should().BeFalse();

        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var import = await db.StatementImports.SingleAsync(x => x.StatementImportId == result.StatementImportId);
        import.AttachmentId.Should().NotBeNull();
        import.LineCount.Should().Be(4);
        (await db.StatementLines.CountAsync(x => x.StatementImportId == result.StatementImportId))
            .Should().Be(4);

        var lines = await svc.GetLinesAsync(result.StatementImportId, default);
        lines.Should().HaveCount(4);

        var imports = await svc.ListAsync(bankAccountId, default);
        imports.Should().ContainSingle(x => x.StatementImportId == result.StatementImportId);
    }

    [SkippableFact]
    public async Task ImportAsync_integrity_failure_persists_nothing()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (bankAccountId, sp) = await SeedBankAccountAsync();

        // Same corruption as the adapter-level test: the deposit row's balance no longer
        // matches its declared amount.
        var corrupt = KBizCsvAdapterTests.GoodCsv.Replace(
            ",25-05-26,18:39,รับโอนเงิน,,,\"20,000.00\",,\"21,000.00\",,MAKE by KBank,,จาก X0000 นาย ทดสอบ ระบบ",
            ",25-05-26,18:39,รับโอนเงิน,,,\"20,000.00\",,\"16,000.00\",,MAKE by KBank,,จาก X0000 นาย ทดสอบ ระบบ");

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IStatementImportService>();

        var act = () => svc.ImportAsync(
            bankAccountId, "bad-statement.csv", "text/csv", 1000,
            KBizCsvAdapterTests.Utf8BomStream(corrupt), null, default);

        await act.Should().ThrowAsync<DomainException>();

        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        (await db.StatementImports.CountAsync(x => x.BankAccountId == bankAccountId)).Should().Be(0);
        (await db.StatementLines.CountAsync(x => x.BankAccountId == bankAccountId)).Should().Be(0);
    }
}
