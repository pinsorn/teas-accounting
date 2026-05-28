'use client';

import Link from 'next/link';
import { useTranslations } from 'next-intl';
import {
  Link2, ClipboardList, FileSpreadsheet, Banknote, FileBadge, type LucideIcon,
} from 'lucide-react';
import { usePurchaseChain } from '@/lib/queries';
import { StatusBadge } from '@/components/ui/StatusBadge';
import type { ChainNode } from '@/lib/types';

// F (Question-Backend36) — vertical Purchase chain rail (PO → VI → PV → WHT)
// rendered on every Purchase detail page. The current node is highlighted; every
// other resolved node links to its detail page.
//
// Previously this resolved the chain client-side from each detail DTO's
// cross-refs (4–N hooks: useVendorInvoice, usePaymentVoucher, useWhtCertificate,
// usePurchaseOrder + parents). That worked but kept Purchase out of parity with
// Sales tooling and fanned out network. The new GET /documents/purchase-chain
// endpoint walks the same graph on the server in one shot — this component is
// now a thin view over usePurchaseChain.

export type PurchaseChainAnchor =
  | 'purchase-order' | 'vendor-invoice' | 'payment-voucher' | 'wht-certificate';

interface ChainNodeView {
  anchor: PurchaseChainAnchor;
  id: number;
  labelKey: string;
  route: string;
  icon: LucideIcon;
  docNo?: string | null;
  status?: string | null;
}

const ICON: Record<PurchaseChainAnchor, LucideIcon> = {
  'purchase-order': ClipboardList,
  'vendor-invoice': FileSpreadsheet,
  'payment-voucher': Banknote,
  'wht-certificate': FileBadge,
};
const ROUTE: Record<PurchaseChainAnchor, string> = {
  'purchase-order': '/purchase-orders',
  'vendor-invoice': '/vendor-invoices',
  'payment-voucher': '/payment-vouchers',
  'wht-certificate': '/wht-certificates',
};
const LABEL_KEY: Record<PurchaseChainAnchor, string> = {
  'purchase-order': 'purchaseOrder',
  'vendor-invoice': 'vendorInvoice',
  'payment-voucher': 'paymentVoucher',
  'wht-certificate': 'whtCertificate',
};

export interface PurchaseDocumentChainProps {
  /** Anchor type of the document currently open. */
  type: PurchaseChainAnchor;
  /** Id of the document currently open. */
  id: number;
}

function toView(
  anchor: PurchaseChainAnchor, n: ChainNode,
): ChainNodeView {
  return {
    anchor,
    id: n.id,
    labelKey: LABEL_KEY[anchor],
    route: ROUTE[anchor],
    icon: ICON[anchor],
    docNo: n.docNo,
    status: n.status,
  };
}

export function PurchaseDocumentChain({ type, id }: PurchaseDocumentChainProps) {
  const t = useTranslations('purchaseChain');
  const { data: chain } = usePurchaseChain(type, id);

  const nodes: ChainNodeView[] = [];
  if (chain) {
    if (chain.purchaseOrder) nodes.push(toView('purchase-order', chain.purchaseOrder));
    // The chain rail shows a linear path, not the full fan-out — when the BE
    // returns multiple VIs/PVs/WHTs (e.g. one PO settled by several VIs), pick
    // the FIRST (lowest-id, BE-ordered) as representative. The detail pages of
    // any sibling can still be reached by navigating; this component is a
    // bread-crumb, not a tree view.
    if (chain.vendorInvoices[0]) nodes.push(toView('vendor-invoice', chain.vendorInvoices[0]));
    if (chain.paymentVouchers[0]) nodes.push(toView('payment-voucher', chain.paymentVouchers[0]));
    if (chain.whtCertificates[0]) nodes.push(toView('wht-certificate', chain.whtCertificates[0]));
  }

  if (nodes.length === 0) return null;

  return (
    <div className="rounded-card border border-ink-100 bg-base-100 shadow-warm-sm" data-testid="purchase-document-chain">
      <div className="flex items-center justify-between border-b border-ink-100 px-5 py-3.5">
        <h3 className="flex items-center gap-2 text-[15px] font-bold text-ink-900">
          <Link2 className="h-4 w-4 text-ink-600" aria-hidden /> {t('title')}
        </h3>
        <span className="text-[12px] text-ink-500">{nodes.length}</span>
      </div>
      <div className="flex flex-col gap-2 p-3">
        {nodes.map((n) => {
          const isCurrent = n.anchor === type && n.id === id;
          const Icon = n.icon;
          const docNo = n.docNo ?? `#${n.id}`;
          const inner = (
            <>
              <span className={`grid h-8 w-8 shrink-0 place-items-center rounded-lg ${isCurrent ? 'bg-peach-200 text-peach-800' : 'bg-peach-50 text-peach-700'}`}>
                <Icon className="h-4 w-4" aria-hidden />
              </span>
              <span className="min-w-0 flex-1">
                <span className="block text-[11px] font-semibold uppercase tracking-wide text-ink-500">{t(n.labelKey)}</span>
                <span className="block truncate font-semibold">{docNo}</span>
              </span>
              {n.status && (
                <span className="shrink-0"><StatusBadge status={n.status} /></span>
              )}
            </>
          );
          const cls = `flex items-center gap-2.5 rounded-field border px-3 py-2.5 text-[13px] transition-colors ${
            isCurrent
              ? 'border-peach-300 bg-peach-50 text-ink-900'
              : 'border-ink-100 bg-base-100 text-ink-900 hover:border-peach-300 hover:bg-peach-50'
          }`;
          return isCurrent ? (
            <div key={`${n.anchor}-${n.id}`} className={cls} data-testid="purchase-chain-row-current" aria-current="true">
              {inner}
            </div>
          ) : (
            <Link key={`${n.anchor}-${n.id}`} href={`${n.route}/${n.id}`} className={cls} data-testid="purchase-chain-row">
              {inner}
            </Link>
          );
        })}
      </div>
    </div>
  );
}
