# Answer-Sana-Backend10 — Sprint 8.5: VAT-Mode Polish (non-VAT-registered companies)

**Date:** 2026-05-17
**From:** Ham (via Sana, Cowork)
**To:** Claude Code
**Re:** Post-Sprint-8 follow-up — gap-closing for `Tax:VatMode=false` companies
**Gate:** **Small surgical sprint. ~2 days. Hold scope.**
**Prereq:** Sprint 8 P4 must ship + Report-Backend10 must land before kicking off this sprint.

> Sprint 8 is closing. After it ships, run this **before Sprint 8.6 (AR-WHT)** because
> some of these gaps (PDF template branching) interact with the Receipt PDF changes
> Sprint 8.6 will make — better to land the branching foundation first.

---

## 1. Problem (4 gaps for non-VAT companies)

Plan §16.2 says TEAS supports VAT-mode all-or-nothing via env (`Tax:VatMode`). Currently
when `VatMode=false`:

| Gap | Where | Symptom |
|---|---|---|
| **(1) PDF says "ใบกำกับภาษี"** | QuestPDF templates for TI/RC/CN/DN | Non-VAT companies issuing a doc with "ใบกำกับภาษี" header = **illegal per ม.86** (only VAT-registered can use that term) — criminal exposure. Currently hardcoded label. |
| **(2) CN/DN legal-basis label** | CN/DN PDF templates | Non-VAT companies must cite ม.82/9 (price adjustment), not ม.86/10 (VAT credit). Currently hardcoded ม.86/10 reference. |
| **(3) e-Tax button visible** | Frontend TI/RC detail pages | Non-VAT companies cannot do e-Tax (ม.3 อัฏฐ — VAT-registered only). Button is shown anyway → confusing + can lead to attempted sign that fails. |
| **(4) No revenue-threshold warning** | Nowhere | Per ม.85/1, businesses must register VAT within 30 days of crossing 1.8M baht/year revenue. No system warning when non-VAT company is approaching/over. |

---

## 2. Fix — 4 small changes, all read from `Tax:VatMode` (already on `IConfiguration`)

### 2.1 PDF template branching

Each PDF service (`TaxInvoicePdfService`, `ReceiptPdfService`, `CreditNotePdfService`,
`DebitNotePdfService`) reads `Tax:VatMode` once at construction:

```csharp
private readonly bool _vatMode;
public TaxInvoicePdfService(IOptions<TaxConfig> tax) {
    _vatMode = tax.Value.VatMode;
}
```

Then in the `Generate` method, swap labels conditionally:

