'use client';

import { use, useEffect, useState } from 'react';
import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { PageHeader } from '@/components/ui/PageHeader';
import { PermissionGate } from '@/components/PermissionGate';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { PrintMenu } from '@/components/ui/PrintMenu';
import { PaperDocument } from '@/components/paper/PaperDocument';
import { PurchaseDocumentChain } from '@/components/doc/PurchaseDocumentChain';
import { ActivityLog } from '@/components/doc/ActivityLog';
import { BusinessUnitBadge } from '@/components/ui/BusinessUnitBadge';
import {
  usePurchaseOrder, usePurchaseOrderAction, useVendor, useCompanyProfile,
} from '@/lib/queries';
import { AttachmentsSection } from '@/components/attachments/AttachmentsSection';
import { formatTHB, formatDate, formatTaxId } from '@/lib/utils';
import { problemToast } from '@/lib/api';
import { PAPER_DOC, paperWatermark, companyToSeller } from '@/lib/paper-doc-config';
import { useHasScope } from '@/components/PermissionGate';

export default function PurchaseOrderDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const poId = Number(id);
  const t = useTranslations('purchaseOrder');
  const tc = useTranslations('common');
  const ta = useTranslations('approve');
  const q = usePurchaseOrder(poId);
  const act = usePurchaseOrderAction();
  const d = q.data;
  const { data: vendor } = useVendor(d?.vendorId ?? 0);
  const { data: company } = useCompanyProfile();
  const hasScope = useHasScope();
  const [isApproveAction, setIsApproveAction] = useState(false);

  useEffect(() => {
    const action = new URLSearchParams(window.location.search).get('action');
    if (action === 'approve') setIsApproveAction(true);
  }, []);

  async function run(action: string, body?: unknown) {
    try { await act.mutateAsync({ id: poId, action, body }); toast.success(tc('save')); }
    catch (e) { problemToast(e, tc('error')); }
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

      {/* ?action=approve — prominent approval banner for agent-created drafts */}
      {isApproveAction && d.status === 'Draft' && (
        <div className="mb-4 rounded-lg border border-warning bg-warning/10 p-4">
          <p className="font-semibold text-warning-content">{ta('bannerTitle')}</p>
          <p className="mt-1 text-sm text-base-content/80">{ta('bannerDesc')}</p>
          <div className="mt-3">
            {hasScope('purchase.purchase_order.approve') ? (
              <button
                data-testid="po-approve-cta"
                className="btn btn-warning btn-sm"
                disabled={act.isPending}
                onClick={() => run('approve')}
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
        <span data-testid="po-status"><StatusBadge status={d.status} /></span>
        <span>{d.vendorName}</span>
        <BusinessUnitBadge
          businessUnitId={d.businessUnitId}
          code={d.businessUnitCode}
          name={d.businessUnitName}
        />
        {d.status === 'Draft' && (
          <PermissionGate scope="purchase.purchase_order.approve">
            <button data-testid="po-approve" className="btn btn-success btn-sm"
              disabled={act.isPending} onClick={() => run('approve')}>{t('approve')}</button>
          </PermissionGate>
        )}
        {d.status === 'Approved' && (
          <>
            {/* ITEM 9 — convenience hand-off to the PV create form, pre-filled from
                this PO (mirrors the VI→PV fromVendorInvoiceId pattern). No backend
                PO→PV link; pure client-side pre-fill. */}
            <PermissionGate scope="purchase.payment_voucher.create">
              <Link href={`/payment-vouchers/new?fromPurchaseOrderId=${poId}`}
                data-testid="po-create-pv" className="btn btn-primary btn-sm">
                {t('createPv')}
              </Link>
            </PermissionGate>
            <PermissionGate scope="purchase.purchase_order.create">
              <button data-testid="po-mark-sent" className="btn btn-outline btn-sm"
                disabled={act.isPending} onClick={() => run('mark-sent')}>{t('sentToVendor')}</button>
            </PermissionGate>
            <PermissionGate scope="purchase.purchase_order.cancel">
              <button data-testid="po-close" className="btn btn-secondary btn-sm"
                disabled={act.isPending} onClick={() => run('close')}>{t('close')}</button>
            </PermissionGate>
          </>
        )}
        {(d.status === 'Draft' || d.status === 'Approved') && (
          <PermissionGate scope="purchase.purchase_order.cancel">
            <button data-testid="po-cancel" className="btn btn-ghost btn-sm text-error"
              disabled={act.isPending}
              onClick={() => {
                const reason = window.prompt(t('cancel') + '?');
                if (reason) run('cancel', { reason });
              }}>{t('cancel')}</button>
          </PermissionGate>
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
            partyLabel={{ th: 'ผู้ขาย', en: 'Vendor' }}
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
            summary={(() => {
              // BP-04 — the PO read DTO exposes no per-line discount field, but it
              // DOES expose unitPrice + quantity. Reconstruct the TRUE pre-discount
              // gross = Σ(unitPrice × quantity); discount = gross − subtotal (the
              // after-discount figure the BE already summed). When there is no
              // discount (gross == subtotal) the discount row is omitted, so the
              // foot is byte-identical to a no-discount PO and to the Sales pattern.
              const gross = d.lines.reduce((s, l) => s + l.unitPrice * (l.quantity ?? 0), 0);
              const disc = Math.round((gross - d.subtotalAmount) * 100) / 100;
              return {
                subtotal: gross,
                discount: disc > 0 ? disc : null,
                beforeVat: d.subtotalAmount,
                vat: d.vatAmount,
                total: d.totalAmount,
              };
            })()}
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
          {/* BP-09 — activity history rail (parity with Sales detail pages). */}
          <ActivityLog docType="purchase-orders" id={poId} />
        </div>
      </div>

      <AttachmentsSection parentType="PURCHASE_ORDER" parentId={poId} />
    </>
  );
}
