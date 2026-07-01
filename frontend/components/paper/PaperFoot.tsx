import { bathText } from '@/lib/bath-text';
import { fmtPaperNum, type PaperSummary } from './types';

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
  // the print matches this screen: Subtotal·VAT (only if VAT) → Grand Total (ALWAYS, = summary.total)
  // → หัก WHT → Net (only if WHT). Here summary.total is the Grand Total; Net = total − wht.
  const hasWht = summary.wht != null;
  const netTotal = hasWht ? summary.total - (summary.wht ?? 0) : summary.total;
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
              <span>มูลค่าก่อนหักส่วนลด · Subtotal</span>
              <span className="v">{fmtPaperNum(summary.subtotal)}</span>
            </div>
            {summary.discount != null && (
              <div className="row">
                <span>ส่วนลดรวม · Discount</span>
                <span className="v">{fmtPaperNum(summary.discount)}</span>
              </div>
            )}
            <div className="row">
              <span>มูลค่าก่อนภาษี · Before VAT</span>
              <span className="v">{fmtPaperNum(beforeVat)}</span>
            </div>
            <div className="row">
              <span>ภาษีมูลค่าเพิ่ม {vatRate}% · VAT</span>
              <span className="v">{fmtPaperNum(summary.vat)}</span>
            </div>
          </>
        )}
        {hasWht ? (
          <>
            <div className="row">
              <span>จำนวนเงินรวมทั้งสิ้น · Grand Total</span>
              <span className="v">{fmtPaperNum(summary.total)}</span>
            </div>
            <div className="row">
              <span>หัก ณ ที่จ่าย · WHT</span>
              <span className="v">−{fmtPaperNum(summary.wht)}</span>
            </div>
            <div className="row total">
              <span>ยอดเงินรับสุทธิ · Net Payable</span>
              <span className="v">฿&nbsp;{fmtPaperNum(netTotal)}</span>
            </div>
          </>
        ) : (
          <div className="row total">
            <span>จำนวนเงินรวมทั้งสิ้น · Grand Total</span>
            <span className="v">฿&nbsp;{fmtPaperNum(summary.total)}</span>
          </div>
        )}
        <div className="amount-words">({words})</div>
      </div>
    </div>
  );
}
