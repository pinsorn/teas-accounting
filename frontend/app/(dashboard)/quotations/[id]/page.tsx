'use client';

import { use } from 'react';
import { useRouter } from 'next/navigation';
import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { Pencil, Trash2 } from 'lucide-react';
import { PrintMenu } from '@/components/ui/PrintMenu';
import { PageHeader } from '@/components/ui/PageHeader';
import { DocActionBar } from '@/components/ui/DocActionBar';
import { PaperDocument } from '@/components/paper/PaperDocument';
import { ActivityLog } from '@/components/doc/ActivityLog';
import { DocumentChain } from '@/components/doc/DocumentChain';
import { useQuotation, useQuotationAction, useDeleteQuotation, useCompanyProfile, useCustomer, useSystemInfo } from '@/lib/queries';
import { PAPER_DOC, paperWatermark, companyToSeller, custInfo } from '@/lib/paper-doc-config';
import { AttachmentsSection } from '@/components/attachments/AttachmentsSection';
import { useConfirm } from '@/hooks/useConfirm';

export default function QuotationDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const qid = Number(id);
  const t = useTranslations('quotation');
  const tc = useTranslations('common');
  const router = useRouter();
  const q = useQuotation(qid);
  const act = useQuotationAction();
  const del = useDeleteQuotation();
  const confirm = useConfirm();
  const company = useCompanyProfile();
  const cust = useCustomer(q.data?.customerId ?? null);
  // ม.86/4 — non-VAT companies cannot issue a Tax Invoice from an accepted quote.
  const vatMode = useSystemInfo().data?.vatMode ?? true;
  const d = q.data;

  async function run(action: string, body?: unknown) {
    try {
      const res = await act.mutateAsync({ id: qid, action, body });
      toast.success(tc('save'));
      if (action === 'convert-to-so') {
        const so = (res as { sales_order_id: number }).sales_order_id;
        router.push(`/sales-orders/${so}`);
      }
    } catch (e) {
      toast.error((e as { detail?: string })?.detail ?? tc('error'));
    }
  }

  async function cancelQuotation() {
    if (!(await confirm({ description: t('cancelConfirm'), variant: 'destructive' }))) return;
    await run('cancel', { reason: t('cancelReason') });
  }

  async function rejectQuotation() {
    if (!(await confirm({ description: t('rejectConfirm'), variant: 'destructive' }))) return;
    await run('reject', { reason: t('rejectReason') });
  }

  async function deleteDraft() {
    if (!(await confirm({ description: tc('confirmDelete'), variant: 'destructive' }))) return;
    try {
      await del.mutateAsync(qid);
      toast.success(tc('deleted'));
      router.push('/quotations');
    } catch (e) {
      toast.error((e as { detail?: string })?.detail ?? tc('error'));
    }
  }

  if (!d) return <div className="p-6 text-base-content/50">{tc('loading')}</div>;

  const canCancel = d.status === 'Sent' || d.status === 'Accepted';
  const cfg = PAPER_DOC.quotation;

  return (
    <>
      <PageHeader
        title={`${t('listTitle')} ${d.docNo ?? `#${d.quotationId}`}`}
        actions={<PrintMenu docType="quotations" id={qid} />}
      />

      <DocActionBar
        status={d.status}
        docNo={d.docNo ?? `#${d.quotationId}`}
        actions={
          <>
            {d.status === 'Draft' && (
              <>
                <Link data-testid="q-edit" href={`/quotations/${qid}/edit`} className="btn btn-secondary btn-sm gap-1">
                  <Pencil className="h-4 w-4" aria-hidden /> {tc('edit')}
                </Link>
                <button data-testid="q-send" className="btn btn-primary btn-sm" disabled={act.isPending} onClick={() => run('send')}>
                  {t('send')}
                </button>
                <button data-testid="q-delete" className="btn btn-danger btn-sm gap-1" disabled={del.isPending} onClick={deleteDraft}>
                  <Trash2 className="h-4 w-4" aria-hidden /> {tc('delete')}
                </button>
              </>
            )}
            {d.status === 'Sent' && (
              <>
                <button data-testid="q-accept" className="btn btn-primary btn-sm" disabled={act.isPending} onClick={() => run('accept')}>
                  {t('accept')}
                </button>
                <button data-testid="q-reject" className="btn btn-danger btn-sm" disabled={act.isPending} onClick={rejectQuotation}>
                  {t('reject')}
                </button>
              </>
            )}
            {d.status === 'Accepted' && d.convertedToSoId == null && (
              <button data-testid="q-convert" className="btn btn-primary btn-sm" disabled={act.isPending} onClick={() => run('convert-to-so')}>
                {t('convertToSo')}
              </button>
            )}
            {d.status === 'Accepted' && vatMode && (
              <Link data-testid="q-create-ti" href={`/tax-invoices/new?fromQuotationId=${d.quotationId}`} className="btn btn-primary btn-sm">
                {t('createTaxInvoice')}
              </Link>
            )}
            {canCancel && (
              <button data-testid="q-cancel" className="btn btn-danger btn-sm" disabled={act.isPending} onClick={cancelQuotation}>
                {t('cancelQuotation')}
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
            docNo={d.docNo ?? `#${d.quotationId}`}
            issueDate={d.docDate}
            validUntil={d.validUntilDate}
            validUntilLabel={cfg.validUntilLabel}
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
            notes={d.notes ?? (d.showWhtNote ? t('whtNote') : null)}
            signRoles={cfg.signRoles}
            watermark={paperWatermark('quotation', d.status)}
          />
        </div>
        <div className="detail-side">
          <DocumentChain type="quotation" id={qid} />
          <ActivityLog docType="quotations" id={qid} />
        </div>
      </div>

      <AttachmentsSection parentType="QUOTATION" parentId={qid} />
    </>
  );
}
