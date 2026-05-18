'use client';

import { useState } from 'react';
import Link from 'next/link';
import { useParams, useRouter } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { PageHeader } from '@/components/ui/PageHeader';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { DocumentNumberBadge } from '@/components/ui/DocumentNumberBadge';
import { PostConfirmDialog } from '@/components/ui/PostConfirmDialog';
import { useVendorInvoice, usePostVendorInvoice } from '@/lib/queries';
import { formatTHB, formatDate, formatTaxId } from '@/lib/utils';
import { AttachmentsSection } from '@/components/attachments/AttachmentsSection';

export default function VendorInvoiceDetailPage() {
  const id = Number(useParams<{ id: string }>().id);
  const router = useRouter();
  const t = useTranslations('vi');
  const tc = useTranslations('common');
  const { data: d, isLoading, isError } = useVendorInvoice(id);
  const post = usePostVendorInvoice();
  const [confirm, setConfirm] = useState(false);

  if (isLoading) return <p className="text-base-content/50">{tc('loading')}</p>;
  if (isError || !d) return <p className="text-error">{tc('error')}</p>;

  const isDraft = d.status === 'Draft';
  const canSettle = d.status === 'Posted' && d.settlementStatus !== 'PAID';
  const pct = d.totalAmount > 0
    ? Math.min(100, Math.round((d.settledAmount / d.totalAmount) * 100)) : 0;

  return (
    <>
      <PageHeader
        title={t('title')}
        subtitle={d.docNo ?? undefined}
        actions={
          <div className="flex gap-2">
            {isDraft && (
              <button className="btn btn-primary btn-sm" disabled={post.isPending}
                onClick={() => setConfirm(true)}>
                {t('post')}
              </button>
            )}
            {canSettle && (
              <button className="btn btn-secondary btn-sm"
                onClick={() => router.push(`/payment-vouchers/new?fromVendorInvoiceId=${id}`)}>
                {t('settleWithPv')}
              </button>
            )}
          </div>
        }
      />
      <div className="mb-4 flex items-center gap-3">
        <DocumentNumberBadge value={d.docNo} />
        <StatusBadge status={d.status} />
        <StatusBadge status={d.settlementStatus} />
        {d.purchaseOrderId && (
          <Link href={`/purchase-orders/${d.purchaseOrderId}`}
            data-testid="vi-linked-po"
            className="badge badge-outline badge-info gap-1 font-mono text-xs">
            {t('linkedPo')}: {d.purchaseOrderDocNo ?? `#${d.purchaseOrderId}`}
          </Link>
        )}
        <span className="text-sm text-base-content/60">{formatDate(d.docDate)}</span>
      </div>

      <div className="card bg-base-100 shadow-sm">
        <div className="card-body">
          <div className="grid grid-cols-1 gap-x-8 gap-y-1 sm:grid-cols-2">
            <p><b>{t('vendor')}:</b> {d.vendorName}{' '}
              <span className="font-mono text-sm opacity-70">{formatTaxId(d.vendorTaxId)}</span></p>
            <p><b>{t('vendorTiNo')}:</b> <span className="font-mono">{d.vendorTaxInvoiceNo}</span></p>
            <p><b>{t('vendorTiDate')}:</b> {formatDate(d.vendorTaxInvoiceDate)}</p>
            <p><b>{t('claimPeriod')}:</b> <span className="tabular-nums">{d.vatClaimPeriod}</span></p>
          </div>

          <h3 className="mt-3 font-semibold">{t('lines')}</h3>
          <table className="table table-sm">
            <thead>
              <tr>
                <th>#</th><th>{t('description')}</th>
                <th className="text-right">{t('amount')}</th>
                <th className="text-right">VAT</th>
              </tr>
            </thead>
            <tbody>
              {d.lines.map((l) => (
                <tr key={l.lineNo}>
                  <td>{l.lineNo}</td>
                  <td>
                    {l.description}
                    {!l.isRecoverableVat && (
                      <span className="ml-2 badge badge-warning badge-xs">{t('nonRecVat')}</span>
                    )}
                  </td>
                  <td className="text-right tabular-nums">{formatTHB(l.amount)}</td>
                  <td className="text-right tabular-nums">{formatTHB(l.vatAmount)}</td>
                </tr>
              ))}
            </tbody>
          </table>

          <div className="mt-3 ml-auto w-72 space-y-1 text-sm">
            <div className="flex justify-between"><span>{t('subtotal')}</span>
              <span className="tabular-nums">{formatTHB(d.subtotalAmount)}</span></div>
            <div className="flex justify-between"><span>{t('vat')}</span>
              <span className="tabular-nums">{formatTHB(d.vatAmount)}</span></div>
            <div className="flex justify-between"><span>{t('nonRecVat')}</span>
              <span className="tabular-nums">{formatTHB(d.nonRecoverableVatAmount)}</span></div>
            <div className="flex justify-between border-t pt-1 text-lg font-bold">
              <span>{t('total')}</span>
              <span className="tabular-nums">{formatTHB(d.totalAmount)} {d.currencyCode}</span></div>
          </div>

          {d.status === 'Posted' && (
            <div className="mt-4">
              <div className="mb-1 flex justify-between text-sm">
                <span>{t('settled')}: {formatTHB(d.settledAmount)} / {formatTHB(d.totalAmount)}</span>
                <span>{pct}%</span>
              </div>
              <progress className="progress progress-info w-full" value={pct} max={100} />
            </div>
          )}
        </div>
      </div>

      <PostConfirmDialog
        open={confirm}
        busy={post.isPending}
        summary={{ customer: d.vendorName, total: d.totalAmount, vat: d.vatAmount }}
        recipients={[]}
        onClose={() => setConfirm(false)}
        onConfirm={async () => {
          try {
            const r = await post.mutateAsync(id);
            toast.success(tc('save'));
            if (r?.poOverReceiptWarning)
              toast.warning(t('poOverReceipt'), { description: r.poOverReceiptWarning });
          } catch {
            toast.error(tc('error'));
          } finally {
            setConfirm(false);
          }
        }}
      />
      <AttachmentsSection parentType="VENDOR_INVOICE" parentId={id} />
    </>
  );
}
