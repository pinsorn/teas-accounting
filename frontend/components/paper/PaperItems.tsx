import { fmtPaperNum, type PaperLineItem } from './types';

// ม.86/4 #5 — item name, qty, unit value per line. Min 10 rows (dashed fillers,
// design review 2026-07-02 — was 3; guide lines stretch the table toward the
// pinned signature footer so a short document has no big blank dead space).
export function PaperItems({ items }: { items: PaperLineItem[] }) {
  const fillers = Math.max(0, 10 - items.length);
  // Design review 2026-07-02 — the discount column only exists when some line
  // actually has a discount; otherwise its width goes to the Description column
  // (mirrors PaperDocumentPdf.Items). ColSpan of the empty/filler rows below must
  // track this so they don't overshoot the header's column count.
  const hasDiscount = items.some((i) => i.discountPercent != null && i.discountPercent !== 0);
  const colCount = hasDiscount ? 7 : 6;
  return (
    <table className="paper-items">
      <thead>
        <tr>
          <th>#</th>
          <th>รายการ / Description</th>
          <th className="num" style={{ width: 58 }}>จำนวน</th>
          <th style={{ width: 55 }}>หน่วย</th>
          <th className="num" style={{ width: 90 }}>ราคา/หน่วย</th>
          {hasDiscount && <th className="num" style={{ width: 65 }}>ส่วนลด</th>}
          <th className="num" style={{ width: 95 }}>จำนวนเงิน</th>
        </tr>
      </thead>
      <tbody>
        {items.map((it, i) => (
          <tr key={i}>
            <td>{i + 1}</td>
            <td>
              <div style={{ fontWeight: 600, lineHeight: 1.35 }}>{it.description || '—'}</div>
              {it.descriptionSub && (
                /* cont.119 — reference sub-text = 10px (design review 2026-07-02, was 11). */
                <div style={{ fontSize: 10, color: 'var(--ink-600)', marginTop: 2 }}>{it.descriptionSub}</div>
              )}
            </td>
            <td className="num">{it.quantity != null ? fmtPaperNum(it.quantity, 0) : '—'}</td>
            <td>{it.unit || '—'}</td>
            <td className="num">{it.unitPrice != null ? fmtPaperNum(it.unitPrice) : '—'}</td>
            {hasDiscount && (
              <td className="num">{it.discountPercent ? `${it.discountPercent}%` : '—'}</td>
            )}
            <td className="num">
              <b>{fmtPaperNum(it.amount)}</b>
            </td>
          </tr>
        ))}
        {items.length === 0 && (
          <tr>
            <td colSpan={colCount} style={{ textAlign: 'center', color: 'var(--ink-400)', padding: 30 }}>
              ยังไม่มีรายการ
            </td>
          </tr>
        )}
        {Array.from({ length: fillers }).map((_, i) => (
          <tr key={`e${i}`} className="empty-row">
            <td colSpan={colCount} />
          </tr>
        ))}
      </tbody>
    </table>
  );
}
