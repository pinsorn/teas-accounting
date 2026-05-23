import { fmtPaperNum, type PaperLineItem } from './types';

// ม.86/4 #5 — item name, qty, unit value per line. Min 3 rows (dashed fillers).
export function PaperItems({ items }: { items: PaperLineItem[] }) {
  const fillers = Math.max(0, 3 - items.length);
  return (
    <table className="paper-items">
      <thead>
        <tr>
          <th>#</th>
          <th>รายการ / Description</th>
          <th className="num" style={{ width: 70 }}>จำนวน</th>
          <th style={{ width: 60 }}>หน่วย</th>
          <th className="num" style={{ width: 100 }}>ราคา/หน่วย</th>
          <th className="num" style={{ width: 70 }}>ส่วนลด</th>
          <th className="num" style={{ width: 110 }}>จำนวนเงิน</th>
        </tr>
      </thead>
      <tbody>
        {items.map((it, i) => (
          <tr key={i}>
            <td>{i + 1}</td>
            <td>
              <div style={{ fontWeight: 600 }}>{it.description || '—'}</div>
              {it.descriptionSub && (
                <div style={{ fontSize: 13, color: 'var(--ink-600)', marginTop: 2 }}>{it.descriptionSub}</div>
              )}
            </td>
            <td className="num">{it.quantity != null ? fmtPaperNum(it.quantity, 0) : '—'}</td>
            <td>{it.unit || '—'}</td>
            <td className="num">{it.unitPrice != null ? fmtPaperNum(it.unitPrice) : '—'}</td>
            <td className="num">{it.discountPercent ? `${it.discountPercent}%` : '—'}</td>
            <td className="num">
              <b>{fmtPaperNum(it.amount)}</b>
            </td>
          </tr>
        ))}
        {items.length === 0 && (
          <tr>
            <td colSpan={7} style={{ textAlign: 'center', color: 'var(--ink-400)', padding: 30 }}>
              ยังไม่มีรายการ
            </td>
          </tr>
        )}
        {Array.from({ length: fillers }).map((_, i) => (
          <tr key={`e${i}`} className="empty-row">
            <td colSpan={7} />
          </tr>
        ))}
      </tbody>
    </table>
  );
}
