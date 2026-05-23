'use client';

import Link from 'next/link';
import { useTranslations } from 'next-intl';
import {
  Link2, FileText, ClipboardList, Truck, FileSpreadsheet,
  ReceiptText, Banknote, FileMinus, FilePlus, type LucideIcon,
} from 'lucide-react';
import { useDocumentChain } from '@/lib/queries';
import { formatTHB, formatDate } from '@/lib/utils';
import { ChainRowPrint } from '@/components/doc/ChainRowPrint';
import type { ChainAnchorType, ChainNode, DocumentChain as Chain } from '@/lib/types';

// cont.69 Phase 3 (D7) — the FULL related-document chain
// (Q → SO → DO → Invoice → TI → RC, then CN/DN) rendered on EVERY sales detail
// page regardless of which node you opened. The current (type,id) row is
// highlighted; every other row links to its detail page. Phase 4 will inject
// per-row print buttons via the optional `rowActions` slot — nothing rendered there now.

type Kind =
  | 'quotation' | 'sales-order' | 'delivery-order' | 'invoice'
  | 'tax-invoice' | 'receipt' | 'credit-note' | 'debit-note';

interface Row {
  kind: Kind;
  // anchor type this row maps to when matching the current document
  anchor: ChainAnchorType;
  node: ChainNode;
  labelKey: string;        // crossRef.* key
  route: string;           // detail route prefix
  icon: LucideIcon;
}

// cont.69 Phase 4 (D8) — API route segment per row kind (differs from the FE
// `route`: Invoice's UI route is /invoices but its API group is billing-notes).
const API_SEG: Record<Kind, string> = {
  'quotation': 'quotations',
  'sales-order': 'sales-orders',
  'delivery-order': 'delivery-orders',
  'invoice': 'billing-notes',
  'tax-invoice': 'tax-invoices',
  'receipt': 'receipts',
  'credit-note': 'credit-notes',
  'debit-note': 'debit-notes',
};

const ROW_ICON: Record<Kind, LucideIcon> = {
  'quotation': FileText,
  'sales-order': ClipboardList,
  'delivery-order': Truck,
  'invoice': FileSpreadsheet,
  'tax-invoice': ReceiptText,
  'receipt': Banknote,
  'credit-note': FileMinus,
  'debit-note': FilePlus,
};

// CN vs DN: the chain returns them together in adjustmentNotes; the status string
// does not distinguish, so we rely on the docNo prefix (CN-/DN-) when present.
function isDebitNote(node: ChainNode): boolean {
  return (node.docNo ?? '').toUpperCase().includes('DN');
}

function buildRows(chain: Chain): Row[] {
  const rows: Row[] = [];
  if (chain.quotation)
    rows.push({ kind: 'quotation', anchor: 'quotation', node: chain.quotation, labelKey: 'quotation', route: '/quotations', icon: ROW_ICON.quotation });
  if (chain.salesOrder)
    rows.push({ kind: 'sales-order', anchor: 'sales-order', node: chain.salesOrder, labelKey: 'salesOrder', route: '/sales-orders', icon: ROW_ICON['sales-order'] });
  chain.deliveryOrders.forEach((n) =>
    rows.push({ kind: 'delivery-order', anchor: 'delivery-order', node: n, labelKey: 'deliveryOrder', route: '/delivery-orders', icon: ROW_ICON['delivery-order'] }));
  chain.invoices.forEach((n) =>
    rows.push({ kind: 'invoice', anchor: 'billing-note', node: n, labelKey: 'billingNote', route: '/invoices', icon: ROW_ICON.invoice }));
  chain.taxInvoices.forEach((n) =>
    rows.push({ kind: 'tax-invoice', anchor: 'tax-invoice', node: n, labelKey: 'taxInvoice', route: '/tax-invoices', icon: ROW_ICON['tax-invoice'] }));
  chain.receipts.forEach((n) =>
    rows.push({ kind: 'receipt', anchor: 'receipt', node: n, labelKey: 'receipt', route: '/receipts', icon: ROW_ICON.receipt }));
  chain.adjustmentNotes.forEach((n) => {
    const dn = isDebitNote(n);
    rows.push({
      kind: dn ? 'debit-note' : 'credit-note',
      anchor: 'adjustment-note',
      node: n,
      labelKey: dn ? 'debitNote' : 'creditNote',
      route: dn ? '/debit-notes' : '/credit-notes',
      icon: dn ? ROW_ICON['debit-note'] : ROW_ICON['credit-note'],
    });
  });
  return rows;
}

