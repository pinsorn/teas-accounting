'use client';

import { useParams } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { Download } from 'lucide-react';
import { PageHeader } from '@/components/ui/PageHeader';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { DocumentNumberBadge } from '@/components/ui/DocumentNumberBadge';
import {
  usePaymentVoucher, useApprovePaymentVoucher, usePostPaymentVoucher,
} from '@/lib/queries';
import { downloadFile } from '@/lib/api';
import { formatTHB, formatDate, formatTaxId } from '@/lib/utils';
import { AttachmentsSection } from '@/components/attachments/AttachmentsSection';

export default function PaymentVoucherDetailPage() {
  const id = Number(useParams<{ id: string }>().id);
  const t = useTranslations('pv');
  const tc = useTranslations('common');
  const { data: d, isLoading, isError } = usePaymentVoucher(id);
  const approve = useApprovePaymentVoucher();
  const post = usePostPaymentVoucher();

  if (isLoading) return <p className="text-base-content/50">{tc('loading')}</p>;
  if (isError || !d) return <p className="text-error">{tc('error')}</p>;

  async function doApprove() {
    try { await approve.mutateAsync(id); toast.success(t('approve')); }
    catch (e) { toast.error((e as { detail?: string })?.detail ?? tc('error')); }
  }
  async function doPost() {
    try { await post.mutateAsync(id); toast.success(t('post')); }
    catch (e) { toast.error((e as { detail?: string })?.detail ?? tc('error')); }
  }

  return (
    <>
      <PageHeader
        title={t('title')}
        subtitle={d.docNo ?? undefined}
        actions={
          <div className="flex gap-2">
            {d.status === 'Draft' && (
              <button className="btn btn-secondary btn-sm" disabled={approve.isPending}
                onClick={doApprove} title={t('sodHint')}>
                {t('approve')}
              </button>
            )}
            {d.status === 'Approved' && (
              <button className="btn btn-primary btn-sm" disabled={post.isPending}
                onClick={doPost}>
                {t('post')}
              </button>
            )}
            <button className="btn btn-ghost btn-sm gap-1"
              onClick={() => downloadFile(`payment-vouchers/${id}/pdf`, `payment-voucher-${id}.pdf`)}>
              <Download className="h-4 w-4" aria-hidden /> PDF
            </button>
          </div>
        }
      />
      <div className="mb-4 flex items-center gap-3">
        <DocumentNumberBadge value={d.docNo} />
        <StatusBadge status={d.status} />
        {d.selfWithholdMode && (
          <span className="badge badge-warning">{t('selfWithhold.detailBadge')}</span>
        )}
        {d.requiresPnd36ReverseCharge && (
          <span className="badge badge-outline">ภ.พ.36</span>
        )}
        <span className="text-sm text-base-content/60">{formatDate(d.docDate)}</span>
      </div>
      <div className="card bg-base-100 shadow-sm">
        <div className="card-body">
          <p><b>{t('vendor')}:</b> {d.vendorName}{' '}
            <span className="font-mono text-sm opacity-70">{formatTaxId(d.vendorTaxId)}</span></p>
          <p><b>{t('method')}:</b> {d.paymentMethod}
            {d.chequeNo ? ` (${d.chequeNo}${d.chequeDate ? ` / ${formatDate(d.chequeDate)}` : ''})` : ''}</p>
          <p><b>{t('category')}:</b> <span className="font-mono">{d.subPrefix}</span></p>
          {d.vendorInvoiceId && (
            <p><b>{t('settlingVi')}:</b>{' '}
              <a className="link link-primary" href={`/vendor-invoices/${d.vendorInvoiceId}`}>
                VI #{d.vendorInvoiceId}
              </a></p>
          )}
          {d.approvedBy != null && (
            <p className="text-sm text-success">
              {t('approvedBy')} #{d.approvedBy}
              {d.approvedAt ? ` ${t('at')} ${formatDate(d.approvedAt)}` : ''}
            </p>
          )}
          {d.status === 'Draft' && (
            <p className="text-xs text-base-content/60">{t('postHint')} · {t('sodHint')}</p>
          )}

          <h3 className="mt-2 font-semibold">{t('lines')}</h3>
          <table className="table table-sm">
            <thead>
              <tr>
                <th>#</th><th>Description</th>
                <th className="text-right">Amount</th>
                <th className="text-right">VAT</th>
                <th className="text-right">WHT</th>
              </tr>
            </thead>
            <tbody>
              {d.lines.map((l) => (
                <tr key={l.lineNo}>
                  <td>{l.lineNo}</td>
                  <td>
                    {l.description}
                    {!l.isRecoverableVat && l.vatAmount > 0 && (
                      <span className="ml-2 badge badge-warning badge-xs">{t('nonRecoverableVat')}</span>
                    )}
                  </td>
                  <td className="text-right tabular-nums">{formatTHB(l.amount)}</td>
                  <td className="text-right tabular-nums">{formatTHB(l.vatAmount)}</td>
                  <td className="text-right tabular-nums">{formatTHB(l.whtAmount)}</td>
                </tr>
              ))}
            </tbody>
          </table>

          <div className="mt-3 ml-auto w-64 space-y-1 text-sm">
            <div className="flex justify-between"><span>{t('subtotal')}</span>
              <span className="tabular-nums">{formatTHB(d.subtotalAmount)}</span></div>
            <div className="flex justify-between"><span>{t('vat')}</span>
              <span className="tabular-nums">{formatTHB(d.vatAmount)}</span></div>
            <div className="flex justify-between"><span>{t('wht')}</span>
              <span className="tabular-nums">−{formatTHB(d.whtAmount)}</span></div>
            <div className="flex justify-between border-t pt-1 text-lg font-bold">
              <span>{t('netPaid')}</span>
              <span className="tabular-nums">{formatTHB(d.totalPaid)} {d.currencyCode}</span></div>
          </div>
        </div>
      </div>
      <AttachmentsSection parentType="PAYMENT_VOUCHER" parentId={id} />
    </>
  );
}