| Doc | `VatMode=true` (current) | `VatMode=false` (new) |
|---|---|---|
| TI header | "ใบกำกับภาษี / Tax Invoice" | "ใบส่งของ / Delivery Order" or "ใบแจ้งหนี้ / Invoice" (config — see §2.5) |
| TI VAT row | Show subtotal / VAT 7% / total | **Hide VAT row entirely**, show single "ยอดรวม" only |
| Receipt header | "ใบเสร็จรับเงิน" | "ใบเสร็จรับเงิน" (same — receipt doesn't depend on VAT status) |
| CN header | "ใบลดหนี้ / Credit Note (ม.86/10)" | "ใบลดหนี้ / Credit Note (ม.82/9)" |
| DN header | "ใบเพิ่มหนี้ / Debit Note (ม.86/9)" | "ใบเพิ่มหนี้ / Debit Note (ม.82/9)" |

Footer: the legal-ref line ("ออกตามมาตรา XX แห่งประมวลรัษฎากร") swaps per the table above.

### 2.2 CN/DN legal-basis label

Covered by §2.1 — same conditional render.

Note: the legal text on the document doesn't change CN/DN behavior at the GL level
(VAT-mode CN reverses VAT, non-VAT-mode CN doesn't have VAT to reverse anyway).
Pure presentation change.

### 2.3 UI hide e-Tax button when VatMode=false

`/system/info` already returns `vat_mode` boolean (per `Program.cs:94-102`). Frontend:

```tsx
// TI / Receipt detail pages
const { vatMode } = useSystemInfo();
{vatMode && (
  <Button onClick={signAndSendETax}>Sign + Send e-Tax</Button>
)}
```

Same pattern wherever "Sign e-Tax" / "Send e-Tax email" buttons exist. Audit all
4 detail pages (TI, RC, CN, DN) — verify no e-Tax CTA leaks when `vatMode=false`.

### 2.4 Revenue-threshold warning (optional but recommended)

**Where:** Dashboard / Sidebar widget — runs on login or on dashboard load.

**Logic:**
```csharp
// In a new SystemHealthService or extension of existing report service
public async Task<RevenueThresholdStatus> CheckVatThresholdAsync(CancellationToken ct)
{
    if (_taxConfig.VatMode) return RevenueThresholdStatus.NotApplicable;  // already VAT

    var twelveMonthsAgo = DateTime.UtcNow.AddYears(-1);
    var revenue = await _db.TaxInvoices
        .Where(ti => ti.PostedAt >= twelveMonthsAgo && ti.Status == DocumentStatus.Posted)
        .SumAsync(ti => ti.TotalAmountThb, ct);

    return revenue switch
    {
        >= 1_800_000m       => RevenueThresholdStatus.Exceeded,
        >= 1_500_000m       => RevenueThresholdStatus.Approaching,
        _                   => RevenueThresholdStatus.Ok
    };
}
```

**UI:** Banner on dashboard (alert variant):
- `Approaching` (rev ≥ 1.5M): "ยอดขายใกล้เกณฑ์ 1.8M — เตรียมจด VAT" (warning yellow)
- `Exceeded` (rev ≥ 1.8M): "ยอดขายเกินเกณฑ์ 1.8M — ต้องจด VAT ภายใน 30 วัน" (alert red)
- `Ok` / `NotApplicable`: nothing

Add `GET /system/vat-threshold-status` endpoint (anonymous-auth, no perm needed).

### 2.5 New config keys

Add to `appsettings.json` and `TaxConfig`:

```csharp
public sealed class TaxConfig
{
    // ... existing ...
    public string NonVatDocLabelTh { get; init; } = "ใบส่งของ";    // default
    public string NonVatDocLabelEn { get; init; } = "Delivery Order";
}
```

Why: some non-VAT businesses prefer "ใบแจ้งหนี้" over "ใบส่งของ" (depending on
business model). Make it configurable rather than hardcoded.

---

## 3. Scope cuts — explicitly OUT (do NOT improvise)

- ❌ **UI toggle for `VatMode`** — stays env-only (plan §16.2). Switching is a one-time
  per-company event handled by ops, not in-product.
- ❌ **Retroactive PDF regeneration** — old PDFs already generated stay as-is. Only new
  PDFs use the new branching.
- ❌ **VAT registration workflow** — when threshold-warning fires, just inform; don't
  build a "register for me" wizard. User goes to ภ.พ.01 manually at สรรพากร.
- ❌ **Re-issuing old TIs in new format** — out of scope, documents are immutable.
- ❌ **e-Tax disable per-company override** — `VatMode` env flag is the single switch.
- ❌ **Threshold rolling 12-month vs calendar year nuance** — use rolling-12-month for
  warning (more conservative). The official RD rule is ปีปฏิทิน but rolling is safer.
  Document this in §16.x note.

---

## 4. Verification gates (non-negotiable)

| Gate | Expectation |
|---|---|
| Backend build | 0/0 |
| Tests | Api 27+N / 27+N (likely +2 — `GetVatThresholdStatus` happy + boundary), Domain unchanged, 0 regression |
| tsc | 0 |
| next build | 0 (no new routes; only conditional rendering + threshold banner component) |
| Playwright | 15 existing + 1 new = **16/16** via system Edge — new spec: `non-vat-mode-pdf.spec.ts` (toggle env via TestServer config, post TI, download PDF, assert text contains "ใบส่งของ" not "ใบกำกับภาษี") |
| Manual PDF inspection | Generate 1 TI + 1 RC + 1 CN + 1 DN in `VatMode=false` mode — verify all 4 PDFs have correct labels (table §2.1) and no "ใบกำกับภาษี" string anywhere |
| Regression check | Generate same 4 docs in `VatMode=true` — unchanged from current behavior |

---

## 5. Definition of done

1. `TaxConfig` extended with `NonVatDocLabelTh/En`.
2. 4 PDF services branched on `_vatMode`.
3. CN/DN legal-ref strings parameterized (ม.86/10 ↔ ม.82/9, ม.86/9 ↔ ม.82/9).
4. Frontend `useSystemInfo()` hook (extend existing if present) exposes `vatMode`.
5. e-Tax CTA conditional on `vatMode` across TI/RC/CN/DN detail pages.
6. `IVatThresholdService` + `GET /system/vat-threshold-status` endpoint + dashboard banner component.
7. i18n th/en for: `nonVat.docLabel.*`, `vatThreshold.approaching.message`, `vatThreshold.exceeded.message`.
8. 1 new Playwright spec (`non-vat-mode-pdf.spec.ts`) + 1-2 unit tests on threshold service.
9. Manual PDF inspection ×8 (4 docs × 2 modes) — attach screenshots to Report-Backend11.
10. All gates green.
11. Mirror sync to `Y:\AccountApp\backend`.
12. `Report-Backend11.md` per template.
13. **Update `plan.md` §23.3** — strike Sprint 8.5 row with "✅ shipped".

---

## 6. After this sprint

Next: **Sprint 8.6 — AR-side WHT** (per plan §23.3). Sana writes spec when 8.5 is in
P-final. The PDF branching foundation laid here will be reused by 8.6's Receipt PDF
WHT section.

---

**Build it. ~2 days. Report back via Report-Backend11.**
