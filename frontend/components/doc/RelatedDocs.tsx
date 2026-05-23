'use client';

import Link from 'next/link';
import { Link2, ChevronRight } from 'lucide-react';
import type { DocumentCrossRefs } from '@/lib/types';

// Sprint 13j-FE D4 — related-document chips (Pattern X chain) for the detail
// side rail. Presentational: pages assemble `items` from useCrossReferences
// (TI/RC/CN/DN) or from inline ref fields (Q/SO/DO/BN). Hidden when empty.

export interface RelatedDocItem {
  type: string; // Thai label, e.g. "ใบกำกับภาษี"
  docNo: string;
  href: string;
}

// Convert the cross-ref graph (Q←SO←DO←TI←RC; CN/DN; BN) into chip items.
export function crossRefsToItems(refs: DocumentCrossRefs | undefined): RelatedDocItem[] {
  if (!refs) return [];
  const items: RelatedDocItem[] = [];
  const push = (label: string, route: string, r: { id: number; docNo: string | null } | null) => {
    if (r) items.push({ type: label, docNo: r.docNo ?? `#${r.id}`, href: `/${route}/${r.id}` });
  };
  push('ใบเสนอราคา', 'quotations', refs.quotation);
  push('ใบสั่งขาย', 'sales-orders', refs.salesOrder);
  push('ใบส่งของ', 'delivery-orders', refs.deliveryOrder);
  refs.taxInvoices.forEach((r) => push('ใบกำกับภาษี', 'tax-invoices', r));
  refs.receipts.forEach((r) => push('ใบเสร็จรับเงิน', 'receipts', r));
  refs.creditNotes.forEach((r) => push('ใบลดหนี้', 'credit-notes', r));
  refs.debitNotes.forEach((r) => push('ใบเพิ่มหนี้', 'debit-notes', r));
  refs.billingNotes.forEach((r) => push('ใบแจ้งหนี้', 'invoices', r));
  return items;
}

export function RelatedDocs({ items }: { items: RelatedDocItem[] }) {
  if (!items || items.length === 0) return null;
  return (
    <div className="rounded-card border border-ink-100 bg-base-100 shadow-warm-sm">
      <div className="flex items-center justify-between border-b border-ink-100 px-5 py-3.5">
        <h3 className="flex items-center gap-2 text-[15px] font-bold text-ink-900">
          <Link2 className="h-4 w-4 text-ink-600" aria-hidden /> เอกสารที่เกี่ยวข้อง
        </h3>
        <span className="text-[12px] text-ink-500">{items.length}</span>
      </div>
      <div className="flex flex-col gap-2 p-3">
        {items.map((it, i) => (
          <Link
            key={i}
            href={it.href}
            className="flex items-center gap-2.5 rounded-field border border-ink-100 bg-base-100 px-3 py-2.5 text-[13px] text-ink-900 transition-colors hover:border-peach-300 hover:bg-peach-50"
          >
            <span className="grid h-8 w-8 shrink-0 place-items-center rounded-lg bg-peach-50 text-peach-700">
              <Link2 className="h-4 w-4" aria-hidden />
            </span>
            <span className="min-w-0 flex-1">
              <span className="block text-[11px] font-semibold uppercase tracking-wide text-ink-500">{it.type}</span>
              <span className="block truncate font-semibold">{it.docNo}</span>
            </span>
            <ChevronRight className="h-3.5 w-3.5 shrink-0 text-ink-500" aria-hidden />
          </Link>
        ))}
      </div>
    </div>
  );
}
