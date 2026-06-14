'use client';

import { useState } from 'react';
import Link from 'next/link';
import { useParams, useRouter } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { PageHeader } from '@/components/ui/PageHeader';
import { PermissionGate } from '@/components/PermissionGate';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { DocumentNumberBadge } from '@/components/ui/DocumentNumberBadge';
import { BusinessUnitBadge } from '@/components/ui/BusinessUnitBadge';
import { CompletenessChips } from '@/components/ui/CompletenessBadge';
import { PostConfirmDialog } from '@/components/ui/PostConfirmDialog';
import { useVendorInvoice, usePostVendorInvoice, useCompanyProfile, useAttachments } from '@/lib/queries';
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
  const tpt = useTranslations('productType');
  const { data: d, isLoading, isError } = useVendorInvoice(id);
  const { data: company } = useCompanyProfile();
  // C — VendorInvoiceService.PostAsync now requires ≥1 non-deleted attachment under
  // (VendorInvoice, id) before flipping Draft → Posted (ม.86/4 + ม.82/4 audit
  // evidence). Reflect that gate in the UI so Post is disabled (with a banner
  // explaining why) until the vendor's ใบกำกับภาษีซื้อ file has been attached.
  const { data: attachments } = useAttachments('VENDOR_INVOICE', id);
  const post = usePostVendorInvoice();
  const [confirm, setConfirm] = useState(false);

  if (isLoading) return <p className="text-base-content/50">{tc('loading')}</p>;
  if (isError || !d) return <p className="text-error">{tc('error')}</p>;

  const isDraft = d.status === 'Draft';
  const canSettle = d.status === 'Posted' && d.settlementStatus !== 'PAID';
  const hasAttachment = (attachments?.items?.length ?? 0) > 0;
  // cont.77 — Post no longer blocks on a missing vendor-TI file; posting without it is
  // allowed and the doc is flagged "incomplete" (advisory). Show a heads-up on a draft.
  const missingFileWarn = isDraft && !hasAttachment;

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
              <PermissionGate scope="purchase.vendor_invoice.post">
                <button className="btn btn-primary btn-sm"
                  disabled={post.isPending}
                  title={missingFileWarn ? t('attachmentAdvisoryHint') : undefined}
                  onClick={() => setConfirm(true)}>
                  {t('post')}
                </button>
              </PermissionGate>
            )}
            {canSettle && (
              <PermissionGate scope="purchase.payment_voucher.create">
                <button className="btn btn-secondary btn-sm"
                  onClick={() => router.push(`/payment-vouchers/new?fromVendorInvoiceId=${id}`)}>
                  {t('settleWithPv')}
                </button>
              </PermissionGate>
            )}
          </div>
        }
      />
      <div className="mb-4 flex items-center gap-3">
        <DocumentNumberBadge value={d.docNo} />
        <StatusBadge status={d.status} />
        {/* ITEM 6 — payments are full-amount, so the settlement-status badge is
            noise on the detail. Keep `settlementStatus` in types/queries (AP-aging
            still uses it) but only surface it once the doc is settled (PAID). */}
        {d.settlementStatus === 'PAID' && <StatusBadge status={d.settlementStatus} />}
        {d.purchaseOrderId && (
          <Link href={`/purchase-orders/${d.purchaseOrderId}`}
            data-testid="vi-linked-po"
            className="badge badge-outline badge-info gap-1 font-mono text-xs">
            {t('linkedPo')}: {d.purchaseOrderDocNo ?? `#${d.purchaseOrderId}`}
          </Link>
        )}
        <BusinessUnitBadge
          businessUnitId={d.businessUnitId}
          code={d.businessUnitCode}
          name={d.businessUnitName}
        />
        <span className="text-sm text-base-content/60">{formatDate(d.docDate)}</span>
      </div>

      {/* purchase-completeness — advisory (non-blocking) flag, POSTED VIs only. */}
      {d.status === 'Posted' && d.completeness && !d.completeness.isComplete && (
        <div className="mb-4">
          <CompletenessChips missing={d.completeness.missing} />
        </div>
      )}

      {missingFileWarn && (
        <div role="alert"
          data-testid="vi-attachment-advisory"
          className="mb-4 flex items-start gap-3 rounded-card border border-warning/30 bg-warning/10 p-3 text-sm text-warning-content">
          <span aria-hidden className="text-warning">⚠️</span>
          <div className="flex-1">
            <div className="font-medium">{t('attachmentAdvisory')}</div>
            <div className="text-xs text-base-content/70">{t('attachmentAdvisoryHint')}</div>
          </div>
        </div>
      )}

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
          {/* purchase-completeness — สินค้า/บริการ per line (read-only snapshot). */}
          {d.lines.some((l) => l.productType) && (
            <div className="rounded-card border border-ink-100 bg-base-100 p-4 shadow-warm-sm">
              <div className="mb-2 text-sm font-semibold">{tpt('label')}</div>
              <ul className="space-y-1 text-xs">
                {d.lines.map((l) => (
                  <li key={l.lineNo} className="flex items-center justify-between gap-2">
                    <span className="truncate text-base-content/70">{l.description}</span>
                    {l.productType && (
                      <span className="badge badge-outline badge-sm shrink-0">{tpt(l.productType)}</span>
                    )}
                  </li>
                ))}
              </ul>
            </div>
          )}
          {/* ITEM 6 — the "ชำระแล้ว x / total" settled-amount line is hidden here:
              vendor payments are full-amount, so the settlement progress is noise on
              the VI detail. `settledAmount`/`settlementStatus` stay in the types and
              query (the AP-aging report still consumes them) — this is a UI-only hide. */}
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
