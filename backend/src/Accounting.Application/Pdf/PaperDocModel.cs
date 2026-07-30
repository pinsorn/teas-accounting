using System.Text.Json;
using System.Text.Json.Serialization;

namespace Accounting.Application.Pdf;

// Sprint 13j-PDF — C# mirror of the LOCKED PaperDocumentProps (§C4,
// frontend/components/paper/types.ts). Field names match 1:1 so the QuestPDF
// output equals the on-screen PaperDocument preview. Pure data — no entity refs;
// per-doctype mappers build this from the posted snapshot / company profile.
// cont.121 (canonical paper-DTO spec 2026-07-02) — moved from Accounting.Infrastructure.Pdf
// so Application service interfaces can expose it via GET /{doc}/{id}/paper.

public sealed record PaperSeller(
    string Name,
    string TaxId,
    string BranchCode,
    string Address,
    // cont.121 — never serialized to the /paper JSON endpoints: the FE keeps its own
    // logo source (company profile) and megabyte base64 blobs don't belong in the DTO.
    [property: JsonIgnore] byte[]? Logo = null,
    // Sprint 13k — an uploaded SVG company logo, surfaced as raw UTF-8 markup so the
    // PDF header can render it via QuestPDF's native .Svg() (vector, no raster loss).
    // Mutually exclusive with Logo in practice (a logo is one file); the renderer
    // prefers LogoSvg when both are set. Additive after Logo → positional callers
    // that pass Phone/Email by NAME (the only ones) are unaffected.
    [property: JsonIgnore] string? LogoSvg = null,
    string? Phone = null,
    string? Email = null);

public sealed record PaperCustomer(
    string Name,
    string? TaxId = null,
    string? BranchCode = null,
    string? Address = null,
    string? Contact = null,
    string? Phone = null);

public sealed record PaperLine(
    string Description,
    string? DescriptionSub,
    decimal? Quantity,
    string? Unit,
    decimal? UnitPrice,
    decimal? DiscountPercent,
    decimal Amount);

public sealed record PaperSummary(
    decimal Subtotal,
    decimal? Discount,
    decimal? BeforeVat,
    decimal Vat,
    decimal Total,
    decimal? VatRate, // percent, e.g. 7
    // Non-VAT mode (ม.86 — บริษัทไม่จด VAT): when false the foot prints a single
    // "ยอดรวม / Total" row only (no Subtotal/Before-VAT/VAT). Sourced from
    // ICompanyTaxConfigService.VatMode by the per-doctype mapper. Defaults true so positional
    // callers + the VAT-registered path are unaffected.
    bool ShowVat = true,
    // Sprint 13j-PURCH Phase C — Payment Voucher only: when set, the foot prints a
    // "หัก ณ ที่จ่าย · WHT" row above the grand total, and Total carries the
    // WHT-deducted net ("จ่ายสุทธิ"). null for every other doctype (additive, last
    // positional → existing callers unaffected).
    decimal? Wht = null,
    // ponytail (01-L3): Tax Invoice with mixed taxable/exempt lines — when > 0 a labelled
    // "มูลค่าสินค้าที่ได้รับยกเว้น · Exempt" row is printed so the non-taxable
    // remainder is explicit. null/0 = suppress (Q/SO/DO/BN callers unaffected).
    decimal? NonTaxable = null);

// cont.119 — party-box label override (mirrors FE PaperDocumentProps.partyLabel).
// The box is "ลูกค้า / Customer" by default; purchase docs where our company is NOT
// the customer pass their own label (PO/PV = "ผู้ขาย / Vendor"). Null → default.
public sealed record PaperPartyLabel(string Th, string En);

// cont.121 — serialize as a camelCase string ("success"|"danger"|"warning"|"info")
// so the FE PaperDocument variant union matches 1:1. NOTE: the attribute must sit on
// the PROPERTY — System.Text.Json resolves property attribute > options converters >
// type attribute, and the API registers a PascalCase JsonStringEnumConverter globally.
public sealed class PaperWatermarkVariantJsonConverter : JsonStringEnumConverter
{
    public PaperWatermarkVariantJsonConverter() : base(JsonNamingPolicy.CamelCase) { }
}

public enum PaperWatermarkVariant { Success, Danger, Warning, Info }

public sealed record PaperWatermark(
    string Text,
    [property: JsonConverter(typeof(PaperWatermarkVariantJsonConverter))] PaperWatermarkVariant Variant);

// Left/Right = the standard two-box signature strip. Middle is optional and only
// set by the Payment Voucher (Phase C) for a three-box strip
// (ผู้จัดทำ / ผู้อนุมัติ / ผู้รับเงิน); null → the renderer keeps the two-box layout.
public sealed record PaperSignRoles(string Left, string Right, string? Middle = null);

/// <summary>doc-signature spec (§D3) — resolved signature imagery + signer positions for the
/// signature strip. URLs and positions are SERIALIZED (the FE loads images through the BFF
/// proxy, exactly like the company logo); the BYTES are [JsonIgnore]d so megabyte blobs never
/// enter the /paper JSON — the same split as PaperSeller.Logo. A null record = the document is
/// not signed yet (Draft) → the renderer draws today's empty box. StampOnMiddle is true only for
/// the Payment Voucher (§A2). LeftName/MiddleName are the SIGNER'S PERSON NAME (User.FullName)
/// and ARE rendered on the ( name ) line of our own boxes, replacing the company name / the
/// 30-dot blank once a signer exists (§A4). Null → today's fallback. The counterparty box never
/// uses them.</summary>
public sealed record PaperSignatures(
    string? LeftUrl = null,
    string? MiddleUrl = null,
    string? StampUrl = null,
    string? LeftPosition = null,
    string? MiddlePosition = null,
    string? LeftName = null,
    string? MiddleName = null,
    bool StampOnMiddle = false,
    [property: JsonIgnore] byte[]? LeftBytes = null,
    [property: JsonIgnore] byte[]? MiddleBytes = null,
    [property: JsonIgnore] byte[]? StampBytes = null);

public sealed record PaperDocModel(
    string DocType,        // "ใบกำกับภาษี"
    string DocTypeEn,      // "TAX INVOICE"
    string DocNo,
    DateOnly IssueDate,
    PaperSeller Seller,
    PaperCustomer Customer,
    IReadOnlyList<PaperLine> Items,
    PaperSummary Summary,
    PaperSignRoles SignRoles,
    DateOnly? ValidUntil = null,
    string? ValidUntilLabel = null,
    string? AmountWords = null,
    string? Notes = null,
    PaperWatermark? Watermark = null,
    PaperPartyLabel? PartyLabel = null,
    // doc-signature spec (§D3) — appended LAST positional; all ten call sites pass every
    // param from Watermark/PartyLabel onward BY NAME (verified §1.2), so this is additive-safe.
    PaperSignatures? Signatures = null);
