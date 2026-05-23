import type { PaperLineItem, PaperSummary } from '@/components/paper/types';

// Sprint 13j-FE C6 — shared mapping from create-form LineItemsTable rows to
// PaperDocument items + live totals (used by the sticky create previews).
export interface FormLine {
  descriptionTh: string;
  quantity: number;
  unitPrice: number;
  taxRate: number; // fraction, e.g. 0.07
  discountPercent?: number;
  uomText?: string;
}

export function buildPaperItems(lines: FormLine[]): PaperLineItem[] {
  return lines.map((l) => ({
    description: l.descriptionTh,
    quantity: l.quantity,
    unit: l.uomText,
    unitPrice: l.unitPrice,
    discountPercent: l.discountPercent,
    amount: l.quantity * l.unitPrice * (1 - (l.discountPercent ?? 0) / 100),
  }));
}

export function buildPaperSummary(lines: FormLine[]): PaperSummary {
  const t = lines.reduce(
    (acc, l) => {
      const gross = l.quantity * l.unitPrice;
      const disc = gross * ((l.discountPercent ?? 0) / 100);
      const net = gross - disc;
      acc.subtotal += gross;
      acc.discount += disc;
      acc.vat += net * l.taxRate;
      acc.total += net + net * l.taxRate;
      return acc;
    },
    { subtotal: 0, discount: 0, vat: 0, total: 0 },
  );
  return { subtotal: t.subtotal, discount: t.discount, vat: t.vat, total: t.total };
}
