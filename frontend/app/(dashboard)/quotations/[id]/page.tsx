'use client';

import { use } from 'react';
import { useRouter } from 'next/navigation';
import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { PageHeader } from '@/components/ui/PageHeader';
import { useQuotation, useQuotationAction } from '@/lib/queries';
import { formatTHB } from '@/lib/utils';
import { AttachmentsSection } from '@/components/attachments/AttachmentsSection';

export default function QuotationDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const qid = Number(id);
  const t = useTranslations('quotation');
  const tc = useTranslations('common');
  const router = useRouter();
  const q = useQuotation(qid);
  const act = useQuotationAction();
  const d = q.data;

  async function run(action: string, body?: unknown) {
    try {
      const res = await act.mutateAsync({ id: qid, action, body });
      toast.success(tc('save'));
      if (action === 'convert-to-so') {
        const so = (res as { sales_order_id: number }).sales_order_id;
        router.push(`/sales-orders/${so}`);
      }
    } catch (e) {
      toast.error((e as { detail?: string })?.detail ?? tc('error'));
    }
  }

  if (!d) return <div className="p-6 text-base-content/50">{tc('loading')}</div>;

  return (
    <>
      <PageHeader title={`${t('listTitle')} ${d.docNo ?? `#${d.quotationId}`}`} />
      <div className="mb-4 flex flex-wrap items-center gap-3">
        <span data-testid="q-status" className="badge badge-lg badge-ghost">{d.status}</span>
        <span>{d.customerName}</span>
        {d.status === 'Draft' && (
          <button data-testid="q-send" className="btn btn-primary btn-sm"
            disabled={act.isPending} onClick={() => run('send')}>{t('send')}</button>
        )}
        {d.status === 'Sent' && (
          <button data-testid="q-accept" className="btn btn-success btn-sm"
            disabled={act.isPending} onClick={() => run('accept')}>{t('accept')}</button>
        )}
        {d.status === 'Accepted' && d.convertedToSoId == null && (
          <button data-testid="q-convert" className="btn btn-secondary btn-sm"
            disabled={act.isPending} onClick={() => run('convert-to-so')}>{t('convertToSo')}</button>
        )}
        {d.convertedToSoId != null && (
          <Link data-testid="q-so-link" href={`/sales-orders/${d.convertedToSoId}`}
            className="link link-primary">{t('viewSo')} #{d.convertedToSoId}</Link>
        )}
      </div>
      <div className="overflow-x-auto rounded-lg border border-base-300">
        <table className="table">
          <thead><tr>
            <th>{t('lineDesc')}</th><th className="text-right">{t('qty')}</th>
            <th className="text-right">{t('unitPrice')}</th>
            <th className="text-right">{t('total')}</th>
          </tr></thead>
          <tbody>
            {d.lines.map((l) => (
              <tr key={l.lineNo}>
                <td>{l.descriptionTh}</td>
                <td className="text-right tabular-nums">{l.quantity}</td>
                <td className="text-right tabular-nums">{formatTHB(l.unitPrice)}</td>
                <td className="text-right tabular-nums">{formatTHB(l.totalAmount)}</td>
              </tr>
            ))}
          </tbody>
          <tfoot><tr className="font-bold">
            <td colSpan={3} className="text-right">{t('total')}</td>
            <td className="text-right tabular-nums">{formatTHB(d.totalAmount)}</td>
          </tr></tfoot>
        </table>
      </div>
      {d.showWhtNote && (
        <p className="mt-3 text-xs text-base-content/60">{t('whtNote')}</p>
      )}
      <AttachmentsSection parentType="QUOTATION" parentId={qid} />
    </>
  );
}
