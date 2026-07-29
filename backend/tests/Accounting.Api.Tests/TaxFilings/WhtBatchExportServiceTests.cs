using System.Text;
using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.TaxFilings;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Tax;
using Accounting.Domain.Enums;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Persistence;
using Accounting.Infrastructure.TaxFilings;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.TaxFilings;

/// <summary>
/// cont.82.1 P2 — DB-backed tests for <see cref="IWhtBatchExportService"/>: form filtering,
/// payee grouping, the missing-tax-id guard, and BE/rate conversions in the emitted file.
/// (Pure format rules are covered by <see cref="WhtBatchFormatTests"/>.)
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class WhtBatchExportServiceTests
{
    private readonly PostgresFixture _fx;
    public WhtBatchExportServiceTests(PostgresFixture fx) => _fx = fx;

    private ServiceProvider Provider()
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:Postgres"] = _fx.ConnectionString }).Build();
        var s = new ServiceCollection();
        s.AddLogging();
        s.AddSingleton<IConfiguration>(cfg);   // the host supplies this in production
        return s.AddInfrastructure(cfg)
            .AddSingleton<ITenantContext>(new StubTenant
            { CompanyId = 1, BranchId = 1, UserId = 1, IsSuperAdmin = false })
            .BuildServiceProvider();
    }

    private static string Sfx() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    // Distinct far-future period per test — the shared fixture persists inserted rows.
    private static int RandPeriod()
    {
        var r = Random.Shared;
        return (3000 + r.Next(0, 6000)) * 100 + r.Next(1, 13);
    }
    private static DateOnly PeriodDate(int period) => new(period / 100, period % 100, 10);
    // 13-digit numeric payee id (TestIds-style uniqueness via a random tail).
    private static string PayeeId() => "1" + Random.Shared.NextInt64(0, 999_999_999_999L).ToString("000000000000");

    private static async Task AddCert(
        ServiceProvider sp, DateOnly date, CustomerType payee, WhtFormType form,
        string? payeeTaxId, decimal income, decimal rate)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        db.WhtCertificates.Add(new WhtCertificate
        {
            CompanyId = 1, BranchId = 1, DocNo = "WT-" + Sfx(), CertDate = date,
            Direction = "P", PayerTaxId = "0105500001234", PayerBranchCode = "00000",
            PayerName = "TEAS Co", PayerAddress = "BKK",  // ม.86/4 branch = char(5); 00000 = HQ
            PayeeTaxId = payeeTaxId, PayeeName = "บริษัท " + Sfx() + " จำกัด",
            PayeeAddress = "BKK", PayeeType = payee, FormType = form,
            IncomeTypeCode = "3", IncomeDescription = "ค่าบริการ",
            IncomeAmount = income, WhtRate = rate,
            WhtAmount = decimal.Round(income * rate, 2),
            Status = DocumentStatus.Posted, IssuedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(default);
    }

    [SkippableFact]
    public async Task Pnd53_batch_groups_by_payee_and_excludes_individuals_and_pnd54()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var period = RandPeriod();
        var d = PeriodDate(period);
        var payeeA = PayeeId();
        var payeeB = PayeeId();

        // Same corporate payee twice (→ 1 SEQ_NO, 2 income triples), a 2nd corporate payee,
        // plus an individual + a Pnd54 corporate that PND53 must drop.
        await AddCert(sp, d, CustomerType.Corporate, WhtFormType.Pnd53, payeeA, 1000m, 0.03m);
        await AddCert(sp, d, CustomerType.Corporate, WhtFormType.Pnd53, payeeA, 2000m, 0.03m);
        await AddCert(sp, d, CustomerType.Corporate, WhtFormType.Pnd53, payeeB, 5000m, 0.03m);
        await AddCert(sp, d, CustomerType.Individual, WhtFormType.Pnd3,  PayeeId(), 9000m, 0.03m);
        await AddCert(sp, d, CustomerType.Corporate, WhtFormType.Pnd54, PayeeId(), 7000m, 0.15m);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IWhtBatchExportService>();
        var file = await svc.BuildAsync("PND53", period, default);

        file.FileName.Should().StartWith("PND53_0105500001234_000000_");
        file.RecordCount.Should().Be(2);            // 2 payees → 2 SEQ_NO rows

        var text = Encoding.UTF8.GetString(file.Content);
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var header = lines[0].Split('|');
        header[0].Should().Be("H");
        header[5].Should().Be("PND53");
        header[17].Should().Be("2");                // TOT_NUM
        header[18].Should().Be("8000.00");          // TOT_AMT = 1000+2000+5000 (no individual/54)
        header[19].Should().Be("240.00");           // TOT_TAX = 8000 * 3%

        lines.Should().HaveCount(3);                // header + 2 detail
        text.Should().Contain(payeeA).And.Contain(payeeB);
        // The individual + Pnd54 amounts must NOT appear.
        text.Should().NotContain("9000.00").And.NotContain("7000.00");
        // Rate emitted as percent (stored fraction 0.03 → 3.00).
        text.Should().Contain("|3.00|");
    }

    [SkippableFact]
    public async Task Payee_without_tax_id_fails_the_export()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var period = RandPeriod();
        await AddCert(sp, PeriodDate(period),
            CustomerType.Corporate, WhtFormType.Pnd53, payeeTaxId: null, 1000m, 0.03m);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IWhtBatchExportService>();
        var act = () => svc.BuildAsync("PND53", period, default);
        (await act.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("wht_batch.missing_tax_id");
    }

    // C2/T9 (specs/pnd2-filing.md, I5) — INC_TYPE_PND is Mandatory; a Pnd2 cert with a null/blank
    // Pnd2IncomeCode must fail the BATCH EXPORT loudly, never emit a blank mandatory field — but
    // the ON-SCREEN filing (B1's GeneratePnd2Async) must still show the row so the user can file
    // by hand (I5's other half, N2 Tier-2 round 2 strengthening).
    [SkippableFact]
    public async Task Pnd2_cert_without_income_code_fails_the_export()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var period = RandPeriod();
        await using (var s0 = sp.CreateAsyncScope())
        {
            var db = s0.ServiceProvider.GetRequiredService<AccountingDbContext>();
            db.WhtCertificates.Add(new WhtCertificate
            {
                CompanyId = 1, BranchId = 1, DocNo = "WT-" + Sfx(), CertDate = PeriodDate(period),
                Direction = "P", PayerTaxId = "0105500001234", PayerBranchCode = "00000",
                PayerName = "TEAS Co", PayerAddress = "BKK",
                PayeeTaxId = PayeeId(), PayeeName = "นายทดสอบ " + Sfx(),
                PayeeAddress = "BKK", PayeeType = CustomerType.Individual, FormType = WhtFormType.Pnd2,
                IncomeTypeCode = "4", Pnd2IncomeCode = null,   // missing — the guard's target
                IncomeAmount = 1000m, WhtRate = 0.15m, WhtAmount = 150m,
                Status = DocumentStatus.Posted, IssuedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await using var s = sp.CreateAsyncScope();
        var batchSvc = s.ServiceProvider.GetRequiredService<IWhtBatchExportService>();
        var act = () => batchSvc.BuildAsync("PND2", period, default);
        (await act.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("wht_batch.missing_income_code");

        // I5's other half — the on-screen preview still shows the row despite the missing code.
        var filingSvc = s.ServiceProvider.GetRequiredService<IWhtFilingService>();
        var filing = await filingSvc.GeneratePnd2Async(period, TaxFilingMode.Preview, default);
        filing.Rows.Should().ContainSingle(r => r.WhtAmount == 150m);
    }

    // T8 (specs/pnd2-filing.md, I4) — the ภ.ง.ด.2 batch file's header totals must equal the
    // on-screen filing's row count/totals for the same period (both read the same certs).
    [SkippableFact]
    public async Task Pnd2_batch_file_totals_agree_with_the_on_screen_filing()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var period = RandPeriod();
        var d = PeriodDate(period);
        await using (var s0 = sp.CreateAsyncScope())
        {
            var db = s0.ServiceProvider.GetRequiredService<AccountingDbContext>();
            db.WhtCertificates.AddRange(
                MakePnd2Cert(d, PayeeId(), 1000m, 150m),
                MakePnd2Cert(d, PayeeId(), 2000m, 300m));
            await db.SaveChangesAsync();
        }

        await using var s = sp.CreateAsyncScope();
        var filingSvc = s.ServiceProvider.GetRequiredService<IWhtFilingService>();
        var batchSvc = s.ServiceProvider.GetRequiredService<IWhtBatchExportService>();

        var filing = await filingSvc.GeneratePnd2Async(period, TaxFilingMode.Preview, default);
        var file = await batchSvc.BuildAsync("PND2", period, default);

        var text = Encoding.UTF8.GetString(file.Content);
        var header = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)[0].Split('|');
        header[14].Should().Be(filing.Rows.Count.ToString());              // TOT_NUM
        header[15].Should().Be(WhtBatchFormat.N(filing.Totals.Income));    // TOT_AMT
        header[16].Should().Be(WhtBatchFormat.N(filing.Totals.Wht));       // TOT_TAX
        file.RecordCount.Should().Be(filing.Rows.Count);
    }

    private static WhtCertificate MakePnd2Cert(
        DateOnly date, string payeeTaxId, decimal income, decimal wht) => new()
    {
        CompanyId = 1, BranchId = 1, DocNo = "WT-" + Sfx(), CertDate = date,
        Direction = "P", PayerTaxId = "0105500001234", PayerBranchCode = "00000",
        PayerName = "TEAS Co", PayerAddress = "BKK",
        PayeeTaxId = payeeTaxId, PayeeName = "นายทดสอบ " + Sfx(),
        PayeeAddress = "BKK", PayeeType = CustomerType.Individual, FormType = WhtFormType.Pnd2,
        IncomeTypeCode = "4", Pnd2IncomeCode = "2",
        IncomeAmount = income, WhtRate = wht / income, WhtAmount = wht,
        Status = DocumentStatus.Posted, IssuedAt = DateTimeOffset.UtcNow,
    };

    [SkippableFact]
    public async Task Empty_period_throws_no_data()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IWhtBatchExportService>();
        var act = () => svc.BuildAsync("PND53", RandPeriod(), default);
        (await act.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("wht_batch.no_data");
    }
}
