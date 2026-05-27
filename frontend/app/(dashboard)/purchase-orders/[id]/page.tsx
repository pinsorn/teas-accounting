'use client';

import { use } from 'react';
import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { PageHeader } from '@/components/ui/PageHeader';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { PrintMenu } from '@/components/ui/PrintMenu';
import { PaperDocument } from '@/components/paper/PaperDocument';
import { PurchaseDocumentChain } from '@/components/doc/PurchaseDocumentChain';
import {
  usePurchaseOrder, usePurchaseOrderAction, useVendor, useCompanyProfile,
} from '@/lib/queries';
import { AttachmentsSection } from '@/components/attachments/AttachmentsSection';
import { formatTHB, formatDate, formatTaxId } from '@/lib/utils';
import { PAPER_DOC, paperWatermark, companyToSeller } from '@/lib/paper-doc-config';

export default function PurchaseOrderDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const poId = Number(id);
  const t = useTranslations('purchaseOrder');
  const tc = useTranslations('common');
  const q = usePurchaseOrder(poId);
  const act = usePurchaseOrderAction();
  const d = q.data;
  const { data: vendor } = useVendor(d?.vendorId ?? 0);
  const { data: company } = useCompanyProfile();

  async function run(action: string, body?: unknown) {
    try { await act.mutateAsync({ id: poId, action, body }); toast.success(tc('save')); }
    catch (e) { toast.error((e as { detail?: string })?.detail ?? tc('error')); }
  }

  if (!d) return <div className="p-6 text-base-content/50">{tc('loading')}</div>;

  // Sprint 13j-PURCH D4 — a PO leaves the editable phase once it is no longer
  // Draft/Approved (Sent/Closed/Cancelled): the page is view-only, no action buttons.
  const cfg = PAPER_DOC['purchase-order'];
  // A PO is buyer-issued: seller block = our company, customer block = the vendor.
  const seller = companyToSeller(company);

  return (
    <>
      <PageHeader
        title={`${t('title')} ${d.docNo ?? `#${d.purchaseOrderId}`}`}
        actions={<PrintMenu docType="purchase-orders" id={poId} />}
      />

      <div className="mb-4 flex flex-wrap items-center gap-3">
        <span data-testid="po-status"><StatusBadge status={d.status} /></span>
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
        {d.sentToVendorAt && (
          <span className="text-xs text-base-content/60">
            {t('sentToVendor')}: {formatDate(d.sentToVendorAt)}</span>
        )}
      </div>

      <div className="detail-grid">
        <div className="paper-wrap">
          <PaperDocument
            docType={cfg.docType}
            docTypeEn={cfg.docTypeEn}
            docNo={d.docNo ?? `#${d.purchaseOrderId}`}
            issueDate={d.docDate}
            validUntil={d.expectedDeliveryDate ?? undefined}
            validUntilLabel={cfg.validUntilLabel}
            seller={seller}
            customer={{
              name: d.vendorName,
              taxId: vendor?.taxId ? formatTaxId(vendor.taxId) : null,
              branchCode: vendor?.branchCode ?? null,
              address: vendor?.address ?? null,
              contact: vendor?.contactPerson ?? null,
              phone: vendor?.phone ?? null,
            }}
            items={d.lines.map((l) => ({
              description: l.descriptionTh,
              quantity: l.quantity,
              unit: l.uomText,
              unitPrice: l.unitPrice,
              discountPercent: undefined,
              amount: l.lineAmount,
            }))}
            summary={{
              subtotal: d.subtotalAmount,
              beforeVat: d.subtotalAmount,
              vat: d.vatAmount,
              total: d.totalAmount,
            }}
            notes={d.notes}
            signRoles={cfg.signRoles}
            watermark={paperWatermark('purchase-order', d.status)}
          />
        </div>
        <div className="detail-side">
          {/* Sprint 13j-PURCH D-supplement — FE chain panel (PO → VI → PV → WHT),
              resolved from cross-refs on the detail DTOs. The linked-VI summary
              below stays for the per-VI amounts + remaining balance. */}
          <PurchaseDocumentChain type="purchase-order" id={poId} />
          {d.linkedVis.length > 0 && (
            <div className="rounded-card border border-ink-100 bg-base-100 p-4 shadow-warm-sm">
              <h2 className="mb-2 font-semibold text-ink-900">{t('linkedTo')}</h2>
              <div className="text-sm">
                {d.linkedVis.map((v) => (
                  <div key={v.vendorInvoiceId} className="flex justify-between py-0.5">
                    <Link className="link link-primary font-mono"
                      href={`/vendor-invoices/${v.vendorInvoiceId}`}>
                      {v.docNo ?? `#${v.vendorInvoiceId}`}</Link>
                    <span className="tabular-nums">{formatTHB(v.totalAmount)}</span>
                  </div>
                ))}
                <div className="mt-2 flex justify-between border-t border-ink-100 pt-1 font-bold">
                  <span>{t('remaining')}</span>
                  <span className="tabular-nums">{formatTHB(d.remaining)}</span>
                </div>
              </div>
            </div>
          )}
        </div>
      </div>

      <AttachmentsSection parentType="PURCHASE_ORDER" parentId={poId} />
    </>
  );
}
