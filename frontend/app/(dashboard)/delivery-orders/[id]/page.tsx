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
import { useDeliveryOrder, useDeliveryOrderAction, useCreateInvoiceFromDeliveryOrder, useCompanyProfile, usePaperDoc, useSystemInfo } from '@/lib/queries';
import { paperDtoToProps } from '@/lib/paper-doc-config';
import { AttachmentsSection } from '@/components/attachments/AttachmentsSection';
import { PrintMenu } from '@/components/ui/PrintMenu';
import { useScopeState } from '@/components/PermissionGate';

export default function DeliveryOrderDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const doId = Number(id);
  const router = useRouter();
  const t = useTranslations('deliveryOrder');
  const tc = useTranslations('common');
  const q = useDeliveryOrder(doId);
  const act = useDeliveryOrderAction();
  const createInvoice = useCreateInvoiceFromDeliveryOrder();
  // cont.121 — paper preview data comes from the canonical /paper DTO (screen ==
  // print, incl. the combined ใบส่งของ-ใบกำกับภาษี title); the company profile
  // stays ONLY as the logo source (not in the DTO).
  const company = useCompanyProfile();
  const paper = usePaperDoc('delivery-orders', doId);
  // ม.86/4 — a non-VAT company issues no Tax Invoice, so hide the DO→TI action.
  const vatMode = useSystemInfo().data?.vatMode ?? true;
  // F6 — two independent convert targets, two independent permission checks.
  const canCreateTi = useScopeState('sales.tax_invoice.create');
  const canCreateInvoiceFromDo = useScopeState('sales.billing_note.manage');
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

  if (!d || !paper.data) return <div className="p-6 text-base-content/50">{tc('loading')}</div>;

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
              <span
                className={!canCreateTi.pending && !canCreateTi.allowed ? 'tooltip' : undefined}
                data-tip={!canCreateTi.pending && !canCreateTi.allowed ? tc('noPermissionTooltip', { perm: 'sales.tax_invoice.create' }) : undefined}
              >
                <button
                  data-testid="do-create-ti"
                  className="btn btn-primary btn-sm"
                  disabled={act.isPending || canCreateTi.pending || !canCreateTi.allowed}
                  onClick={() => run('create-ti')}
                >
                  {t('createTi')}
                </button>
              </span>
            )}
            {/* Phase 2a new flow: DO → Invoice (ใบแจ้งหนี้). Shown for an issued/
                delivered DO that has no Invoice yet. */}
            {(d.status === 'Delivered' || d.status === 'Issued') && d.billingNoteId == null && (
              <span
                className={!canCreateInvoiceFromDo.pending && !canCreateInvoiceFromDo.allowed ? 'tooltip' : undefined}
                data-tip={!canCreateInvoiceFromDo.pending && !canCreateInvoiceFromDo.allowed ? tc('noPermissionTooltip', { perm: 'sales.billing_note.manage' }) : undefined}
              >
                <button
                  data-testid="do-create-invoice"
                  className="btn btn-primary btn-sm"
                  disabled={createInvoice.isPending || canCreateInvoiceFromDo.pending || !canCreateInvoiceFromDo.allowed}
                  onClick={createInvoiceFromDo}
                >
                  {t('createInvoice')}
                </button>
              </span>
            )}
          </>
        }
      />

      <div className="detail-grid">
        <div className="paper-wrap">
          <PaperDocument {...paperDtoToProps(paper.data, { logo: company.data?.logoUrl })} />
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
