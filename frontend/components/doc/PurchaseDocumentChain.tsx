'use client';

import Link from 'next/link';
import { useTranslations } from 'next-intl';
import {
  Link2, ClipboardList, FileSpreadsheet, Banknote, FileBadge, type LucideIcon,
} from 'lucide-react';
import {
  usePurchaseOrder, useVendorInvoice, usePaymentVoucher, useWhtCertificate,
} from '@/lib/queries';
import { StatusBadge } from '@/components/ui/StatusBadge';

// Sprint 13j-PURCH D-supplement (FE-only) — the Purchase document chain
// (PO → VI → PV → WHT) rendered vertically on every Purchase detail page,
// mirroring the Sales <DocumentChain> look. The current node is highlighted;
// every other resolved node links to its detail page.
//
// RESOLUTION (no BE endpoint added): nodes are resolved purely from the
// cross-refs ALREADY carried on each detail DTO. Sprint 13j-PURCH Flag-2 added
// the missing DOWNWARD refs, so the chain now resolves in BOTH directions.
//   UPWARD (child → parent):
//     • WhtCertificateDetail.paymentVoucherId  → PV
//     • PaymentVoucherDetail.vendorInvoiceId    → VI
//     • VendorInvoiceDetail.purchaseOrderId     → PO
//   DOWNWARD (parent → child):
//     • PurchaseOrderDetail.linkedVis[]         → VI
//     • VendorInvoiceDetail.settlingPvs[]       → PV   (Flag-2, new)
//     • PaymentVoucherDetail.whtCertificates[]  → WHT  (Flag-2, new)
// We hydrate each referenced doc with its own detail hook so we can show its
// docNo + status. When several downward children exist we pick the FIRST
// (lowest id, BE-ordered) as the chain's representative node — the chain shows a
// single linear path, not a fan-out. Nodes with no resolvable ref are omitted
// (no invented data).

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

export function PurchaseDocumentChain({ type, id }: PurchaseDocumentChainProps) {
  const t = useTranslations('purchaseChain');

  // Hydrate the open doc directly. Each hook is `enabled` only when its id is
  // known (passing 0 disables it), so at most a short walk fires.
  const po = usePurchaseOrder(type === 'purchase-order' ? id : 0);
  const viDirect = useVendorInvoice(type === 'vendor-invoice' ? id : 0);
  const pvDirect = usePaymentVoucher(type === 'payment-voucher' ? id : 0);
  const whtDirect = useWhtCertificate(type === 'wht-certificate' ? id : 0);

  // ── Upward parents (child → parent), resolved from the open doc's own DTO. ──
  const upPvId = type === 'wht-certificate' ? whtDirect.data?.paymentVoucherId ?? 0 : 0;
  const upPv = usePaymentVoucher(upPvId > 0 ? upPvId : 0);
  const pvUpward = type === 'payment-voucher' ? pvDirect.data : upPv.data;

  const upViId = pvUpward?.vendorInvoiceId ?? 0;
  // VI may also be reached downward from a PO (linkedVis). Compute that first so
  // a single useVendorInvoice covers both the upward and PO-downward cases.
  const poDoc = po.data;
  const poLinkedViId = poDoc?.linkedVis?.[0]?.vendorInvoiceId ?? 0;
  const hubViId =
    type === 'vendor-invoice' ? id
      : upViId > 0 ? upViId
        : poLinkedViId; // 0 if none
  const hubVi = useVendorInvoice(type !== 'vendor-invoice' && hubViId > 0 ? hubViId : 0);
  const vi = type === 'vendor-invoice' ? viDirect.data : hubVi.data;

  const upPoId = vi?.purchaseOrderId ?? 0;
  const upPo = usePurchaseOrder(type !== 'purchase-order' && upPoId > 0 ? upPoId : 0);
  const poResolved = type === 'purchase-order' ? poDoc : upPo.data;

  // ── Downward children (parent → child), using the Flag-2 refs. ──
  // PV: upward from WHT, else downward from the resolved VI's settlingPvs[0].
  const downPvId = vi?.settlingPvs?.[0]?.paymentVoucherId ?? 0;
  const hubPvId =
    type === 'payment-voucher' ? id
      : upPvId > 0 ? upPvId
        : downPvId;
  // Hydrate the PV when we only know it via a downward ref (need its whtCertificates).
  const downPv = usePaymentVoucher(
    type !== 'payment-voucher' && upPvId === 0 && downPvId > 0 ? downPvId : 0,
  );
  const pv = type === 'payment-voucher' ? pvDirect.data
    : upPvId > 0 ? upPv.data
      : downPv.data;

  // WHT: from the open doc, else downward from the resolved PV's whtCertificates[0].
  const downWht = pv?.whtCertificates?.[0];
  const resolvedWhtId = type === 'wht-certificate' ? id : downWht?.whtCertificateId ?? 0;

  const nodes: ChainNodeView[] = [];

  // PO node
  const resolvedPoId = poResolved?.purchaseOrderId ?? (type === 'purchase-order' ? id : upPoId);
  if (resolvedPoId > 0) {
    nodes.push({
      anchor: 'purchase-order', id: resolvedPoId,
      labelKey: LABEL_KEY['purchase-order'], route: ROUTE['purchase-order'],
      icon: ICON['purchase-order'], docNo: poResolved?.docNo, status: poResolved?.status,
    });
  }

  // VI node (upward from PV/VI, or downward from PO.linkedVis)
  const poLinkedVi = poResolved?.linkedVis?.[0];
  const resolvedViId =
    (vi?.vendorInvoiceId ?? (type === 'vendor-invoice' ? id : hubViId)) || poLinkedVi?.vendorInvoiceId || 0;
  if (resolvedViId > 0) {
    nodes.push({
      anchor: 'vendor-invoice', id: resolvedViId,
      labelKey: LABEL_KEY['vendor-invoice'], route: ROUTE['vendor-invoice'],
      icon: ICON['vendor-invoice'],
      docNo: vi?.docNo ?? poLinkedVi?.docNo, status: vi?.status,
    });
  }

  // PV node (upward from WHT, or downward from VI.settlingPvs)
  const viDownPv = vi?.settlingPvs?.[0];
  const resolvedPvId = pv?.paymentVoucherId ?? (type === 'payment-voucher' ? id : hubPvId);
  if (resolvedPvId > 0) {
    nodes.push({
      anchor: 'payment-voucher', id: resolvedPvId,
      labelKey: LABEL_KEY['payment-voucher'], route: ROUTE['payment-voucher'],
      icon: ICON['payment-voucher'],
      docNo: pv?.docNo ?? viDownPv?.docNo, status: pv?.status ?? viDownPv?.status,
    });
  }

  // WHT node (the open cert, or downward from PV.whtCertificates)
  if (resolvedWhtId > 0) {
    nodes.push({
      anchor: 'wht-certificate', id: resolvedWhtId,
      labelKey: LABEL_KEY['wht-certificate'], route: ROUTE['wht-certificate'],
      icon: ICON['wht-certificate'],
      docNo: whtDirect.data?.docNo ?? downWht?.docNo,
      status: whtDirect.data?.status ?? downWht?.status,
    });
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
