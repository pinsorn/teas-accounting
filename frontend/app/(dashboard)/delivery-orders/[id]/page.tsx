'use client';

import { use } from 'react';
import { useRouter } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { PageHeader } from '@/components/ui/PageHeader';
import { DocActionBar } from '@/components/ui/DocActionBar';
import { PaperDocument } from '@/components/paper/PaperDocument';
import { ActivityLog } from '@/components/doc/ActivityLog';
import { DocumentChain } from '@/components/doc/DocumentChain';
import { useDeliveryOrder, useDeliveryOrderAction, useCreateInvoiceFromDeliveryOrder, useCompanyProfile, useCustomer, useSystemInfo } from '@/lib/queries';
import { PAPER_DOC, paperWatermark, companyToSeller, custInfo } from '@/lib/paper-doc-config';
import { AttachmentsSection } from '@/components/attachments/AttachmentsSection';
import { PrintMenu } from '@/components/ui/PrintMenu';

export default function DeliveryOrderDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const doId = Number(id);
  const router = useRouter();
  const t = useTranslations('deliveryOrder');
  const tc = useTranslations('common');
  const q = useDeliveryOrder(doId);
  const act = useDeliveryOrderAction();
  const createInvoice = useCreateInvoiceFromDeliveryOrder();
  const company = useCompanyProfile();
  const cust = useCustomer(q.data?.customerId ?? null);
  // ม.86/4 — a non-VAT company issues no Tax Invoice, so hide the DO→TI action.
  const vatMode = useSystemInfo().data?.vatMode ?? true;
  const d = q.data;

  async function run(action: string) {
    try { await act.mutateAsync({ id: doId, action }); toast.success(tc('save')); }
    catch (e) { toast.error((e as { detail?: string })?.detail ?? tc('error')); }
  }

  async function createInvoiceFromDo() {
    try {
      const res = await createInvoice.mutateAsync(doId);
      router.push(`/invoices/${res.billing_note_id}`);
    } catch (e) {
      toast.error((e as { detail?: string })?.detail ?? tc('error'));
    }
  }

  if (!d) return <div className="p-6 text-base-content/50">{tc('loading')}</div>;

  const cfg = PAPER_DOC['delivery-order'];

  return (
    <>
      <PageHeader
        title={`${t('listTitle')} ${d.docNo ?? `#${d.deliveryOrderId}`}`}
        actions={<PrintMenu docType="delivery-orders" id={doId} />}
      />

      <DocActionBar
        status={d.status}
        docNo={d.docNo ?? `#${d.deliveryOrderId}`}
        actions={
          <>
            {d.status === 'Draft' && (
              <button data-testid="do-issue" className="btn btn-primary btn-sm" disabled={act.isPending} onClick={() => run('issue')}>
                {t('issue')}
              </button>
            )}
            {d.status === 'Issued' && (
              <button data-testid="do-mark-delivered" className="btn btn-primary btn-sm" disabled={act.isPending} onClick={() => run('mark-delivered')}>
                {t('markDelivered')}
              </button>
            )}
            {vatMode && d.status === 'Delivered' && !d.isCombinedWithTi && d.taxInvoiceId == null && (
              <button data-testid="do-create-ti" className="btn btn-primary btn-sm" disabled={act.isPending} onClick={() => run('create-ti')}>
                {t('createTi')}
              </button>
            )}
            {/* Phase 2a new flow: DO → Invoice (ใบแจ้งหนี้). Shown for an issued/
                delivered DO that has no Invoice yet. */}
            {(d.status === 'Delivered' || d.status === 'Issued') && d.billingNoteId == null && (
              <button data-testid="do-create-invoice" className="btn btn-primary btn-sm" disabled={createInvoice.isPending} onClick={createInvoiceFromDo}>
                {t('createInvoice')}
              </button>
            )}
          </>
        }
      />

      <div className="detail-grid">
        <div className="paper-wrap">
          <PaperDocument
            docType={cfg.docType}
            docTypeEn={cfg.docTypeEn}
            docNo={d.docNo ?? `#${d.deliveryOrderId}`}
            issueDate={d.docDate}
            seller={companyToSeller(company.data)}
            customer={custInfo(d.customerName, cust.data)}
            items={d.lines.map((l) => ({
              description: l.descriptionTh,
              quantity: l.quantity,
              unit: l.uomText,
              unitPrice: l.unitPrice,
              amount: l.lineAmount,
            }))}
            summary={{ subtotal: d.subtotalAmount, vat: d.vatAmount, total: d.totalAmount }}
            signRoles={cfg.signRoles}
            watermark={paperWatermark('delivery-order', d.status)}
          />
        </div>
        <div className="detail-side">
          <DocumentChain type="delivery-order" id={doId} />
          <ActivityLog docType="delivery-orders" id={doId} />
        </div>
      </div>

      <AttachmentsSection parentType="DELIVERY_ORDER" parentId={doId} />
    </>
  );
}
