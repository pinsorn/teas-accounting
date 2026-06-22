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
import { useSalesOrder, usePostSalesOrder, useCreateDeliveryOrder, useCompanyProfile, useCustomer, useSystemInfo } from '@/lib/queries';
import { PAPER_DOC, paperWatermark, companyToSeller, custInfo } from '@/lib/paper-doc-config';
import { AttachmentsSection } from '@/components/attachments/AttachmentsSection';
import { PrintMenu } from '@/components/ui/PrintMenu';

export default function SalesOrderDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const soId = Number(id);
  const t = useTranslations('salesOrder');
  const tc = useTranslations('common');
  const router = useRouter();
  const q = useSalesOrder(soId);
  const post = usePostSalesOrder();
  const makeDo = useCreateDeliveryOrder();
  const company = useCompanyProfile();
  const cust = useCustomer(q.data?.customerId ?? null);
  // §4.6 / ม.86 — a non-VAT company carries no VAT and can't issue a Tax Invoice,
  // so the DO must not be VAT-coded nor combined with a TI. (Backend re-derives this
  // too; this keeps the request honest.)
  const vatMode = useSystemInfo().data?.vatMode ?? true;
  const d = q.data;

  async function doPost() {
    try { await post.mutateAsync(soId); toast.success(tc('save')); }
    catch (e) { toast.error((e as { detail?: string })?.detail ?? tc('error')); }
  }

  async function createDelivery() {
    if (!d) return;
    try {
      const r = await makeDo.mutateAsync({
        soId,
        req: {
          docDate: new Date().toISOString().slice(0, 10),
          customerId: d.customerId, businessUnitId: d.businessUnitId,
          isCombinedWithTi: vatMode, notes: null, fromSalesOrderId: soId,
          lines: d.lines.map((l) => ({
            salesOrderLineId: null, productId: l.productId,
            descriptionTh: l.descriptionTh, quantity: l.quantity,
            uomText: l.uomText, unitPrice: l.unitPrice, discountPercent: 0,
            taxCodeId: 1, taxCode: vatMode ? 'VAT7' : 'VAT0', taxRate: vatMode ? 0.07 : 0,
          })),
        },
      }) as { delivery_order_id: number };
      toast.success(tc('save'));
      router.push(`/delivery-orders/${r.delivery_order_id}`);
    } catch (e) {
      toast.error((e as { detail?: string })?.detail ?? tc('error'));
    }
  }

  if (!d) return <div className="p-6 text-base-content/50">{tc('loading')}</div>;

  const cfg = PAPER_DOC['sales-order'];

  return (
    <>
      <PageHeader
        title={`${t('listTitle')} ${d.docNo ?? `#${d.salesOrderId}`}`}
        actions={<PrintMenu docType="sales-orders" id={soId} />}
      />

      <DocActionBar
        status={d.status}
        statusTestId="so-status"
        docNo={d.docNo ?? `#${d.salesOrderId}`}
        actions={
          <>
            {d.status === 'Draft' && (
              <button data-testid="so-post" className="btn btn-primary btn-sm" disabled={post.isPending} onClick={doPost}>
                {t('post')}
              </button>
            )}
            {d.status === 'Posted' && (
              <button data-testid="so-create-do" className="btn btn-primary btn-sm" disabled={makeDo.isPending} onClick={createDelivery}>
                {t('createDo')}
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
            docNo={d.docNo ?? `#${d.salesOrderId}`}
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
            watermark={paperWatermark('sales-order', d.status)}
          />
        </div>
        <div className="detail-side">
          <DocumentChain type="sales-order" id={soId} />
          <ActivityLog docType="sales-orders" id={soId} />
        </div>
      </div>

      <AttachmentsSection parentType="SALES_ORDER" parentId={soId} />
    </>
  );
}
