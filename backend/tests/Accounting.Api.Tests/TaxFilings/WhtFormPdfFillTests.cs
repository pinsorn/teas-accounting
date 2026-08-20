using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.TaxFilings;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Master;
using Accounting.Domain.Entities.Purchase;
using Accounting.Domain.Entities.Tax;
using Accounting.Domain.Enums;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Persistence;
using Accounting.Infrastructure.TaxFilings;
using PdfSharp.Pdf.IO;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.TaxFilings;

/// <summary>
/// Phase C/E render-correctness for the filled ภ.ง.ด.3 ใบแนบ and ภ.ง.ด.54 ม.70 PDFs, exercised with
/// real payee data through <see cref="IWhtFilingService"/> (the prior smoke only proved a non-empty PDF
/// with zero rows). Guards two compliance defects:
///   • ภ.ง.ด.3 ใบแนบ row scheme — the pnd3_attach template's row-1 slot lives in the Text1.* namespace
///     whose .0–.3 are the page header, so row-1 data is shifted +3 (taxId=Text1.4, not Text1.1). The
///     earlier best-guess scheme wrote row-1 taxId/name into header fields and rows 2–6's date→cond into
///     the wrong columns. A wrong box = a wrong filing (§4.1).
///   • ภ.ง.ด.54 — no app path emits FormType=Pnd54 (cert FormType is derived from vendor type), so the
///     pnd54 endpoint never saw real ม.70 amounts. Here we insert one and assert GeneratePnd54Async maps
///     it through to a populated filing.
/// Set TEAS_PDF_DUMP_DIR to also write the rendered PDFs for a manual box-for-box eyeball.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class WhtFormPdfFillTests
{
    private readonly PostgresFixture _fx;
    public WhtFormPdfFillTests(PostgresFixture fx) => _fx = fx;

    private ServiceProvider Provider()
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:Postgres"] = _fx.ConnectionString }).Build();
        var s = new ServiceCollection();
        s.AddLogging();
        s.AddSingleton<IConfiguration>(cfg);
        return s.AddInfrastructure(cfg)
            .AddSingleton<ITenantContext>(new StubTenant
            { CompanyId = 1, BranchId = 1, UserId = 1, IsSuperAdmin = false })
            .BuildServiceProvider();
    }

    private static string Sfx() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    private static int RandPeriod()  // distinct far-future period — the shared fixture persists rows
    {
        var r = Random.Shared;
        return (3000 + r.Next(0, 6000)) * 100 + r.Next(1, 13);
    }
    private static string PayeeId() => "1" + Random.Shared.NextInt64(0, 999_999_999_999L).ToString("000000000000");

    private static async Task AddCert(
        ServiceProvider sp, int period, int day, CustomerType payee, WhtFormType form,
        string payeeName, string incomeCode, string incomeDesc, decimal income, decimal rate)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        db.WhtCertificates.Add(new WhtCertificate
        {
            CompanyId = 1, BranchId = 1, DocNo = "WT-" + Sfx(),
            CertDate = new DateOnly(period / 100, period % 100, day),
            Direction = "P", PayerTaxId = "0105500001234", PayerBranchCode = "00000",
            PayerName = "บริษัท ทดสอบ จำกัด", PayerAddress = "กรุงเทพฯ",
            PayeeTaxId = PayeeId(), PayeeName = payeeName, PayeeAddress = "กรุงเทพฯ",
            PayeeType = payee, FormType = form,
            IncomeTypeCode = incomeCode, IncomeDescription = incomeDesc,
            IncomeAmount = income, WhtRate = rate,
            WhtAmount = decimal.Round(income * rate, 2),
            Status = DocumentStatus.Posted, IssuedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(default);
    }

    // O5 (specs/fix-o5-pp36-pdf.md) — seeds a Posted foreign-vendor Vendor + settling PaymentVoucher
    // with RequiresPnd36ReverseCharge=true, mirroring Sprint9WhtComplianceTests' GeneratePnd36Async
    // seed shape (ม.83/6 declares off the settling PV, not the VendorInvoice — R2/C2).
    private static async Task AddPnd36Pv(
        ServiceProvider sp, int period, int day, string vendorName, string country, decimal subtotal)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var dd = new DateOnly(period / 100, period % 100, day);
        var ven = new Vendor
        {
            CompanyId = 1, VendorCode = "V-" + Sfx(), NameTh = vendorName,
            IsForeign = true, CountryCode = country, VatRegistered = true,
        };
        db.Vendors.Add(ven);
        await db.SaveChangesAsync(default);

        db.PaymentVouchers.Add(new PaymentVoucher
        {
            CompanyId = 1, BranchId = 1, SubPrefix = "PV",
            DocDate = dd, PostingDate = dd,
            VendorId = ven.VendorId, VendorName = vendorName, VendorType = CustomerType.Corporate,
            CurrencyCode = "THB", ExchangeRate = 1m,
            SubtotalAmount = subtotal, VatAmount = 0m, WhtAmount = 0m,
            TotalPaid = subtotal, TotalAmountThb = subtotal,
            RequiresPnd36ReverseCharge = true,
            Status = DocumentStatus.Posted, PostedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(default);
    }

    private static void Dump(string file, byte[] pdf)
    {
        var dir = Environment.GetEnvironmentVariable("TEAS_PDF_DUMP_DIR");
        if (string.IsNullOrWhiteSpace(dir)) return;
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, file), pdf);
    }

    // ม.40 income types for ภ.ง.ด.3 individuals (description drives the printed ประเภทเงินได้ column).
    private static readonly (string Code, string Desc, decimal Rate)[] IndividualKinds =
    [
        ("5", "ค่าเช่าอาคาร",       0.05m),
        ("2", "ค่าบริการ",          0.03m),
        ("6", "ค่าวิชาชีพอิสระ",     0.03m),
        ("8", "ค่าจ้างทำของ",       0.03m),
        ("5", "ค่าเช่ารถยนต์",       0.05m),
        ("2", "ค่านายหน้า",         0.03m),
        ("8", "ค่าโฆษณา",           0.02m),
    ];

    // The actual guard against the wrong-box regression: pin the ภ.ง.ด.3 ใบแนบ field names per slot.
    // Row 1 lives in the header-shifted Text1.* namespace (+3); rows 2+ use the standard Text{k}.* scheme.
    // A render-only assertion can't catch this — the buggy mapping still produced a non-empty 7-row PDF.
    [Fact]
    public void Pnd3_attach_row_field_scheme_matches_the_template()
    {
        var r1 = WhtFilingService.Pnd3Layout.AttachRow(1);
        (r1.Seq, r1.TaxId, r1.Name, r1.Date, r1.IncomeType, r1.Rate, r1.Income, r1.Wht, r1.Cond)
            .Should().Be(("Text1.27", "Text1.4", "Text1.6", "Text1.9", "Text1.10", "Text1.11",
                          "Text1.12", "Text1.13", "Text1.14"));
        var r2 = WhtFilingService.Pnd3Layout.AttachRow(2);
        (r2.Seq, r2.TaxId, r2.Name, r2.Date, r2.IncomeType, r2.Rate, r2.Income, r2.Wht, r2.Cond)
            .Should().Be(("Text2.27", "Text2.1", "Text2.3", "Text2.6", "Text2.7", "Text2.8",
                          "Text2.9", "Text2.10", "Text2.11"));
    }

    [SkippableFact]
    public async Task Pnd3_attach_renders_individual_payee_rows_in_correct_columns()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var period = RandPeriod();

        // 7 individual payees → ใบแนบ spans 2 sheets (6 rows/page) and exercises both the row-1
        // (header-shifted) and rows 2–6 field schemes.
        for (var i = 0; i < IndividualKinds.Length; i++)
        {
            var (code, desc, rate) = IndividualKinds[i];
            await AddCert(sp, period, i + 1, CustomerType.Individual, WhtFormType.Pnd3,
                $"นายผู้รับเงิน รายที่ {i + 1}", code, desc, 10_000m + i * 10_000m + 123.45m, rate);
        }
        // Noise that ภ.ง.ด.3 must exclude: a juristic (→ ภ.ง.ด.53) and a ม.70 (→ ภ.ง.ด.54) payee.
        await AddCert(sp, period, 8, CustomerType.Corporate, WhtFormType.Pnd53,
            "บริษัท นิติบุคคล จำกัด", "5", "ค่าเช่า", 50_000m, 0.05m);
        await AddCert(sp, period, 9, CustomerType.Corporate, WhtFormType.Pnd54,
            "Foreign Vendor Inc.", "8", "ค่าบริการต่างประเทศ", 70_000m, 0.15m);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IWhtFilingService>();

        var filing = await svc.GeneratePnd3Async(period, TaxFilingMode.Preview, default);
        filing.Rows.Should().HaveCount(7, "only Individual non-ภ.ง.ด.54 payees belong on ภ.ง.ด.3");
        filing.Rows.Select(r => r.PayeeName).Should()
            .OnlyContain(n => n!.StartsWith("นายผู้รับเงิน"));

        var pdf = await svc.BuildPnd3PdfAsync(period, default);
        System.Text.Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
        pdf.Length.Should().BeGreaterThan(10_000, "a filled main page + 2 ใบแนบ sheets is non-trivial");
        Dump("_test_pnd3_attach.pdf", pdf);
    }

    [SkippableFact]
    public async Task Pnd54_maps_ma70_amounts_through_to_the_form()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var period = RandPeriod();

        await AddCert(sp, period, 10, CustomerType.Corporate, WhtFormType.Pnd54,
            "Foreign Service Co.", "8", "ค่าบริการต่างประเทศ ม.70", 250_000m, 0.15m);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IWhtFilingService>();

        var filing = await svc.GeneratePnd54Async(period, TaxFilingMode.Preview, default);
        filing.Rows.Should().HaveCount(1, "the inserted ม.70 cert is the only FormType=Pnd54 row");
        filing.Totals.Income.Should().Be(250_000m);
        filing.Totals.Wht.Should().Be(37_500m);

        var pdf = await svc.BuildPnd54PdfAsync(period, default);
        System.Text.Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
        pdf.Length.Should().BeGreaterThan(10_000);
        Dump("_test_pnd54.pdf", pdf);
    }

    // ภ.ง.ด.54 is one foreign payee per sheet — BuildPnd54PdfAsync must render EVERY ม.70 payment in
    // the period, not just the first (the v1 filler took only Rows[0], silently dropping the rest).
    [SkippableFact]
    public async Task Pnd54_renders_one_sheet_per_ma70_payment()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IWhtFilingService>();

        static int Pages(byte[] pdf)
        {
            using var doc = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
            return doc.PageCount;
        }

        // baseline: one ม.70 payment (the ภ.ง.ด.54 template is N pages/sheet — don't hard-code N).
        var p1 = RandPeriod();
        await AddCert(sp, p1, 5, CustomerType.Corporate, WhtFormType.Pnd54,
            "Foreign Vendor A", "8", "ค่าบริการต่างประเทศ", 100_000m, 0.15m);
        var single = Pages(await svc.BuildPnd54PdfAsync(p1, default));

        // two ม.70 payments → twice the sheets (the v1 bug rendered only Rows[0] = one sheet).
        var p2 = RandPeriod();
        await AddCert(sp, p2, 5, CustomerType.Corporate, WhtFormType.Pnd54,
            "Foreign Vendor A", "8", "ค่าบริการต่างประเทศ", 100_000m, 0.15m);
        await AddCert(sp, p2, 6, CustomerType.Corporate, WhtFormType.Pnd54,
            "Foreign Vendor B", "3", "ค่าสิทธิต่างประเทศ", 200_000m, 0.10m);
        (await svc.GeneratePnd54Async(p2, TaxFilingMode.Preview, default)).Rows.Should().HaveCount(2);

        Pages(await svc.BuildPnd54PdfAsync(p2, default))
            .Should().Be(2 * single, "one ภ.ง.ด.54 sheet per ม.70 payment");
    }

    // R2/L1-1/N3 (Tier-2 review round 2) — WhtFilingService has TWO independent unguarded
    // `prof?.TaxId ?? company.TaxId` resolutions (BuildWhtPdfAsync for ภ.ง.ด.3/53, and
    // BuildPnd54PdfAsync's ModelFor for ภ.ง.ด.54), both now guarded with the identical
    // PayerTaxIdRules.EnsureUsable call. This pins the ภ.ง.ด.3 path (BuildWhtPdfAsync, via
    // BuildPnd3PdfAsync) — one test per service per the extension scope; the ภ.ง.ด.54 site
    // shares the byte-identical guard call, verified by inspection + build.
    [SkippableFact]
    public async Task Pnd3_pdf_refuses_a_placeholder_company_profile_tax_id()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();

        var prof = await db.CompanyProfiles.SingleAsync(p => p.CompanyId == 1);
        var original = prof.TaxId;
        prof.TaxId = "0000000000000";
        await db.SaveChangesAsync();
        try
        {
            var svc = s.ServiceProvider.GetRequiredService<IWhtFilingService>();
            var act = () => svc.BuildPnd3PdfAsync(RandPeriod(), default);
            (await act.Should().ThrowAsync<DomainException>())
                .Which.Code.Should().Be("filing.payer_tax_id_missing");
        }
        finally
        {
            prof.TaxId = original;
            await db.SaveChangesAsync();
        }
    }

    // ── O5 (specs/fix-o5-pp36-pdf.md) — ภ.พ.36 filled PDF ──
    // Money invariant per sheet: printed(1.)=ServiceAmountThb, printed(2.)=printed(5.)=VatAmount
    // (rows 3/4 — เงินเพิ่ม/เบี้ยปรับ — are always blank, we only ever file on time). Asserted at the
    // SERVICE layer (the authoritative source BuildPp36PdfAsync prints from) — mirrors every other
    // filler test in this file, which never text-extracts a flattened comb render (Thai combining
    // marks scramble under extraction; the box PLACEMENT itself was render-verified via a screenshot
    // actually viewed during development, per specs/fix-o5-pp36-pdf.md, not re-asserted here).
    [SkippableFact]
    public async Task Pp36_maps_reverse_charge_payment_through_to_the_form()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var period = RandPeriod();

        await AddPnd36Pv(sp, period, 15, "Amazon Web Services, Inc.", "US", 50_000m);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IWhtFilingService>();

        var filing = await svc.GeneratePnd36Async(period, TaxFilingMode.Preview, default);
        filing.Rows.Should().HaveCount(1);
        filing.Rows[0].ServiceAmountThb.Should().Be(50_000m);
        filing.Rows[0].VatAmount.Should().Be(3_500m);          // 50,000 * 7%
        filing.TotalVat.Should().Be(filing.Rows[0].VatAmount); // (2.) == (5.) invariant, one row

        var pdf = await svc.BuildPp36PdfAsync(period, default);
        System.Text.Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
        pdf.Length.Should().BeGreaterThan(10_000);
        Dump("_test_pp36.pdf", pdf);
    }

    // ภ.พ.36's own page-2 instructions say to separate by payee ("แยกเปนแต่ละรายผู้รับ") — one
    // sheet per reverse-charge payment, same one-sheet-per-payment contract ภ.ง.ด.54 already proves.
    [SkippableFact]
    public async Task Pp36_renders_one_sheet_per_reverse_charge_payment()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IWhtFilingService>();

        static int Pages(byte[] pdf)
        {
            using var doc = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
            return doc.PageCount;
        }

        var p1 = RandPeriod();
        await AddPnd36Pv(sp, p1, 5, "Foreign Vendor A", "US", 20_000m);
        var single = Pages(await svc.BuildPp36PdfAsync(p1, default));

        var p2 = RandPeriod();
        await AddPnd36Pv(sp, p2, 5, "Foreign Vendor A", "US", 20_000m);
        await AddPnd36Pv(sp, p2, 6, "Foreign Vendor B", "SG", 30_000m);
        (await svc.GeneratePnd36Async(p2, TaxFilingMode.Preview, default)).Rows.Should().HaveCount(2);

        Pages(await svc.BuildPp36PdfAsync(p2, default))
            .Should().Be(2 * single, "one ภ.พ.36 sheet per reverse-charge payment");
    }

    // R2/L1-1/N3-shaped guard — BuildPp36PdfAsync is a NEW build path GeneratePnd36Async itself does
    // NOT guard (presentation-only concern, per the U1/U10 convention already applied to
    // BuildPnd54PdfAsync/BuildWhtPdfAsync).
    [SkippableFact]
    public async Task Pp36_pdf_refuses_a_placeholder_company_profile_tax_id()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();

        var prof = await db.CompanyProfiles.SingleAsync(p => p.CompanyId == 1);
        var original = prof.TaxId;
        prof.TaxId = "0000000000000";
        await db.SaveChangesAsync();
        try
        {
            var svc = s.ServiceProvider.GetRequiredService<IWhtFilingService>();
            var act = () => svc.BuildPp36PdfAsync(RandPeriod(), default);
            (await act.Should().ThrowAsync<DomainException>())
                .Which.Code.Should().Be("filing.payer_tax_id_missing");
        }
        finally
        {
            prof.TaxId = original;
            await db.SaveChangesAsync();
        }
    }
}
