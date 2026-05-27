'use client';

import { useParams } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { PageHeader } from '@/components/ui/PageHeader';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { DocumentNumberBadge } from '@/components/ui/DocumentNumberBadge';
import { PrintMenu } from '@/components/ui/PrintMenu';
import { PaperDocument } from '@/components/paper/PaperDocument';
import { PurchaseDocumentChain } from '@/components/doc/PurchaseDocumentChain';
import {
  usePaymentVoucher, useApprovePaymentVoucher, usePostPaymentVoucher,
  useCompanyProfile,
} from '@/lib/queries';
import { formatDate, formatTaxId } from '@/lib/utils';
import { problemToast } from '@/lib/api';
import { AttachmentsSection } from '@/components/attachments/AttachmentsSection';
import { PAPER_DOC, paperWatermark, companyToSeller } from '@/lib/paper-doc-config';

export default function PaymentVoucherDetailPage() {
  const id = Number(useParams<{ id: string }>().id);
  const t = useTranslations('pv');
  const tc = useTranslations('common');
  const { data: d, isLoading, isError } = usePaymentVoucher(id);
  const { data: company } = useCompanyProfile();
  const approve = useApprovePaymentVoucher();
  const post = usePostPaymentVoucher();

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
            {/* Sprint 13j-PURCH D3 — tracked ต้นฉบับ/สำเนา print (Phase C added
                payment-vouchers /pdf?copy + /mark-printed). */}
            <PrintMenu docType="payment-vouchers" id={id} />
          </div>
        }
      />
      <div className="mb-4 flex flex-wrap items-center gap-3">
        <DocumentNumberBadge value={d.docNo} />
        <StatusBadge status={d.status} />
        {d.selfWithholdMode && (
          <span className="badge badge-warning">{t('selfWithhold.detailBadge')}</span>
        )}
        {d.requiresPnd36ReverseCharge && (
          <span className="badge badge-outline">ภ.พ.36</span>
        )}
        <span className="text-sm text-base-content/60">{formatDate(d.docDate)}</span>
        {d.status === 'Draft' && (
          <span className="text-xs text-base-content/60">{t('postHint')} · {t('sodHint')}</span>
        )}
      </div>

      <div className="detail-grid">
        <div className="paper-wrap">
          <PaperDocument
            docType={cfg.docType}
            docTypeEn={cfg.docTypeEn}
            docNo={d.docNo ?? `#${d.paymentVoucherId}`}
            issueDate={d.docDate}
            seller={seller}
            partyLabel={{ th: 'ผู้รับเงิน', en: 'Payee' }}
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
        <div className="detail-side">
          <PurchaseDocumentChain type="payment-voucher" id={id} />
        </div>
      </div>

      <AttachmentsSection parentType="PAYMENT_VOUCHER" parentId={id} />
    </>
  );
}
