using System.Text.Json;
using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Sales;
using Accounting.Infrastructure.Persistence;
using Accounting.Infrastructure.Sales;
using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Accounting.Api.Tests.Sales;

/// <summary>
/// specs/fix-chain-conversion-integrity.md — F13. <c>tax_code_id</c> is a GLOBAL identity
/// column on a per-company table (tax.tax_codes), yet six frontend forms hardcoded
/// <c>taxCodeId: 1</c>. On any company other than the one that happens to own row 1, every
/// sales line stored another tenant's tax-code id, and an unknown/orphan code string (like the
/// prod "V7" row) was stored verbatim instead of resolving to the company's real code. These
/// prove the new SalesLineBackstop.Resolve ladder (I4): a matched code stores the MASTER row's
/// own (id, code); an unmatched/blank code resolves to the company's OWN standard output code
/// (never a foreign id, never an orphan string); and a tenant with no tax-code master at all
/// never throws (memory seed-cos-bypass-createasync-taxcodes).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class TaxCodePairIntegrityTests
{
    private readonly PostgresFixture _fx;
    public TaxCodePairIntegrityTests(PostgresFixture fx) => _fx = fx;

    private ServiceProvider Provider(int companyId, int branchId) =>
        TestCompanyFactory.BuildProvider(_fx.ConnectionString, companyId, branchId);

    // Doc date in the CURRENT month so the accounting period is open.
    private static DateOnly Today()
    {
        var n = DateTime.UtcNow;
        return new DateOnly(n.Year, n.Month, 16);
    }

    private static async Task<(decimal Rate, string Code, int TaxCodeId)> CreateLineAsync(
        ServiceProvider sp, long customerId, string taxCode, decimal price = 1000m)
    {
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        var id = await svc.CreateDraftAsync(new CreateTaxInvoiceRequest(
            Today(), customerId, false, "THB", 1m, null, null, null,
            [new TaxInvoiceLineInput(null, null, "บริการ", 1m, 1, "ครั้ง", price, 0m, 1, taxCode, 0m)],
            null), default);

        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var line = await db.TaxInvoiceLines.AsNoTracking()
            .Where(l => l.TaxInvoiceId == id).OrderBy(l => l.LineNo).FirstAsync();
        return (line.TaxRate, line.TaxCode ?? "", line.TaxCodeId);
    }

    private static async Task<int> MasterTaxCodeIdAsync(ServiceProvider sp, int companyId, string code)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        return await db.TaxCodes.Where(c => c.CompanyId == companyId && c.Code == code)
            .Select(c => c.TaxCodeId).SingleAsync();
    }

    [SkippableFact]
    public async Task Unknown_request_code_resolves_to_the_companys_own_standard_output_code()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        await using var sp = Provider(c.CompanyId, c.BranchId);
        var expectedId = await MasterTaxCodeIdAsync(sp, c.CompanyId, "VAT7");

        // "V7" — the orphan code that reached prod (F13): not a typo of "VAT7" that a fuzzy
        // match should catch, an unrelated string absent from EVERY company's master.
        var line = await CreateLineAsync(sp, c.CustomerId, "V7");

        line.Code.Should().Be("VAT7", "an orphan/unknown code must resolve to the company's own standard output code");
        line.Rate.Should().Be(0.07m);
        line.TaxCodeId.Should().Be(expectedId);
        line.TaxCodeId.Should().NotBe(1, "the hardcoded frontend taxCodeId:1 must never survive — this company's own row is not id 1");
    }

    [SkippableFact]
    public async Task Null_request_code_resolves_to_the_companys_own_standard_output_code()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        await using var sp = Provider(c.CompanyId, c.BranchId);
        var expectedId = await MasterTaxCodeIdAsync(sp, c.CompanyId, "VAT7");

        // WP-5 (§3.6) — the frontend no longer sends a taxCodeId/taxCode when the user hasn't
        // touched the tax-code picker (was: hardcoded taxCodeId:1, taxCode:'V7'). A null pair
        // must resolve exactly like an unmatched code (ladder step 3), never 422 — trap §9.6.
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        var id = await svc.CreateDraftAsync(new CreateTaxInvoiceRequest(
            Today(), c.CustomerId, false, "THB", 1m, null, null, null,
            [new TaxInvoiceLineInput(null, null, "บริการ", 1m, 1, "ครั้ง", 1000m, 0m, null, null, 0m)],
            null), default);

        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var line = await db.TaxInvoiceLines.AsNoTracking()
            .Where(l => l.TaxInvoiceId == id).OrderBy(l => l.LineNo).FirstAsync();

        line.TaxCode.Should().Be("VAT7");
        line.TaxRate.Should().Be(0.07m);
        line.TaxCodeId.Should().Be(expectedId);
    }

    // WP-5 trap §9.6 — leaving NotEmpty() on CreateTaxInvoiceValidator's TaxCode rule 422s
    // every tax invoice the instant the frontend sends null (no more hardcoded
    // taxCodeId:1/taxCode:'V7'). CreateDraftAsync above is called SERVICE-side, bypassing the
    // endpoint's validator entirely — this is the only test in this file that actually
    // exercises the validator FluentValidation.NotEmpty() would otherwise 422 on.
    [Fact]
    public void Validator_accepts_a_request_with_no_tax_code()
    {
        var v = new CreateTaxInvoiceValidator();
        var req = new CreateTaxInvoiceRequest(
            new DateOnly(2026, 6, 1), 1, false, "THB", 1m, null, null, null,
            [new TaxInvoiceLineInput(null, null, "บริการ", 1m, 1, "ครั้ง", 1000m, 0m, null, null, 0.07m)],
            null);
        v.Validate(req).IsValid.Should().BeTrue();
    }

    // fix-r2-u2 (L6-1/T1) — the ONLY deterministic RED for the DTO binding defect: a payload
    // byte-identical to BillingNoteForm.tsx's own submit shape (BillingNoteForm.tsx:238-263),
    // including "taxCodeId": null / "taxCode": null for an untouched line. Pure
    // System.Text.Json, no DB, no HTTP — avoids the WebApplicationFactory UseSetting footgun
    // entirely. Pre-fix (BillingLineInput.TaxCodeId is non-nullable int): System.Text.Json
    // throws JsonException on binding null into int. Post-fix (§3.1 widening): deserializes
    // clean and the null pair round-trips.
    [Fact]
    public void Billing_line_with_null_tax_code_pair_deserializes()
    {
        const string json = """
        {
          "docDate": "2026-08-19",
          "dueDate": "2026-08-19",
          "customerId": 1,
          "businessUnitId": null,
          "quotationId": null,
          "taxInvoiceIds": null,
          "currencyCode": "THB",
          "exchangeRate": 1,
          "notes": null,
          "internalNotes": null,
          "lines": [
            {
              "productId": null,
              "taxInvoiceId": null,
              "descriptionTh": "สินค้าทดสอบ",
              "quantity": 1,
              "uomText": "หน่วย",
              "unitPrice": 100,
              "discountPercent": 0,
              "taxCodeId": null,
              "taxCode": null,
              "taxRate": 0,
              "productType": "GOOD"
            }
          ]
        }
        """;

        // Act as a delegate first: pre-fix (BillingLineInput.TaxCodeId non-nullable int) this
        // throws JsonException — the deterministic RED. Written this way (not a direct property
        // assertion) so the test SOURCE compiles unchanged on both sides of the WP-1 edit; only
        // the runtime behaviour of Deserialize differs.
        CreateBillingNoteRequest? req = null;
        Action act = () => req = JsonSerializer.Deserialize<CreateBillingNoteRequest>(json, JsonOpts);
        act.Should().NotThrow<JsonException>("the FE's own null tax-code pair (BillingNoteForm.tsx:259-260) must bind");
        req.Should().NotBeNull();
        req!.Lines.Should().HaveCount(1);
        req.Lines[0].TaxCodeId.Should().BeNull();
        req.Lines[0].TaxCode.Should().BeNull();
    }

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    [SkippableFact]
    public async Task Exempt_code_keeps_its_code_and_id_and_zero_rate()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        await using var sp = Provider(c.CompanyId, c.BranchId);
        var expectedId = await MasterTaxCodeIdAsync(sp, c.CompanyId, "EXEMPT-BOOK");

        var line = await CreateLineAsync(sp, c.CustomerId, "EXEMPT-BOOK");

        line.Code.Should().Be("EXEMPT-BOOK");
        line.Rate.Should().Be(0m);
        line.TaxCodeId.Should().Be(expectedId);
    }

    [SkippableFact]
    public async Task Company_with_no_tax_code_master_still_creates_a_line()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        await using var sp = Provider(c.CompanyId, c.BranchId);

        // Simulate a raw-SQL-seeded tenant with zero tax codes (memory
        // seed-cos-bypass-createasync-taxcodes). tax.tax_rates has an FK to tax.tax_codes
        // (TaxRateConfiguration.cs:18) — delete the child rate rows first.
        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var codeIds = await db.TaxCodes.Where(x => x.CompanyId == c.CompanyId)
                .Select(x => x.TaxCodeId).ToListAsync();
            var rates = await db.TaxRates.Where(r => codeIds.Contains(r.TaxCodeId)).ToListAsync();
            db.TaxRates.RemoveRange(rates);
            await db.SaveChangesAsync();
            var codes = await db.TaxCodes.Where(x => x.CompanyId == c.CompanyId).ToListAsync();
            db.TaxCodes.RemoveRange(codes);
            await db.SaveChangesAsync();
        }

        var line = await CreateLineAsync(sp, c.CustomerId, "VAT7");

        line.Code.Should().Be("VAT7", "byte-for-byte today's legacy fallback code");
        line.Rate.Should().Be(0.07m, "the company's configured VAT rate — the resolver never throws on an empty master");
        line.TaxCodeId.Should().Be(0, "SYNTHETIC_TAX_CODE_ID — no master row backs this line's code");
    }

    // fix-r2-u2 (T4/L6-4/§3.2) — the copy-forward launder. ApplyTaxInvoiceLinesAsync
    // (BillingNoteService.cs:516) inherits a TI line's (tax_code_id, tax_code) verbatim, and
    // sales.tax_invoice_lines cannot be repaired by 639 (posted-line immutability trigger,
    // SqlScripts/582) — so the copy itself must launder. Recipe (do not fight the trigger):
    // create a DRAFT TI, raw-SQL-rewrite its line (permitted — the trigger only blocks
    // non-DRAFT parents), THEN post it (posting does not re-resolve lines), THEN group it into
    // a BN. Rewrites BOTH tax_code_id AND tax_code — rewriting only the id would not reach the
    // intended branch (a service-created line already stores a code string that IS in the
    // company's own master, so rule (b) would recover it regardless of intent).
    [SkippableFact]
    public async Task Billing_note_from_tax_invoice_launders_a_foreign_tax_code_id()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        var other = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        await using var sp = Provider(c.CompanyId, c.BranchId);
        await using var spOther = Provider(other.CompanyId, other.BranchId);
        var ownVat7Id = await MasterTaxCodeIdAsync(sp, c.CompanyId, "VAT7");
        // a real tax_code_id, but belonging to a DIFFERENT company — never a key in THIS
        // company's own AllById dictionary, exactly co3's shape (co1's VAT7 id under co3's row).
        var foreignId = await MasterTaxCodeIdAsync(spOther, other.CompanyId, "VAT7");

        async Task<long> CreateDraftTiAsync()
        {
            await using var s = sp.CreateAsyncScope();
            var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
            return await svc.CreateDraftAsync(new CreateTaxInvoiceRequest(
                Today(), c.CustomerId, false, "THB", 1m, null, null, null,
                [new TaxInvoiceLineInput(null, null, "บริการ", 1m, 1, "ครั้ง", 1000m, 0m, null, "VAT7", 0m)],
                null), default);
        }

        async Task RewriteLineAsync(long tiId, int taxCodeId, string taxCode)
        {
            await using var conn = new NpgsqlConnection(_fx.ConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "UPDATE sales.tax_invoice_lines SET tax_code_id = $1, tax_code = $2 " +
                "WHERE tax_invoice_id = $3", conn);
            cmd.Parameters.AddWithValue(taxCodeId);
            cmd.Parameters.AddWithValue(taxCode);
            cmd.Parameters.AddWithValue(tiId);
            (await cmd.ExecuteNonQueryAsync()).Should().Be(1);
        }

        async Task PostAsync(long tiId)
        {
            await using var s = sp.CreateAsyncScope();
            var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
            await svc.PostAsync(tiId, default);
        }

        async Task<BillingNoteLineSnapshot> GroupIntoBillingNoteAsync(long tiId)
        {
            await using var s = sp.CreateAsyncScope();
            var bnSvc = s.ServiceProvider.GetRequiredService<IBillingNoteService>();
            var bnId = await bnSvc.CreateDraftAsync(new CreateBillingNoteRequest(
                Today(), Today(), c.CustomerId, null, null, new[] { tiId }, "THB", 1m, null, null,
                Array.Empty<BillingLineInput>()), default);
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var line = await db.BillingNoteLines.AsNoTracking()
                .Where(l => l.BillingNoteId == bnId).OrderBy(l => l.LineNo).FirstAsync();
            var ti = await db.TaxInvoices.AsNoTracking().FirstAsync(t => t.TaxInvoiceId == tiId);
            return new BillingNoteLineSnapshot(line.TaxCodeId, line.TaxCode, line.TaxRate,
                line.LineAmount, line.TaxAmount, line.TotalAmount,
                ti.SubtotalAmount, ti.TaxAmount, ti.TotalAmount);
        }

        // Case (b) — inherited id foreign, inherited code string IS in this company's own
        // master → recovered by the code string, to THIS company's own VAT7 id.
        var tiB = await CreateDraftTiAsync();
        await RewriteLineAsync(tiB, foreignId, "VAT7");
        await PostAsync(tiB);
        var snapB = await GroupIntoBillingNoteAsync(tiB);

        snapB.TaxCodeId.Should().Be(ownVat7Id, "rule (b): recovered by the code string, to this company's own row");
        snapB.TaxCode.Should().Be("VAT7", "the string snapshot is never rewritten");
        snapB.TaxRate.Should().Be(0.07m);
        snapB.LineAmount.Should().Be(snapB.TiSubtotal);
        snapB.TaxAmount.Should().Be(snapB.TiTax);
        snapB.TotalAmount.Should().Be(snapB.TiTotal);

        // Case (c) — inherited id foreign, inherited code string ABSENT from this company's
        // master too (the exact co3 shape) → SYNTHETIC_TAX_CODE_ID.
        var tiC = await CreateDraftTiAsync();
        await RewriteLineAsync(tiC, foreignId, "VAT0");
        await PostAsync(tiC);
        var snapC = await GroupIntoBillingNoteAsync(tiC);

        snapC.TaxCodeId.Should().Be(0, "rule (c): no id and no code-string match — SYNTHETIC_TAX_CODE_ID");
        snapC.TaxCode.Should().Be("VAT0", "the string snapshot is never rewritten");
        snapC.TaxRate.Should().Be(0.07m);
        snapC.LineAmount.Should().Be(snapC.TiSubtotal);
        snapC.TaxAmount.Should().Be(snapC.TiTax);
        snapC.TotalAmount.Should().Be(snapC.TiTotal);
    }

    private sealed record BillingNoteLineSnapshot(
        int TaxCodeId, string? TaxCode, decimal TaxRate,
        decimal LineAmount, decimal TaxAmount, decimal TotalAmount,
        decimal TiSubtotal, decimal TiTax, decimal TiTotal);

    // fix-r2-u2 (T2/I2/I6) — a non-VAT company's manually-typed line (no tax-code picker ever
    // renders for it, LineItemsTable.tsx:103) must land exactly on the ladder's step-1 synthetic
    // pair, whatever the request carries.
    [SkippableFact]
    public async Task Non_vat_company_billing_note_line_stores_the_synthetic_pair()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: false);
        await using var sp = Provider(c.CompanyId, c.BranchId);
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IBillingNoteService>();
        var bnId = await svc.CreateDraftAsync(new CreateBillingNoteRequest(
            Today(), Today(), c.CustomerId, null, null, null, "THB", 1m, null, null,
            [new BillingLineInput(null, null, "สินค้า", 1m, "ชิ้น", 100m, 0m, null, null, 0m)]), default);

        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var line = await db.BillingNoteLines.AsNoTracking()
            .Where(l => l.BillingNoteId == bnId).OrderBy(l => l.LineNo).FirstAsync();

        line.TaxCodeId.Should().Be(0, "SYNTHETIC_TAX_CODE_ID — non-VAT company, ladder step 1");
        line.TaxCode.Should().Be("VAT0");
        line.TaxRate.Should().Be(0m);
        line.TaxAmount.Should().Be(0m);
    }

    // fix-r2-u2 (T3/I5) — BillingLineInput.TaxCodeId is never read by ApplyLinesAsync (§1.1);
    // a bogus request id must never reach storage, whatever value it carries.
    [SkippableFact]
    public async Task Bogus_request_tax_code_id_is_never_stored()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        await using var sp = Provider(c.CompanyId, c.BranchId);
        var expectedId = await MasterTaxCodeIdAsync(sp, c.CompanyId, "VAT7");

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IBillingNoteService>();
        var bnId = await svc.CreateDraftAsync(new CreateBillingNoteRequest(
            Today(), Today(), c.CustomerId, null, null, null, "THB", 1m, null, null,
            [new BillingLineInput(null, null, "สินค้า", 1m, "ชิ้น", 1000m, 0m, 999, null, 0m)]), default);

        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var line = await db.BillingNoteLines.AsNoTracking()
            .Where(l => l.BillingNoteId == bnId).OrderBy(l => l.LineNo).FirstAsync();

        line.TaxCodeId.Should().Be(expectedId, "TaxCodeId is never read — always the resolved standard output id");
        line.TaxCodeId.Should().NotBe(999);
        line.TaxCode.Should().Be("VAT7");
        line.TaxRate.Should().Be(0.07m);
    }

    // fix-r2-u2 (T5/I1/I3) — belt for I1: every column except tax_code_id survives the repair
    // script byte-identical, including the header totals. Runs the ACTUAL 639 script content
    // directly (this test's job is the column-fidelity invariant, not the RLS behaviour — T6
    // owns that).
    [SkippableFact]
    public async Task Repair_script_changes_only_tax_code_id()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        var other = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);

        long bnId;
        await using (var sp = Provider(c.CompanyId, c.BranchId))
        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<IBillingNoteService>();
            bnId = await svc.CreateDraftAsync(new CreateBillingNoteRequest(
                Today(), Today(), c.CustomerId, null, null, null, "THB", 1m, null, null,
                [new BillingLineInput(null, null, "สินค้าทดสอบ T5", 2m, "ชิ้น", 500m, 0m, null, null, 0m)]), default);
        }

        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();

        int foreignId;
        await using (var scalarCmd = new NpgsqlCommand(
            "SELECT tax_code_id FROM tax.tax_codes WHERE company_id = $1 AND code = 'VAT7'", conn))
        {
            scalarCmd.Parameters.AddWithValue(other.CompanyId);
            foreignId = (int)(await scalarCmd.ExecuteScalarAsync())!;
        }

        // Seed the violation — leaves the code string as the service wrote it ('VAT7', which IS
        // in `c`'s own master, rule (a)); only tax_code_id is corrupted. Any violation shape
        // proves I1 equally well; this one also cross-checks against T4/T6's rule (a).
        await using (var seedCmd = new NpgsqlCommand(
            "UPDATE sales.billing_note_lines SET tax_code_id = $1 WHERE billing_note_id = $2", conn))
        {
            seedCmd.Parameters.AddWithValue(foreignId);
            seedCmd.Parameters.AddWithValue(bnId);
            (await seedCmd.ExecuteNonQueryAsync()).Should().Be(1);
        }

        var before = await ReadLineSnapshotAsync(conn, bnId);
        var beforeHeader = await ReadHeaderSnapshotAsync(conn, bnId);

        var scriptPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src",
            "Accounting.Infrastructure", "Migrations", "SqlScripts",
            "639_repair_foreign_tax_code_id_on_sales_lines.sql");
        File.Exists(scriptPath).Should().BeTrue($"script not found at {scriptPath}");
        var sql = await File.ReadAllTextAsync(scriptPath);
        await using (var scriptCmd = new NpgsqlCommand(sql, conn) { CommandTimeout = 300 })
            await scriptCmd.ExecuteNonQueryAsync();

        var after = await ReadLineSnapshotAsync(conn, bnId);
        var afterHeader = await ReadHeaderSnapshotAsync(conn, bnId);

        after.TaxCodeId.Should().NotBe(before.TaxCodeId, "the seeded violation must actually be repaired");
        (after with { TaxCodeId = before.TaxCodeId }).Should().BeEquivalentTo(before,
            "I1 — every column except tax_code_id must be byte-identical after the repair");
        afterHeader.Should().BeEquivalentTo(beforeHeader, "I1 — header totals must never move");
    }

    private sealed record LineFullSnapshot(
        int LineNo, long? ProductId, string? ProductCode, string ProductType, long? TaxInvoiceId,
        string DescriptionTh, decimal Quantity, string UomText, decimal UnitPrice,
        decimal DiscountPercent, decimal DiscountAmount, decimal LineAmount,
        int TaxCodeId, string TaxCode, decimal TaxRate, decimal TaxAmount, decimal TotalAmount);

    private sealed record HeaderSnapshot(decimal SubtotalAmount, decimal VatAmount, decimal TotalAmount);

    private static async Task<LineFullSnapshot> ReadLineSnapshotAsync(NpgsqlConnection conn, long bnId)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT line_no, product_id, product_code, product_type, tax_invoice_id, description_th, " +
            "quantity, uom_text, unit_price, discount_percent, discount_amount, line_amount, " +
            "tax_code_id, tax_code, tax_rate, tax_amount, total_amount " +
            "FROM sales.billing_note_lines WHERE billing_note_id = $1 ORDER BY line_no", conn);
        cmd.Parameters.AddWithValue(bnId);
        await using var r = await cmd.ExecuteReaderAsync();
        (await r.ReadAsync()).Should().BeTrue();
        return new LineFullSnapshot(
            r.GetInt32(0), r.IsDBNull(1) ? null : r.GetInt64(1), r.IsDBNull(2) ? null : r.GetString(2),
            r.GetString(3), r.IsDBNull(4) ? null : r.GetInt64(4), r.GetString(5),
            r.GetDecimal(6), r.GetString(7), r.GetDecimal(8), r.GetDecimal(9), r.GetDecimal(10),
            r.GetDecimal(11), r.GetInt32(12), r.GetString(13), r.GetDecimal(14), r.GetDecimal(15), r.GetDecimal(16));
    }

    private static async Task<HeaderSnapshot> ReadHeaderSnapshotAsync(NpgsqlConnection conn, long bnId)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT subtotal_amount, vat_amount, total_amount FROM sales.billing_notes WHERE billing_note_id = $1", conn);
        cmd.Parameters.AddWithValue(bnId);
        await using var r = await cmd.ExecuteReaderAsync();
        (await r.ReadAsync()).Should().BeTrue();
        return new HeaderSnapshot(r.GetDecimal(0), r.GetDecimal(1), r.GetDecimal(2));
    }
}
