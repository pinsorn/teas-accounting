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
import { useVendorInvoice, usePostVendorInvoice, useCompanyProfile } from '@/lib/queries';
import { formatTHB, formatDate } from '@/lib/utils';
import { problemToast } from '@/lib/api';
import { AttachmentsSection } from '@/components/attachments/AttachmentsSection';
import { PurchaseDocumentChain } from '@/components/doc/PurchaseDocumentChain';
import { ActivityLog } from '@/components/doc/ActivityLog';
import { PaperDocument } from '@/components/paper/PaperDocument';
import { PAPER_DOC, paperWatermark, companyToCustomer } from '@/lib/paper-doc-config';

export default function VendorInvoiceDetailPage() {
  const id = Number(useParams<{ id: string }>().id);
  const router = useRouter();
  const t = useTranslations('vi');
  const tc = useTranslations('common');
  const { data: d, isLoading, isError } = useVendorInvoice(id);
  const { data: company } = useCompanyProfile();
  const post = usePostVendorInvoice();
  const [confirm, setConfirm] = useState(false);

  if (isLoading) return <p className="text-base-content/50">{tc('loading')}</p>;
  if (isError || !d) return <p className="text-error">{tc('error')}</p>;

  const isDraft = d.status === 'Draft';
  const canSettle = d.status === 'Posted' && d.settlementStatus !== 'PAID';
  const pct = d.totalAmount > 0
    ? Math.min(100, Math.round((d.settledAmount / d.totalAmount) * 100)) : 0;

  // Sprint 13j-PURCH Flag-1 (BP-09) — on-screen READ-ONLY PaperDocument. The
  // VENDOR issued this tax invoice, so the seller block = the vendor and the
  // customer block = our company. No PrintMenu (no /pdf endpoint — §4.6); the
  // paper carries no editable inputs (view-only regardless of status — §4.2).
  const cfg = PAPER_DOC['vendor-invoice'];

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

      <div className="detail-grid">
        <div className="paper-wrap">
          <PaperDocument
            docType={cfg.docType}
            docTypeEn={cfg.docTypeEn}
            docNo={d.docNo ?? `#${d.vendorInvoiceId}`}
            issueDate={d.docDate}
            seller={{
              name: d.vendorName,
              taxId: d.vendorTaxId ?? '',
              branchCode: d.vendorBranchCode ?? '00000',
              address: d.vendorAddress ?? '',
            }}
            customer={companyToCustomer(company)}
            items={d.lines.map((l) => ({
              description: l.description,
              amount: l.amount,
            }))}
            summary={{
              subtotal: d.subtotalAmount,
              beforeVat: d.subtotalAmount,
              vat: d.vatAmount,
              total: d.totalAmount,
            }}
            notes={d.notes}
            signRoles={cfg.signRoles}
            watermark={paperWatermark('vendor-invoice', d.status)}
            extraMetaBlock={
              <div className="text-[12px] leading-relaxed text-ink-700">
                <div><b>{t('vendorTiNo')}:</b>{' '}
                  <span className="font-mono">{d.vendorTaxInvoiceNo}</span></div>
                <div><b>{t('vendorTiDate')}:</b> {formatDate(d.vendorTaxInvoiceDate)}</div>
                <div><b>{t('claimPeriod')}:</b>{' '}
                  <span className="tabular-nums">{d.vatClaimPeriod}</span></div>
                {d.nonRecoverableVatAmount > 0 && (
                  <div><b>{t('nonRecVat')}:</b>{' '}
                    <span className="tabular-nums">{formatTHB(d.nonRecoverableVatAmount)}</span></div>
                )}
              </div>
            }
          />
        </div>
        <div className="detail-side space-y-4">
          <PurchaseDocumentChain type="vendor-invoice" id={id} />
          {d.status === 'Posted' && (
            <div className="rounded-card border border-ink-100 bg-base-100 p-4 shadow-warm-sm">
              <div className="mb-1 flex justify-between text-sm">
                <span>{t('settled')}: {formatTHB(d.settledAmount)} / {formatTHB(d.totalAmount)}</span>
                <span>{pct}%</span>
              </div>
              <progress className="progress progress-info w-full" value={pct} max={100} />
            </div>
          )}
          {/* BP-09 — activity history rail (parity with Sales detail pages). */}
          <ActivityLog docType="vendor-invoices" id={id} />
        </div>
      </div>

      <PostConfirmDialog
        docType="vendor_invoice"
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
          } catch (e) {
            problemToast(e, tc('error'));
          } finally {
            setConfirm(false);
          }
        }}
      />
      <AttachmentsSection parentType="VENDOR_INVOICE" parentId={id} />
    </>
  );
}
