'use client';

import { use } from 'react';
import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { PageHeader } from '@/components/ui/PageHeader';
import { useDeliveryOrder, useDeliveryOrderAction } from '@/lib/queries';
import { formatTHB } from '@/lib/utils';
import { AttachmentsSection } from '@/components/attachments/AttachmentsSection';

export default function DeliveryOrderDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const doId = Number(id);
  const t = useTranslations('deliveryOrder');
  const tc = useTranslations('common');
  const q = useDeliveryOrder(doId);
  const act = useDeliveryOrderAction();
  const d = q.data;

  async function run(action: string) {
    try { await act.mutateAsync({ id: doId, action }); toast.success(tc('save')); }
    catch (e) { toast.error((e as { detail?: string })?.detail ?? tc('error')); }
  }

  if (!d) return <div className="p-6 text-base-content/50">{tc('loading')}</div>;

  return (
    <>
      <PageHeader title={`${t('listTitle')} ${d.docNo ?? `#${d.deliveryOrderId}`}`} />
      <div className="mb-4 flex flex-wrap items-center gap-3">
        <span data-testid="do-status" className="badge badge-lg badge-ghost">{d.status}</span>
        <span>{d.customerName}</span>
        {d.isCombinedWithTi && (
          <span className="badge badge-info badge-sm">{t('combined')}</span>
        )}
        {d.status === 'Draft' && (
          <button data-testid="do-post" className="btn btn-primary btn-sm"
            disabled={act.isPending} onClick={() => run('post')}>{t('post')}</button>
        )}
        {d.status === 'Posted' && !d.isCombinedWithTi && d.taxInvoiceId == null && (
          <button data-testid="do-create-ti" className="btn btn-secondary btn-sm"
            disabled={act.isPending} onClick={() => run('create-ti')}>{t('createTi')}</button>
        )}
        {d.taxInvoiceId != null && (
          <Link data-testid="do-ti-link" href={`/tax-invoices/${d.taxInvoiceId}`}
            className="link link-primary">{t('linkedTi')} #{d.taxInvoiceId}</Link>
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
      <AttachmentsSection parentType="DELIVERY_ORDER" parentId={doId} />
    </>
  );
}
