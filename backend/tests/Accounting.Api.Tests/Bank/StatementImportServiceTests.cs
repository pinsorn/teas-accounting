using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Bank;
using Accounting.Domain.Common;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Bank;

/// <summary>
/// Bank reconciliation (specs/bank-reconciliation.md B2.5, T2) — StatementImportService
/// integration tests. T2: a synthetic CSV whose amount contradicts the balance delta must FAIL
/// the import and persist NOTHING (no StatementImport/StatementLine rows).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class StatementImportServiceTests : IDisposable
{
    private readonly PostgresFixture _fx;

    // Fable cross-review (2026-07-09, CI failure on PR #64) — ImportAsync uploads through the
    // REAL Attachment infra (D11: raw bytes stored as-uploaded), which writes to
    // LocalDiskFileStorage's configured StorageRoot. TestCompanyFactory.BuildProvider uses the
    // DEFAULT root (/var/teas/attachments) — unwritable on a CI runner (UnauthorizedAccessException
    // on Linux; happened to work locally on Windows, so the local suite was green while CI was
    // red). Mirrors Sprint11AttachmentTests' own Provider() override exactly: a per-test temp
    // directory, cleaned up in Dispose.
    private readonly string _storageRoot =
        Path.Combine(Path.GetTempPath(), "teas-it-" + Guid.NewGuid().ToString("N")[..8]);

    public StatementImportServiceTests(PostgresFixture fx) => _fx = fx;

    public void Dispose()
    {
        if (Directory.Exists(_storageRoot)) Directory.Delete(_storageRoot, recursive: true);
    }

    private ServiceProvider BuildProviderWithTempStorage(int companyId, int branchId)
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = _fx.ConnectionString,
            ["FileStorage:StorageRoot"] = _storageRoot,
            ["FileStorage:MaxFileSizeMb"] = "25",
        }).Build();
        var s = new ServiceCollection();
        s.AddLogging();
        return s.AddInfrastructure(cfg)
            .AddSingleton<ITenantContext>(new StubTenant
            { CompanyId = companyId, BranchId = branchId, UserId = 1, IsSuperAdmin = false })
            .BuildServiceProvider();
    }

    private async Task<(int BankAccountId, ServiceProvider Sp)> SeedBankAccountAsync()
    {
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var sp = BuildProviderWithTempStorage(co.CompanyId, co.BranchId);
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

    /// <summary>Codex review finding #3 (2026-07-10) — GoodCsv's own metadata carries account no.
    /// "999-9-99999-9"; importing it against a DIFFERENTLY-numbered selected bank account must be
    /// rejected BEFORE any attachment/db write (same "persists nothing" shape as the integrity
    /// test above).</summary>
    [SkippableFact]
    public async Task ImportAsync_rejects_a_statement_parsed_from_the_wrong_bank_account()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var sp = BuildProviderWithTempStorage(co.CompanyId, co.BranchId);
        await using var s0 = sp.CreateAsyncScope();
        var bankSvc = s0.ServiceProvider.GetRequiredService<IBankAccountService>();
        // Deliberately a DIFFERENT account number than the GoodCsv fixture's "999-9-99999-9".
        var wrongBankAccountId = await bankSvc.CreateAsync(new CreateBankAccountRequest(
            "SCB", "Siam Commercial Bank", "111-1-11111-1", null, null, null, "THB"), default);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IStatementImportService>();
        var act = () => svc.ImportAsync(
            wrongBankAccountId, "test-statement.csv", "text/csv", 1000,
            KBizCsvAdapterTests.Utf8BomStream(KBizCsvAdapterTests.GoodCsv), null, default);

        (await Assert.ThrowsAsync<DomainException>(act)).Code.Should().Be("bank.statement_account_mismatch");

        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        (await db.StatementImports.CountAsync(x => x.BankAccountId == wrongBankAccountId)).Should().Be(0);
        (await db.StatementLines.CountAsync(x => x.BankAccountId == wrongBankAccountId)).Should().Be(0);
    }
}
