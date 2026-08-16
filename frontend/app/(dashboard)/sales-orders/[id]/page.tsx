'use client';

import { use, useState } from 'react';
import { useRouter } from 'next/navigation';
import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { Pencil } from 'lucide-react';
import { PageHeader } from '@/components/ui/PageHeader';
import { DocActionBar } from '@/components/ui/DocActionBar';
import { ConfirmActionDialog } from '@/components/ui/ConfirmActionDialog';
import { BusinessUnitBadge } from '@/components/ui/BusinessUnitBadge';
import { PaperDocument } from '@/components/paper/PaperDocument';
import { ActivityLog } from '@/components/doc/ActivityLog';
import { DocumentChain } from '@/components/doc/DocumentChain';
import { useSalesOrder, usePostSalesOrder, useCreateDeliveryOrder, useCreateInvoiceFromSalesOrder, useCompanyProfile, usePaperDoc, useSystemInfo } from '@/lib/queries';
import { paperDtoToProps } from '@/lib/paper-doc-config';
import { AttachmentsSection } from '@/components/attachments/AttachmentsSection';
import { PrintMenu } from '@/components/ui/PrintMenu';
import { useScopeState } from '@/components/PermissionGate';

export default function SalesOrderDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const soId = Number(id);
  const t = useTranslations('salesOrder');
  const tc = useTranslations('common');
  const tca = useTranslations('confirmAction');
  const router = useRouter();
  const q = useSalesOrder(soId);
  const post = usePostSalesOrder();
  const makeDo = useCreateDeliveryOrder();
  const makeInvoice = useCreateInvoiceFromSalesOrder();
  // cont.121 — paper preview data comes from the canonical /paper DTO (screen ==
  // print); the company profile stays ONLY as the logo source (not in the DTO).
  const company = useCompanyProfile();
  const paper = usePaperDoc('sales-orders', soId);
  // §4.6 / ม.86 — a non-VAT company carries no VAT and can't issue a Tax Invoice,
  // so the DO must not be VAT-coded nor combined with a TI. (Backend re-derives this
  // too; this keeps the request honest.)
  const vatMode = useSystemInfo().data?.vatMode ?? true;
  // F6 — mirrors SalesChainEndpoints.cs:114-127 exactly: a VAT-registered company
  // creates a Tax Invoice from this button (needs sales.tax_invoice.create); a
  // non-VAT one creates an Invoice/billing note instead (needs sales.billing_note.manage).
  const createInvoiceScope = vatMode ? 'sales.tax_invoice.create' : 'sales.billing_note.manage';
  const canCreateInvoice = useScopeState(createInvoiceScope);
  // S11 — post issues the doc number immediately with no confirm step.
  const [confirmPost, setConfirmPost] = useState(false);
  const d = q.data;

  async function doPost() {
    try { await post.mutateAsync(soId); toast.success(tc('save')); }
    catch (e) { toast.error((e as { detail?: string })?.detail ?? tc('error')); }
    finally { setConfirmPost(false); }
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

  // mcp-document-chain (D9) — service-only SO (deliveryRequired=false) invoices directly,
  // skipping the Delivery Order step. Polymorphic response: navigate to whichever doc came back.
  async function createInvoiceDirect() {
    try {
      const r = await makeInvoice.mutateAsync(soId);
      toast.success(tc('save'));
      router.push(r.tax_invoice_id != null ? `/tax-invoices/${r.tax_invoice_id}` : `/invoices/${r.billing_note_id}`);
    } catch (e) {
      toast.error((e as { detail?: string })?.detail ?? tc('error'));
    }
  }

  if (!d || !paper.data) return <div className="p-6 text-base-content/50">{tc('loading')}</div>;

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
              <>
                {/* S15 (F6-parity) — draft-only edit (reuses SalesOrderForm via `edit`). */}
                <Link data-testid="so-edit" href={`/sales-orders/${soId}/edit`} className="btn btn-secondary btn-sm gap-1">
                  <Pencil className="h-4 w-4" aria-hidden /> {tc('edit')}
                </Link>
                <button data-testid="so-post" className="btn btn-primary btn-sm" disabled={post.isPending} onClick={() => setConfirmPost(true)}>
                  {t('post')}
                </button>
              </>
            )}
            {d.status === 'Posted' && (
              d.deliveryRequired ? (
                <button data-testid="so-create-do" className="btn btn-primary btn-sm" disabled={makeDo.isPending} onClick={createDelivery}>
                  {t('createDo')}
                </button>
              ) : (
                <span
                  className={!canCreateInvoice.pending && !canCreateInvoice.allowed ? 'tooltip' : undefined}
                  data-tip={!canCreateInvoice.pending && !canCreateInvoice.allowed ? tc('noPermissionTooltip', { perm: createInvoiceScope }) : undefined}
                >
                  <button
                    data-testid="so-create-invoice"
                    className="btn btn-primary btn-sm"
                    disabled={makeInvoice.isPending || canCreateInvoice.pending || !canCreateInvoice.allowed}
                    onClick={createInvoiceDirect}
                  >
                    {t('createInvoice')}
                  </button>
                </span>
              )
            )}
          </>
        }
      />

      {/* S10 — BU wasn't visible anywhere on the SO detail page though the API carries it. */}
      <div className="mb-4"><BusinessUnitBadge businessUnitId={d.businessUnitId} /></div>

      <div className="detail-grid">
        <div className="paper-wrap">
          <PaperDocument {...paperDtoToProps(paper.data, { logo: company.data?.logoUrl })} />
        </div>
        <div className="detail-side">
          <DocumentChain type="sales-order" id={soId} />
          <ActivityLog docType="sales-orders" id={soId} />
        </div>
      </div>

      <AttachmentsSection parentType="SALES_ORDER" parentId={soId} />

      <ConfirmActionDialog
        open={confirmPost}
        busy={post.isPending}
        title={tca('soPost.title')}
        warning={tca('soPost.warning')}
        party={d.customerName}
        rows={[
          { label: t('vat'), value: d.vatAmount, muted: true },
          { label: t('total'), value: d.totalAmount },
        ]}
        onClose={() => setConfirmPost(false)}
        onConfirm={doPost}
      />
    </>
  );
}
