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
import { useBillingNote, useBillingNoteAction, useCreateTaxInvoiceFromBillingNote, useDeleteBillingNote, useCompanyProfile, usePaperDoc, useSystemInfo } from '@/lib/queries';
import { paperDtoToProps } from '@/lib/paper-doc-config';
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
  const tca = useTranslations('confirmAction');
  const q = useBillingNote(bnId);
  const act = useBillingNoteAction();
  const createTi = useCreateTaxInvoiceFromBillingNote();
  const del = useDeleteBillingNote();
  const confirm = useConfirm();
  // cont.121 — paper preview data comes from the canonical /paper DTO (screen ==
  // print); the company profile stays ONLY as the logo source (not in the DTO).
  const company = useCompanyProfile();
  const paper = usePaperDoc('billing-notes', bnId);
  // ม.86/4 — only a VAT-registered company issues a Tax Invoice from the Invoice.
  const vatMode = useSystemInfo().data?.vatMode ?? true;
  const [cancelReason, setCancelReason] = useState('');
  const [showCancel, setShowCancel] = useState(false);
  // S11 — issue/mark-settled had no confirm dialog (issue issues the doc number
  // immediately, immutable numbering).
  const [confirmIssue, setConfirmIssue] = useState(false);
  const [confirmMarkSettled, setConfirmMarkSettled] = useState(false);
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

  if (!d || !paper.data) return <div className="p-6 text-base-content/50">{tc('loading')}</div>;

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
                {/* S15 (F6-parity) — draft-only edit (reuses BillingNoteForm via `edit`). */}
                <Link data-testid="bn-edit" href={`/invoices/${bnId}/edit`} className="btn btn-secondary btn-sm gap-1">
                  <Pencil className="h-4 w-4" aria-hidden /> {tc('edit')}
                </Link>
                <button data-testid="bn-issue-action" className="btn btn-primary btn-sm" disabled={act.isPending} onClick={() => setConfirmIssue(true)}>
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
                <button data-testid="bn-mark-settled" className="btn btn-primary btn-sm" disabled={act.isPending} onClick={() => setConfirmMarkSettled(true)}>
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

      {/* S10 — BU wasn't visible anywhere on the Invoice detail page though the API carries it. */}
      <div className="mb-4"><BusinessUnitBadge businessUnitId={d.businessUnitId} /></div>

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
          <PaperDocument {...paperDtoToProps(paper.data, { logo: company.data?.logoUrl })} />
        </div>
        <div className="detail-side">
          <DocumentChain type="billing-note" id={bnId} />
          <ActivityLog docType="billing-notes" id={bnId} />
        </div>
      </div>

      <AttachmentsSection parentType="BILLING_NOTE" parentId={bnId} />

      <ConfirmActionDialog
        open={confirmIssue}
        busy={act.isPending}
        title={tca('bnIssue.title')}
        warning={tca('bnIssue.warning')}
        party={d.customerName}
        rows={[
          { label: t('vat'), value: d.vatAmount, muted: true },
          { label: t('total'), value: d.totalAmount },
        ]}
        onClose={() => setConfirmIssue(false)}
        onConfirm={async () => { await run('issue'); setConfirmIssue(false); }}
      />
      <ConfirmActionDialog
        open={confirmMarkSettled}
        busy={act.isPending}
        title={tca('bnMarkSettled.title')}
        warning={tca('bnMarkSettled.warning')}
        party={d.customerName}
        rows={[{ label: t('total'), value: d.totalAmount }]}
        onClose={() => setConfirmMarkSettled(false)}
        onConfirm={async () => { await run('mark-settled'); setConfirmMarkSettled(false); }}
      />
    </>
  );
}
