using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Attachments;
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

    /// <summary>L2-4 (PLAN-fix-findings-r2.md §U4, findings-r2/findings-leg2.md) — a statement
    /// line whose Description exceeds the column's varchar(500) cap (StatementLineConfiguration)
    /// previously escaped as a raw, unhandled DbUpdateException (a generic 500 leaking Postgres
    /// text at the API layer). Pre-validation must catch this at parse time and throw a typed
    /// DomainException naming the line number only — never the raw text (D10 no-PII
    /// convention).</summary>
    [SkippableFact]
    public async Task ImportAsync_oversized_description_field_returns_typed_error_not_raw_500()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (bankAccountId, sp) = await SeedBankAccountAsync();

        var longDescription = new string('A', 600);
        var oversized = KBizCsvAdapterTests.GoodCsv.Replace(
            ",19-06-26,23:59,ภาษีหัก ณ ที่จ่าย,0.75,,,,\"6,004.25\",,โอนเข้า/หักบัญชีอัตโนมัติ,,รหัสอ้างอิง PCB00001\r\n",
            $",19-06-26,23:59,ภาษีหัก ณ ที่จ่าย,0.75,,,,\"6,004.25\",,โอนเข้า/หักบัญชีอัตโนมัติ,,{longDescription}\r\n");
        oversized.Should().NotBe(KBizCsvAdapterTests.GoodCsv, "the fixture's oversized line must actually replace something, or this test proves nothing");

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IStatementImportService>();
        var act = () => svc.ImportAsync(
            bankAccountId, "oversized-statement.csv", "text/csv", 1000,
            KBizCsvAdapterTests.Utf8BomStream(oversized), null, default);

        var ex = await Assert.ThrowsAsync<DomainException>(act);
        ex.Code.Should().Be("bank.import_line_too_long");
        ex.Message.Should().NotContain("22001", "the raw Postgres error class must never leak to a typed error's message");

        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        (await db.StatementImports.CountAsync(x => x.BankAccountId == bankAccountId)).Should().Be(0);
        (await db.StatementLines.CountAsync(x => x.BankAccountId == bankAccountId)).Should().Be(0);
    }

    /// <summary>L2-4 residual defense — a case pre-validation does NOT cover (an oversized
    /// SourceFileName, varchar(255) on StatementImport, not a per-line field) must still hit the
    /// persistence-phase catch and translate to a typed `bank.import_failed`, with the same
    /// rollback/atomicity guarantee as every other ImportAsync failure mode.</summary>
    [SkippableFact]
    public async Task ImportAsync_residual_persistence_failure_rolls_back_atomically_with_typed_error()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (bankAccountId, sp) = await SeedBankAccountAsync();
        var oversizedFileName = new string('f', 260) + ".csv";   // SourceFileName caps at 255

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IStatementImportService>();
        var act = () => svc.ImportAsync(
            bankAccountId, oversizedFileName, "text/csv", 1000,
            KBizCsvAdapterTests.Utf8BomStream(KBizCsvAdapterTests.GoodCsv), null, default);

        var ex = await Assert.ThrowsAsync<DomainException>(act);
        ex.Code.Should().Be("bank.import_failed");
        ex.Message.Should().NotContain("22001", "the raw Postgres error class must never leak to a typed error's message");

        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        (await db.StatementImports.CountAsync(x => x.BankAccountId == bankAccountId)).Should().Be(0);
        (await db.StatementLines.CountAsync(x => x.BankAccountId == bankAccountId)).Should().Be(0);
    }

    // ── L2-3 (PLAN-fix-findings-r2.md §U3.2) — DeleteImportAsync ──────────────────────────────

    [SkippableFact]
    public async Task DeleteImportAsync_removes_a_pure_unmatched_import_and_its_lines()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (bankAccountId, sp) = await SeedBankAccountAsync();
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IStatementImportService>();
        var result = await svc.ImportAsync(
            bankAccountId, "test-statement.csv", "text/csv", 1000,
            KBizCsvAdapterTests.Utf8BomStream(KBizCsvAdapterTests.GoodCsv), null, default);

        await svc.DeleteImportAsync(result.StatementImportId, default);

        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        (await db.StatementImports.CountAsync(x => x.StatementImportId == result.StatementImportId)).Should().Be(0);
        (await db.StatementLines.CountAsync(x => x.StatementImportId == result.StatementImportId)).Should().Be(0);
    }

    [SkippableFact]
    public async Task DeleteImportAsync_refuses_when_a_line_is_matched()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (bankAccountId, sp) = await SeedBankAccountAsync();
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IStatementImportService>();
        var result = await svc.ImportAsync(
            bankAccountId, "test-statement.csv", "text/csv", 1000,
            KBizCsvAdapterTests.Utf8BomStream(KBizCsvAdapterTests.GoodCsv), null, default);

        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var line = await db.StatementLines.FirstAsync(x => x.StatementImportId == result.StatementImportId);
        line.MatchStatus = Accounting.Domain.Enums.MatchStatus.Matched;
        // MatchedReceiptId sits under a unique partial index (StatementLineConfiguration) — a
        // hardcoded placeholder collides with leftover rows from a prior run of this SAME test
        // in the shared, persistent teas_test DB (Random.Shared avoids that; no real Receipt FK
        // exists on StatementLine, so any value works — see BankEnums.cs's "id only" note).
        line.MatchedReceiptId = Random.Shared.NextInt64(1, long.MaxValue);
        await db.SaveChangesAsync();

        var act = () => svc.DeleteImportAsync(result.StatementImportId, default);
        (await Assert.ThrowsAsync<DomainException>(act)).Code.Should().Be("bank.import_has_matched_lines");

        (await db.StatementImports.CountAsync(x => x.StatementImportId == result.StatementImportId)).Should().Be(1,
            "a refused delete must not remove anything");
        (await db.StatementLines.CountAsync(x => x.StatementImportId == result.StatementImportId)).Should().Be(4);
    }

    [SkippableFact]
    public async Task DeleteImportAsync_unknown_id_throws_not_found()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (_, sp) = await SeedBankAccountAsync();
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IStatementImportService>();

        var act = () => svc.DeleteImportAsync(999999999, default);
        (await Assert.ThrowsAsync<DomainException>(act)).Code.Should().Be("bank.import_not_found");
    }

    // ── Codex review 2026-08-20 F3 — deleting an import must soft-delete its attachment too ──

    [SkippableFact]
    public async Task DeleteImportAsync_soft_deletes_the_statement_attachment_and_stops_serving_it()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (bankAccountId, sp) = await SeedBankAccountAsync();
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IStatementImportService>();
        var result = await svc.ImportAsync(
            bankAccountId, "test-statement.csv", "text/csv", 1000,
            KBizCsvAdapterTests.Utf8BomStream(KBizCsvAdapterTests.GoodCsv), null, default);

        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var import = await db.StatementImports.SingleAsync(x => x.StatementImportId == result.StatementImportId);
        import.AttachmentId.Should().NotBeNull();
        var attachmentId = import.AttachmentId!.Value;

        var attachments = s.ServiceProvider.GetRequiredService<IAttachmentService>();
        (await attachments.ListAsync("BANK_STATEMENT", result.StatementImportId, default))
            .Should().ContainSingle(a => a.AttachmentId == attachmentId,
                "sanity — the attachment must be live and listed before deletion");

        await svc.DeleteImportAsync(result.StatementImportId, default);

        var attachmentRow = await db.Attachments.AsNoTracking()
            .SingleAsync(a => a.AttachmentId == attachmentId);
        attachmentRow.DeletedAt.Should().NotBeNull(
            "F3 — deleting the import must soft-delete its raw statement attachment, not leave it live");

        (await attachments.ListAsync("BANK_STATEMENT", result.StatementImportId, default))
            .Should().BeEmpty("the soft-deleted attachment must no longer be listed");

        var download = () => attachments.OpenForDownloadAsync(attachmentId, default);
        (await Assert.ThrowsAsync<DomainException>(download)).Code.Should().Be("attachment.not_found",
            "the soft-deleted attachment must no longer be downloadable");
    }

    // F4 (Codex review 2026-08-20) — no new tests here by design: the pre-existing
    // DeleteImportAsync_removes_a_pure_unmatched_import_and_its_lines (all-unmatched → clean
    // delete) and DeleteImportAsync_refuses_when_a_line_is_matched (a Matched line → refused,
    // nothing deleted) above already exercise the conditional-delete + count-comparison code path
    // end-to-end and must stay green after the refactor. Per the dispatch: "a true concurrent-race
    // test is not required — the conditional delete makes the race structurally harmless."
}
