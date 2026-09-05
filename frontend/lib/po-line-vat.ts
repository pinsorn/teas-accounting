// WP1.3 (F14) — PO-linked VI line vatRate derivation, extracted to a pure function so the
// four branches are unit-testable without mounting the VI create page. Scoped fix (do NOT
// blanket 0.07): only fall back to the company standard rate when BOTH company and vendor are
// VAT-registered; otherwise a PO line with no tax data stays 0% (a genuinely non-VAT PO/vendor
// must never gain a VAT charge it never had).
export function derivePoLineVatRate(
  line: { taxRate?: number | null; lineAmount: number; taxAmount: number },
  companyVat: boolean,
  vendorVat: boolean,
  stdRate: number,
): number {
  // WP-B (fix-po-vi-vat-derivation) — PoLineDto now delivers `taxRate` (backend Round 1a), so
  // this first branch is the common path for every PO-linked VI, not future-proofing: it wins
  // whenever the PO line carries its own rate — INCLUDING 0 (an exempt line must never fall
  // through to the reverse-derivation or the registered-company std-rate fallback below, which
  // are now legacy paths kept for callers whose line shape omits `taxRate`).
  if (line.taxRate != null) return line.taxRate;
  if (line.lineAmount > 0 && line.taxAmount > 0)
    return Math.round((line.taxAmount / line.lineAmount) * 100) / 100;
  return companyVat && vendorVat ? stdRate : 0;
}