export interface DocumentChainProps {
  /** Anchor type of the document currently open. */
  type: ChainAnchorType;
  /** Id of the document currently open. */
  id: number;
  /** Phase 4 — override per-row actions. Defaults to a ต้นฉบับ/สำเนา print control. */
  rowActions?: (node: ChainNode, kind: Kind) => React.ReactNode;
}

// cont.69 Phase 4 (D8) — default per-row action: tracked original/copy print.
const defaultRowActions = (node: ChainNode, kind: Kind): React.ReactNode =>
  <ChainRowPrint docType={API_SEG[kind]} id={node.id} />;

export function DocumentChain({ type, id, rowActions = defaultRowActions }: DocumentChainProps) {
  const q = useDocumentChain(type, id);
  const t = useTranslations('crossRef');
  if (!q.data) return null;
  const rows = buildRows(q.data);
  if (rows.length === 0) return null;

  return (
    <div className="rounded-card border border-ink-100 bg-base-100 shadow-warm-sm" data-testid="document-chain">
      <div className="flex items-center justify-between border-b border-ink-100 px-5 py-3.5">
        <h3 className="flex items-center gap-2 text-[15px] font-bold text-ink-900">
          <Link2 className="h-4 w-4 text-ink-600" aria-hidden /> {t('title')}
        </h3>
        <span className="text-[12px] text-ink-500">{rows.length}</span>
      </div>
      <div className="flex flex-col gap-2 p-3">
        {rows.map((r) => {
          const isCurrent = r.anchor === type && r.node.id === id;
          const Icon = r.icon;
          const label = t(r.labelKey);
          const docNo = r.node.docNo ?? `#${r.node.id}`;
          const inner = (
            <>
              <span className={`grid h-8 w-8 shrink-0 place-items-center rounded-lg ${isCurrent ? 'bg-peach-200 text-peach-800' : 'bg-peach-50 text-peach-700'}`}>
                <Icon className="h-4 w-4" aria-hidden />
              </span>
              <span className="min-w-0 flex-1">
                <span className="block text-[11px] font-semibold uppercase tracking-wide text-ink-500">{label}</span>
                <span className="block truncate font-semibold">{docNo}</span>
                <span className="block text-[11px] text-ink-500">{formatDate(r.node.docDate)} · {r.node.status} · {formatTHB(r.node.total)}</span>
              </span>
              {rowActions && (
                <span className="shrink-0" onClick={(e) => e.stopPropagation()}>{rowActions(r.node, r.kind)}</span>
              )}
            </>
          );
          const cls = `flex items-center gap-2.5 rounded-field border px-3 py-2.5 text-[13px] transition-colors ${
            isCurrent
              ? 'border-peach-300 bg-peach-50 text-ink-900'
              : 'border-ink-100 bg-base-100 text-ink-900 hover:border-peach-300 hover:bg-peach-50'
          }`;
          return isCurrent ? (
            <div key={`${r.anchor}-${r.node.id}`} className={cls} data-testid="chain-row-current" aria-current="true">
              {inner}
            </div>
          ) : (
            <Link key={`${r.anchor}-${r.node.id}`} href={`${r.route}/${r.node.id}`} className={cls} data-testid="chain-row">
              {inner}
            </Link>
          );
        })}
      </div>
    </div>
  );
}
