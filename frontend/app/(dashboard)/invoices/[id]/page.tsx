'use client';

import { use, useState } from 'react';
import { useRouter } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { PageHeader } from '@/components/ui/PageHeader';
import { DocActionBar } from '@/components/ui/DocActionBar';
import { PaperDocument } from '@/components/paper/PaperDocument';
import { ActivityLog } from '@/components/doc/ActivityLog';
import { DocumentChain } from '@/components/doc/DocumentChain';
import { useBillingNote, useBillingNoteAction, useCreateTaxInvoiceFromBillingNote, useDeleteBillingNote, useCompanyProfile, useCustomer, useSystemInfo } from '@/lib/queries';
import { PAPER_DOC, paperWatermark, companyToSeller, custInfo } from '@/lib/paper-doc-config';
import { AttachmentsSection } from '@/components/attachments/AttachmentsSection';
import { useConfirm } from '@/hooks/useConfirm';
import { PrintMenu } from '@/components/ui/PrintMenu';

// Sprint 13h P6.2 — Billing Note detail. Draft → Issue/Delete. Issued → Cancel/MarkSettled.

export default function BillingNoteDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const bnId = Number(id);
  const router = useRouter();
  const t = useTranslations('billingNote');
  const tc = useTranslations('common');
  const q = useBillingNote(bnId);
  const act = useBillingNoteAction();
  const createTi = useCreateTaxInvoiceFromBillingNote();
  const del = useDeleteBillingNote();
  const confirm = useConfirm();
  const company = useCompanyProfile();
  const cust = useCustomer(q.data?.customerId ?? null);
  // ม.86/4 — only a VAT-registered company issues a Tax Invoice from the Invoice.
  const vatMode = useSystemInfo().data?.vatMode ?? true;
  const [cancelReason, setCancelReason] = useState('');
  const [showCancel, setShowCancel] = useState(false);
  const d = q.data;

  async function run(action: string, body?: unknown) {
    try {
      await act.mutateAsync({ id: bnId, action, body });
      toast.success(tc('save'));
    } catch (e) {
      toast.error((e as { detail?: string })?.detail ?? tc('error'));
    }
  }

  async function createTaxInvoice() {
    try {
      const res = await createTi.mutateAsync(bnId);
      router.push(`/tax-invoices/${res.tax_invoice_id}`);
    } catch (e) {
      // 422 ti.non_vat_blocked (or other) → surface the detail.
      toast.error((e as { detail?: string })?.detail ?? tc('error'));
    }
  }

  async function deleteDraft() {
    if (!(await confirm({ description: tc('confirmDelete'), variant: 'destructive' }))) return;
    try {
      await del.mutateAsync(bnId);
      toast.success(tc('save'));
      window.location.href = '/invoices';
    } catch (e) {
      toast.error((e as { detail?: string })?.detail ?? tc('error'));
    }
  }

  if (!d) return <div className="p-6 text-base-content/50">{tc('loading')}</div>;

  const cfg = PAPER_DOC['billing-note'];

  return (
    <>
      <PageHeader
        title={`${t('listTitle')} ${d.docNo ?? `#${d.billingNoteId}`}`}
        actions={<PrintMenu docType="billing-notes" id={bnId} />}
      />

      <DocActionBar
        status={d.status}
        statusTestId="bn-status"
        docNo={d.docNo ?? `#${d.billingNoteId}`}
        actions={
          <>
            {d.status === 'Draft' && (
              <>
                <button data-testid="bn-issue-action" className="btn btn-primary btn-sm" disabled={act.isPending} onClick={() => run('issue')}>
                  {t('issue')}
                </button>
                <button data-testid="bn-delete" className="btn btn-danger btn-sm" disabled={del.isPending} onClick={deleteDraft}>
                  {tc('delete')}
                </button>
              </>
            )}
            {/* Non-VAT (ม.86/4): no Tax Invoice — the Invoice settles straight to a Receipt.
                Available while Issued OR already marked settled (the Receipt is still the
                payment document; "ยืนยันชำระครบแล้ว" must not strand the user). WHT is
                auto-categorized server-side from the Invoice's service lines on /receipts/new. */}
            {!vatMode && (d.status === 'Issued' || d.status === 'Settled') && (
              <button
                data-testid="bn-create-receipt"
                className="btn btn-primary btn-sm"
                onClick={() => router.push(`/receipts/new?bn=${bnId}&customer=${d.customerId}&amount=${d.totalAmount}`)}
              >
                {t('createReceipt')}
              </button>
            )}
            {/* Phase 2a: Invoice → Tax Invoice. VAT only, while no TI issued yet. Stays
                available after "ยืนยันชำระครบแล้ว" (Settled) — settling must not strand it. */}
            {vatMode && (d.status === 'Issued' || d.status === 'Settled') && (d.taxInvoices?.length ?? 0) === 0 && (
              <button data-testid="bn-create-ti" className="btn btn-primary btn-sm" disabled={createTi.isPending} onClick={createTaxInvoice}>
                {t('createTaxInvoice')}
              </button>
            )}
            {d.status === 'Issued' && (
              <>
                <button data-testid="bn-mark-settled" className="btn btn-primary btn-sm" disabled={act.isPending} onClick={() => run('mark-settled')}>
                  {t('markSettled')}
                </button>
                <button data-testid="bn-cancel-toggle" className="btn btn-danger btn-sm" onClick={() => setShowCancel((v) => !v)}>
                  {tc('cancel')}
                </button>
              </>
            )}
          </>
        }
      />

      {showCancel && d.status === 'Issued' && (
        <div className="mb-4 flex items-center gap-2">
          <input
            className="input input-bordered input-sm max-w-md flex-1"
            placeholder={t('cancelReasonPlaceholder')}
            value={cancelReason}
            onChange={(e) => setCancelReason(e.target.value)}
          />
          <button
            data-testid="bn-cancel-confirm"
            className="btn btn-danger btn-sm"
            disabled={!cancelReason || act.isPending}
            onClick={() => run('cancel', { reason: cancelReason })}
          >
            {tc('confirm')}
          </button>
        </div>
      )}

      <div className="detail-grid">
        <div className="paper-wrap">
          <PaperDocument
            docType={cfg.docType}
            docTypeEn={cfg.docTypeEn}
            docNo={d.docNo ?? `#${d.billingNoteId}`}
            issueDate={d.docDate}
            validUntil={d.dueDate}
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
            notes={d.notes}
            signRoles={cfg.signRoles}
            watermark={paperWatermark('billing-note', d.status)}
          />
        </div>
        <div className="detail-side">
          <DocumentChain type="billing-note" id={bnId} />
          <ActivityLog docType="billing-notes" id={bnId} />
        </div>
      </div>

      <AttachmentsSection parentType="BILLING_NOTE" parentId={bnId} />
    </>
  );
}
