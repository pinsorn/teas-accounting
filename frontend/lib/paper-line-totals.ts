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

// Non-VAT companies (ม.86) carry no VAT on any document; pass vatMode=false so the
// line tax rate never leaks a phantom VAT into the preview total.
export function buildPaperSummary(lines: FormLine[], vatMode = true): PaperSummary {
  const t = lines.reduce(
    (acc, l) => {
      const gross = l.quantity * l.unitPrice;
      const disc = gross * ((l.discountPercent ?? 0) / 100);
      const net = gross - disc;
      const vat = vatMode ? net * l.taxRate : 0;
      acc.subtotal += gross;
      acc.discount += disc;
      acc.vat += vat;
      acc.total += net + vat;
      return acc;
    },
    { subtotal: 0, discount: 0, vat: 0, total: 0 },
  );
  return { subtotal: t.subtotal, discount: t.discount, vat: t.vat, total: t.total };
}
