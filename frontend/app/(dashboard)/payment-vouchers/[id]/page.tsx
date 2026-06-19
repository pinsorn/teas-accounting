'use client';

import { useState, useEffect } from 'react';
import { useParams } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { FilePlus2 } from 'lucide-react';
import { PageHeader } from '@/components/ui/PageHeader';
import { AgentPendingBadge } from '@/components/ui/AgentPendingBadge';
import { PermissionGate, useHasScope } from '@/components/PermissionGate';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { DocumentNumberBadge } from '@/components/ui/DocumentNumberBadge';
import { BusinessUnitBadge } from '@/components/ui/BusinessUnitBadge';
import { CompletenessChips } from '@/components/ui/CompletenessBadge';
import { PrintMenu } from '@/components/ui/PrintMenu';
import { PaperDocument } from '@/components/paper/PaperDocument';
import { PurchaseDocumentChain } from '@/components/doc/PurchaseDocumentChain';
import { ActivityLog } from '@/components/doc/ActivityLog';
import { CreateViFromPvDialog } from '@/components/forms/CreateViFromPvDialog';
import {
  usePaymentVoucher, useApprovePaymentVoucher, usePostPaymentVoucher,
  useCompanyProfile, useVendor,
} from '@/lib/queries';
import { formatDate, formatTaxId } from '@/lib/utils';
import { problemToast } from '@/lib/api';
import { AttachmentsSection } from '@/components/attachments/AttachmentsSection';
import { PAPER_DOC, paperWatermark, companyToSeller } from '@/lib/paper-doc-config';

