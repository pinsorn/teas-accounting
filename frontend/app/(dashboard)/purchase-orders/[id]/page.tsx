'use client';

import { use } from 'react';
import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { PageHeader } from '@/components/ui/PageHeader';
import { usePurchaseOrder, usePurchaseOrderAction } from '@/lib/queries';
import { AttachmentsSection } from '@/components/attachments/AttachmentsSection';
import { formatTHB, formatDate } from '@/lib/utils';

export default function PurchaseOrderDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const poId = Number(id);
  const t = useTranslations('purchaseOrder');
  const tc = useTranslations('common');
  const q = usePurchaseOrder(poId);
  const act = usePurchaseOrderAction();
  const d = q.data;

  async function run(action: string, body?: unknown) {
    try { await act.mutateAsync({ id: poId, action, body }); toast.success(tc('save')); }
    catch (e) { toast.error((e as { detail?: string })?.detail ?? tc('error')); }
  }

  if (!d) return <div className="p-6 text-base-content/50">{tc('loading')}</div>;

  return (
    <>
      <PageHeader title={`${t('title')} ${d.docNo ?? `#${d.purchaseOrderId}`}`} />
      <div className="mb-4 flex flex-wrap items-center gap-3">
        <span data-testid="po-status" className="badge badge-lg badge-ghost">{d.status}</span>
        <span>{d.vendorName}</span>
        {d.status === 'Draft' && (
          <button data-testid="po-approve" className="btn btn-success btn-sm"
            disabled={act.isPending} onClick={() => run('approve')}>{t('approve')}</button>
        )}
        {d.status === 'Approved' && (
          <>
            <button data-testid="po-mark-sent" className="btn btn-outline btn-sm"
              disabled={act.isPending} onClick={() => run('mark-sent')}>{t('sentToVendor')}</button>
            <button data-testid="po-close" className="btn btn-secondary btn-sm"
              disabled={act.isPending} onClick={() => run('close')}>{t('close')}</button>
          </>
        )}
        {(d.status === 'Draft' || d.status === 'Approved') && (
          <button data-testid="po-cancel" className="btn btn-ghost btn-sm text-error"
            disabled={act.isPending}
            onClick={() => {
              const reason = window.prompt(t('cancel') + '?');
              if (reason) run('cancel', { reason });
            }}>{t('cancel')}</button>
        )}
        <a className="btn btn-ghost btn-sm"
          href={`/api/proxy/purchase-orders/${poId}/pdf`}
          target="_blank" rel="noreferrer">PDF</a>
        {d.sentToVendorAt && (
          <span className="text-xs text-base-content/60">
            {t('sentToVendor')}: {formatDate(d.sentToVendorAt)}</span>
        )}
      </div>

      <div className="overflow-x-auto rounded-lg border border-base-300">
        <table className="table">
          <tbody>
            {d.lines.map((l) => (
              <tr key={l.lineNo}>
                <td>{l.descriptionTh}</td>
                <td className="text-right tabular-nums">{l.quantity}</td>
                <td className="text-right tabular-nums">{formatTHB(l.totalAmount)}</td>
              </tr>
            ))}
          </tbody>
          <tfoot><tr className="font-bold">
            <td colSpan={2} className="text-right">{t('total')}</td>
            <td className="text-right tabular-nums">{formatTHB(d.totalAmount)}</td>
          </tr></tfoot>
        </table>
      </div>

      {d.linkedVis.length > 0 && (
        <div className="mt-4">
          <h2 className="mb-2 font-semibold">{t('linkedTo')}</h2>
          <div className="rounded-lg border border-base-300 p-3 text-sm">
            {d.linkedVis.map((v) => (
              <div key={v.vendorInvoiceId} className="flex justify-between">
                <Link className="link link-primary font-mono"
                  href={`/vendor-invoices/${v.vendorInvoiceId}`}>
                  {v.docNo ?? `#${v.vendorInvoiceId}`}</Link>
                <span className="tabular-nums">{formatTHB(v.totalAmount)}</span>
              </div>
            ))}
            <div className="mt-2 flex justify-between border-t pt-1 font-bold">
              <span>{t('remaining')}</span>
              <span className="tabular-nums">{formatTHB(d.remaining)}</span>
            </div>
          </div>
        </div>
      )}

      <AttachmentsSection parentType="PURCHASE_ORDER" parentId={poId} />
    </>
  );
}
