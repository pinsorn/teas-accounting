import { bathText } from '@/lib/bath-text';
import { fmtPaperNum, type PaperSummary } from './types';

// Bilingual total label — Thai on top, English on the line below (Ham 2026-07-01),
// mirrored by the PDF (PaperDocumentPdf.Foot). Keeps the two languages from colliding
// on one line.
function Bi({ th, en }: { th: string; en: string }) {
  return (
    <span className="bi">
      <span>{th}</span>
      <span className="en">{en}</span>
    </span>
  );
}

// ม.86/4 #6 — VAT shown SEPARATELY in totals (compliance, never folded in).
export function PaperFoot({
  summary,
  notes,
  amountWords,
}: {
  summary: PaperSummary;
  notes?: string | null;
  amountWords?: string;
}) {
  // Round away float noise (0.07 * 100 = 7.000000000000001) so the VAT rate
  // prints cleanly on fiscal documents.
  const vatRate = Math.round((summary.vatRate ?? 7) * 100) / 100;
  const beforeVat = summary.beforeVat ?? summary.subtotal - (summary.discount ?? 0);
  // Footer sequence (Ham 2026-07-01) — mirrors the QuestPDF PaperDocumentPdf.Foot / PaperFootPlan so
  // the print matches this screen: Subtotal·VAT (only if VAT) → Grand Total (ALWAYS) → หัก WHT →
  // Net (only if WHT).
  // F-A (specs/fix-e2e-v1260-findings.md) — money semantics, canonical source
  // Infrastructure/Pdf/PaperFootPlan.cs:17-18,29: summary.total is the NET when summary.wht is
  // set ("จ่ายสุทธิ"), NOT the Grand Total — Grand = total + wht, Net = total. This comment
  // previously claimed the opposite (summary.total = Grand; Net = total − wht), which made the
  // Net row double-subtract WHT on screen (e.g. Grand 850/Net 700 for a doc whose PDF correctly
  // showed 1,000/-150/850). Never re-derive this locally — PaperFootPlan.cs is the single source
  // of truth; mirror it exactly.
  const hasWht = summary.wht != null;
  const grandTotal = hasWht ? summary.total + (summary.wht ?? 0) : summary.total;
  const netTotal = summary.total;
  const words = amountWords ?? bathText(netTotal);
  // Non-VAT (ม.86): hide the Subtotal/Before-VAT/VAT breakdown, leaving only Total.
  const showVat = summary.showVat ?? true;
  return (
    <div className="paper-foot">
      <div>
        {notes && (
          <div className="paper-notes">
            <div className="lbl">หมายเหตุ / Notes</div>
            {notes}
          </div>
        )}
      </div>
      <div className="paper-totals">
        {showVat && (
          <>
            <div className="row">
              <Bi th="มูลค่าก่อนหักส่วนลด" en="Subtotal" />
              <span className="v">{fmtPaperNum(summary.subtotal)}</span>
            </div>
            {summary.discount != null && (
              <div className="row">
                <Bi th="ส่วนลดรวม" en="Discount" />
                <span className="v">{fmtPaperNum(summary.discount)}</span>
              </div>
            )}
            <div className="row">
              <Bi th="มูลค่าก่อนภาษี" en="Before VAT" />
              <span className="v">{fmtPaperNum(beforeVat)}</span>
            </div>
            {/* ม.86/4 #5 — mixed taxable/exempt TI: label the non-taxable remainder
                (mirrors PDF PaperFootPlan FootLine.Exempt). */}
            {summary.nonTaxable != null && summary.nonTaxable > 0 && (
              <div className="row">
                <Bi th="มูลค่าสินค้าที่ได้รับยกเว้น" en="Exempt" />
                <span className="v">{fmtPaperNum(summary.nonTaxable)}</span>
              </div>
            )}
            <div className="row">
              {summary.vatLabel
                ? <Bi th={summary.vatLabel.th} en={summary.vatLabel.en} />
                : <Bi th={`ภาษีมูลค่าเพิ่ม ${vatRate}%`} en="VAT" />}
              <span className="v">{fmtPaperNum(summary.vat)}</span>
            </div>
          </>
        )}
        {hasWht ? (
          <>
            <div className="row">
              <Bi th="จำนวนเงินรวมทั้งสิ้น" en="Grand Total" />
              <span className="v">{fmtPaperNum(grandTotal)}</span>
            </div>
            <div className="row">
              <Bi th="หัก ณ ที่จ่าย" en="WHT" />
              {/* ASCII hyphen-minus — same codepoint the PDF prints (font-safe in Sarabun). */}
              <span className="v">-{fmtPaperNum(summary.wht)}</span>
            </div>
            <div className="row total">
              <Bi th="ยอดเงินรับสุทธิ" en="Net Payable" />
              <span className="v">฿&nbsp;{fmtPaperNum(netTotal)}</span>
            </div>
          </>
        ) : (
          <div className="row total">
            <Bi th="จำนวนเงินรวมทั้งสิ้น" en="Grand Total" />
            <span className="v">฿&nbsp;{fmtPaperNum(summary.total)}</span>
          </div>
        )}
        <div className="amount-words">({words})</div>
      </div>
    </div>
  );
}