export default function PaymentVoucherDetailPage() {
  const id = Number(useParams<{ id: string }>().id);
  const t = useTranslations('pv');
  const tc = useTranslations('common');
  const ta = useTranslations('approve');
  const tpt = useTranslations('productType');
  const { data: d, isLoading, isError } = usePaymentVoucher(id);
  const { data: company } = useCompanyProfile();
  const approve = useApprovePaymentVoucher();
  const post = usePostPaymentVoucher();
  const hasScope = useHasScope();
  const [viDialog, setViDialog] = useState(false);
  const [isApproveAction, setIsApproveAction] = useState(false);

  useEffect(() => {
    const action = new URLSearchParams(window.location.search).get('action');
    if (action === 'approve') setIsApproveAction(true);
  }, []);
  // PV→VI guided create is offered when the vendor is VAT-registered and there
  // is no linked VI yet (purchase-completeness D1). Vendor VAT flag comes from
  // the vendor master (advisory).
  const vendor = useVendor(d?.vendorId ?? 0).data;

  if (isLoading) return <p className="text-base-content/50">{tc('loading')}</p>;
  if (isError || !d) return <p className="text-error">{tc('error')}</p>;

  async function doApprove() {
    try { await approve.mutateAsync(id); toast.success(t('approve')); }
    catch (e) { problemToast(e, tc('error')); }
  }
  async function doPost() {
    try { await post.mutateAsync(id); toast.success(t('post')); }
    catch (e) { problemToast(e, tc('error')); }
  }

  // Sprint 13j-PURCH D-supplement — PV renders as a PaperDocument. A PV is
  // buyer-issued: seller block = our company, customer block = the vendor we pay.
  // Foot uses the new optional WHT row + net-paid (whtAmount / totalPaid). The PV
  // is view-only here regardless of status (no editable inputs on the paper —
  // §4.2; posted PV is immutable).
  const cfg = PAPER_DOC['payment-voucher'];
  const seller = companyToSeller(company);
  const hasWht = d.whtAmount > 0;
  // D1 — offer guided VI create when vendor is VAT-registered & no VI linked yet.
  const canCreateVi = !!vendor?.vatRegistered && d.vendorInvoiceId === null;

  return (
    <>
      <PageHeader
        title={t('title')}
        subtitle={d.docNo ?? undefined}
        actions={
          <div className="flex gap-2">
            {d.status === 'Draft' && (
              <PermissionGate scope="purchase.payment_voucher.approve">
                <button data-testid="pv-approve" className="btn btn-secondary btn-sm" disabled={approve.isPending}
                  onClick={doApprove} title={t('sodHint')}>
                  {t('approve')}
                </button>
              </PermissionGate>
            )}
            {d.status === 'Approved' && (
              <PermissionGate scope="purchase.payment_voucher.post">
                <button data-testid="pv-post" className="btn btn-primary btn-sm" disabled={post.isPending}
                  onClick={doPost}>
                  {t('post')}
                </button>
              </PermissionGate>
            )}
            {canCreateVi && (
              <PermissionGate scope="purchase.vendor_invoice.create">
                <button className="btn btn-primary btn-sm gap-1" data-testid="pv-create-vi"
                  onClick={() => setViDialog(true)}>
                  <FilePlus2 className="h-4 w-4" aria-hidden /> {t('createVi.action')}
                </button>
              </PermissionGate>
            )}
            {/* Sprint 13j-PURCH D3 — tracked ต้นฉบับ/สำเนา print (Phase C added
                payment-vouchers /pdf?copy + /mark-printed). */}
            <PrintMenu docType="payment-vouchers" id={id} />
          </div>
        }
      />
      {/* B3 — agent-draft badge on normal navigation (independent of ?action=approve). */}
      {d.status === 'Draft' && d.createdViaApiKey && (
        <div className="mb-4"><AgentPendingBadge /></div>
      )}
      {/* ?action=approve — prominent approval banner for agent-created drafts */}
      {isApproveAction && d.status === 'Draft' && (
        <div className="mb-4 rounded-lg border border-warning bg-warning/10 p-4">
          <p className="font-semibold text-warning-content">{ta('bannerTitle')}</p>
          <p className="mt-1 text-sm text-base-content/80">{ta('bannerDesc')}</p>
          <div className="mt-3">
            {hasScope('purchase.payment_voucher.approve') ? (
              <button
                data-testid="pv-approve-cta"
                className="btn btn-warning btn-sm"
                disabled={approve.isPending}
                onClick={doApprove}
              >
                {ta('ctaApprove')}
              </button>
            ) : (
              <p className="text-sm font-medium text-error">{ta('noPermission')}</p>
            )}
          </div>
        </div>
      )}
      {isApproveAction && d.status !== 'Draft' && (
        <div className="mb-4 rounded-lg border border-base-300 bg-base-200 p-3 text-sm text-base-content/60">
          {ta('alreadyPosted')}
        </div>
      )}

      <div className="mb-4 flex flex-wrap items-center gap-3">
        <DocumentNumberBadge value={d.docNo} />
        <StatusBadge status={d.status} />
        {d.selfWithholdMode && (
          <span className="badge badge-warning">
            {t('selfWithhold.detailBadge')}
            {d.whtPayerMode === 'GROSS_UP_ONCE'
              ? ` · ${t('selfWithhold.mode.onceBadge')}`
              : ` · ${t('selfWithhold.mode.foreverBadge')}`}
          </span>
        )}
        {d.requiresPnd36ReverseCharge && (
          <span className="badge badge-outline">ภ.พ.36</span>
        )}
        <BusinessUnitBadge
          businessUnitId={d.businessUnitId}
          code={d.businessUnitCode}
          name={d.businessUnitName}
        />
        <span className="text-sm text-base-content/60">{formatDate(d.docDate)}</span>
        {d.status === 'Draft' && (
          <span className="text-xs text-base-content/60">{t('postHint')} · {t('sodHint')}</span>
        )}
      </div>

      {/* purchase-completeness — advisory (non-blocking) flag, POSTED PVs only. */}
      {d.status === 'Posted' && d.completeness && !d.completeness.isComplete && (
        <div className="mb-4">
          <CompletenessChips missing={d.completeness.missing} />
        </div>
      )}

      <div className="detail-grid">
        <div className="paper-wrap">
          <PaperDocument
            docType={cfg.docType}
            docTypeEn={cfg.docTypeEn}
            docNo={d.docNo ?? `#${d.paymentVoucherId}`}
            issueDate={d.docDate}
            seller={seller}
            partyLabel={{ th: 'ผู้ขาย', en: 'Vendor' }}
            customer={{
              name: d.vendorName,
              taxId: d.vendorTaxId ? formatTaxId(d.vendorTaxId) : null,
              branchCode: d.vendorBranchCode ?? null,
              address: d.vendorAddress ?? null,
            }}
            items={d.lines.map((l) => ({
              description: l.description,
              amount: l.amount,
            }))}
            summary={{
              subtotal: d.subtotalAmount,
              beforeVat: d.subtotalAmount,
              vat: d.vatAmount,
              // Pre-WHT gross; PaperFoot derives net-paid = total − wht = totalPaid.
              total: d.subtotalAmount + d.vatAmount,
              wht: hasWht ? d.whtAmount : null,
            }}
            notes={d.notes}
            signRoles={cfg.signRoles}
            watermark={paperWatermark('payment-voucher', d.status)}
            extraMetaBlock={
              <div className="text-[12px] leading-relaxed text-ink-700">
                <div><b>{t('method')}:</b> {d.paymentMethod}
                  {d.chequeNo ? ` (${d.chequeNo}${d.chequeDate ? ` / ${formatDate(d.chequeDate)}` : ''})` : ''}</div>
                <div><b>{t('category')}:</b> <span className="font-mono">{d.subPrefix}</span></div>
              </div>
            }
          />
        </div>
        <div className="detail-side space-y-4">
          <PurchaseDocumentChain type="payment-voucher" id={id} />
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
          {/* BP-09 — activity history rail (parity with Sales detail pages). */}
          <ActivityLog docType="payment-vouchers" id={id} />
        </div>
      </div>

      <AttachmentsSection parentType="PAYMENT_VOUCHER" parentId={id} />

      <CreateViFromPvDialog
        pvId={id}
        open={viDialog}
        defaultDate={d.docDate}
        onClose={() => setViDialog(false)}
      />
    </>
  );
}
