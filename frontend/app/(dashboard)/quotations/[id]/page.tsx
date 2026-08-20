'use client';

import { use, useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { Pencil, Trash2 } from 'lucide-react';
import { PrintMenu } from '@/components/ui/PrintMenu';
import { PageHeader } from '@/components/ui/PageHeader';
import { AgentPendingBadge } from '@/components/ui/AgentPendingBadge';
import { DocActionBar } from '@/components/ui/DocActionBar';
import { ConfirmActionDialog } from '@/components/ui/ConfirmActionDialog';
import { BusinessUnitBadge } from '@/components/ui/BusinessUnitBadge';
import { PaperDocument } from '@/components/paper/PaperDocument';
import { ActivityLog } from '@/components/doc/ActivityLog';
import { DocumentChain } from '@/components/doc/DocumentChain';
import { useQuotation, useQuotationAction, useDeleteQuotation, useCompanyProfile, usePaperDoc, useSystemInfo, useCreateTaxInvoiceFromQuotation } from '@/lib/queries';
import { paperDtoToProps } from '@/lib/paper-doc-config';
import { AttachmentsSection } from '@/components/attachments/AttachmentsSection';
import { useConfirm } from '@/hooks/useConfirm';
import { useHasScope, useScopeState } from '@/components/PermissionGate';
import { problemToast } from '@/lib/api';

export default function QuotationDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const qid = Number(id);
  const t = useTranslations('quotation');
  const tc = useTranslations('common');
  const ta = useTranslations('approve');
  const tca = useTranslations('confirmAction');
  const router = useRouter();
  const q = useQuotation(qid);
  const act = useQuotationAction();
  const del = useDeleteQuotation();
  const makeTi = useCreateTaxInvoiceFromQuotation();
  const confirm = useConfirm();
  // cont.121 — paper preview data comes from the canonical /paper DTO (screen ==
  // print); the company profile stays ONLY as the logo source (not in the DTO).
  const company = useCompanyProfile();
  const paper = usePaperDoc('quotations', qid);
  // ม.86/4 — non-VAT companies cannot issue a Tax Invoice from an accepted quote.
  const vatMode = useSystemInfo().data?.vatMode ?? true;
  const hasScope = useHasScope();
  // F6 — the target of the convert action is the Sales Order, so the button needs
  // sales.sales_order.manage (not a quotation scope) to match the backend 403.
  const canConvertToSo = useScopeState('sales.sales_order.manage');
  // F8b/WP-4 — mirrors q-convert exactly: gates on the TARGET permission only
  // (sales.tax_invoice.create), even though the route ALSO requires qManage server-side
  // (R3/H3 — same precedent as q-convert, which gates on sales_order.manage only and
  // not quotation.manage too). A quotation.read + tax_invoice.create user sees an enabled
  // button and can get a 403 — pre-existing precedent, not a new F6 gap.
  const canCreateTi = useScopeState('sales.tax_invoice.create');
  const [isApproveAction, setIsApproveAction] = useState(false);
  // S11 — send/accept had no confirm dialog (send issues the doc number
  // immediately, immutable numbering). Mirrors the WP3.6 purchase-side pattern.
  const [confirmSend, setConfirmSend] = useState(false);
  const [confirmAccept, setConfirmAccept] = useState(false);
  // fix-codex-ui-review-2026-08-20 UI-3 — cancel/reject require a REAL reason
  // from the user (audit trail), not a canned string. Mirrors the Invoice
  // page's cancel idiom (invoices/[id]/page.tsx:47,177-188): toggle + inline
  // input + confirm disabled while empty.
  const [showCancel, setShowCancel] = useState(false);
  const [cancelReason, setCancelReason] = useState('');
  const [showReject, setShowReject] = useState(false);
  const [rejectReason, setRejectReason] = useState('');
  const d = q.data;

  useEffect(() => {
    const action = new URLSearchParams(window.location.search).get('action');
    if (action === 'approve') setIsApproveAction(true);
  }, []);

  async function run(action: string, body?: unknown) {
    try {
      const res = await act.mutateAsync({ id: qid, action, body });
      toast.success(tc('save'));
      if (action === 'convert-to-so') {
        const so = (res as { sales_order_id: number }).sales_order_id;
        router.push(`/sales-orders/${so}`);
      }
    } catch (e) {
      problemToast(e, tc('error'));
    }
  }

  // F8b (specs/fix-chain-conversion-integrity.md) — server-side conversion: no line payload,
  // no prefilled create form. Lands directly on the created draft TI (mirrors q-convert).
  async function createTaxInvoice() {
    try {
      const r = await makeTi.mutateAsync(qid);
      toast.success(tc('save'));
      router.push(`/tax-invoices/${r.tax_invoice_id}`);
    } catch (e) {
      problemToast(e, tc('error'));
    }
  }

  async function deleteDraft() {
    if (!(await confirm({ description: tc('confirmDelete'), variant: 'destructive' }))) return;
    try {
      await del.mutateAsync(qid);
      toast.success(tc('deleted'));
      router.push('/quotations');
    } catch (e) {
      problemToast(e, tc('error'));
    }
  }

  if (!d || !paper.data) return <div className="p-6 text-base-content/50">{tc('loading')}</div>;

  const canCancel = d.status === 'Sent' || d.status === 'Accepted';

  return (
    <>
      <PageHeader
        title={`${t('listTitle')} ${d.docNo ?? `#${d.quotationId}`}`}
        actions={<PrintMenu docType="quotations" id={qid} />}
      />

      {/* B3 — agent-draft badge on normal navigation (independent of ?action=approve). */}
      {d.status === 'Draft' && d.createdViaApiKey && (
        <div className="mb-4"><AgentPendingBadge /></div>
      )}

      {/* ?action=approve — prominent approval banner for agent-created drafts */}
      {isApproveAction && d.status === 'Draft' && (
        <div className="mb-4 flex items-center justify-between gap-4 rounded-lg border border-warning bg-warning/10 p-4">
          <div>
            <p className="font-semibold text-warning-content">{ta('bannerTitle')}</p>
            <p className="mt-1 text-sm text-base-content/80">{ta('bannerDesc')}</p>
          </div>
          <div className="shrink-0">
            {hasScope('sales.quotation.send') ? (
              <button
                data-testid="q-approve-cta"
                className="btn btn-warning btn-sm"
                disabled={act.isPending}
                onClick={() => run('send')}
              >
                {ta('ctaSend')}
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

      <DocActionBar
        status={d.status}
        statusTestId="q-status"
        docNo={d.docNo ?? `#${d.quotationId}`}
        actions={
          <>
            {d.status === 'Draft' && (
              <>
                <Link data-testid="q-edit" href={`/quotations/${qid}/edit`} className="btn btn-secondary btn-sm gap-1">
                  <Pencil className="h-4 w-4" aria-hidden /> {tc('edit')}
                </Link>
                {/* Hidden while the ?action=approve banner is up — one send CTA at a time. */}
                {!isApproveAction && (
                  <button data-testid="q-send" className="btn btn-primary btn-sm" disabled={act.isPending} onClick={() => setConfirmSend(true)}>
                    {t('send')}
                  </button>
                )}
                <button data-testid="q-delete" className="btn btn-danger btn-sm gap-1" disabled={del.isPending} onClick={deleteDraft}>
                  <Trash2 className="h-4 w-4" aria-hidden /> {tc('delete')}
                </button>
              </>
            )}
            {d.status === 'Sent' && (
              <>
                <button data-testid="q-accept" className="btn btn-primary btn-sm" disabled={act.isPending} onClick={() => setConfirmAccept(true)}>
                  {t('accept')}
                </button>
                <button data-testid="q-reject" className="btn btn-danger btn-sm" disabled={act.isPending} onClick={() => setShowReject((v) => !v)}>
                  {t('reject')}
                </button>
              </>
            )}
            {d.status === 'Accepted' && d.convertedToSoId == null && (
              <span
                className={!canConvertToSo.pending && !canConvertToSo.allowed ? 'tooltip' : undefined}
                data-tip={!canConvertToSo.pending && !canConvertToSo.allowed ? tc('noPermissionTooltip', { perm: 'sales.sales_order.manage' }) : undefined}
              >
                <button
                  data-testid="q-convert"
                  className="btn btn-primary btn-sm"
                  disabled={act.isPending || canConvertToSo.pending || !canConvertToSo.allowed}
                  onClick={() => run('convert-to-so')}
                >
                  {t('convertToSo')}
                </button>
              </span>
            )}
            {d.status === 'Accepted' && vatMode && (
              <span
                className={!canCreateTi.pending && !canCreateTi.allowed ? 'tooltip' : undefined}
                data-tip={!canCreateTi.pending && !canCreateTi.allowed ? tc('noPermissionTooltip', { perm: 'sales.tax_invoice.create' }) : undefined}
              >
                <button
                  data-testid="q-create-ti"
                  className="btn btn-primary btn-sm"
                  disabled={makeTi.isPending || canCreateTi.pending || !canCreateTi.allowed}
                  onClick={createTaxInvoice}
                >
                  {t('createTaxInvoice')}
                </button>
              </span>
            )}
            {canCancel && (
              <button data-testid="q-cancel" className="btn btn-danger btn-sm" disabled={act.isPending} onClick={() => setShowCancel((v) => !v)}>
                {t('cancelQuotation')}
              </button>
            )}
          </>
        }
      />

      {/* S10 — BU wasn't visible anywhere on the QT detail page though the API carries it. */}
      <div className="mb-4"><BusinessUnitBadge businessUnitId={d.businessUnitId} /></div>

      {/* fix-codex-ui-review-2026-08-20 UI-3 — required-reason inline input, mirrors
          invoices/[id]/page.tsx:176-193 exactly (no canned string, no modal). */}
      {showCancel && canCancel && (
        <div className="mb-4 flex items-center gap-2">
          <input
            className="input input-bordered input-sm max-w-md flex-1"
            placeholder={t('cancelReasonPlaceholder')}
            value={cancelReason}
            onChange={(e) => setCancelReason(e.target.value)}
            maxLength={500}
          />
          <button
            data-testid="q-cancel-confirm"
            className="btn btn-danger btn-sm"
            disabled={!cancelReason.trim() || act.isPending}
            onClick={() => run('cancel', { reason: cancelReason.trim() })}
          >
            {tc('confirm')}
          </button>
        </div>
      )}
      {showReject && d.status === 'Sent' && (
        <div className="mb-4 flex items-center gap-2">
          <input
            className="input input-bordered input-sm max-w-md flex-1"
            placeholder={t('rejectReasonPlaceholder')}
            value={rejectReason}
            onChange={(e) => setRejectReason(e.target.value)}
            maxLength={500}
          />
          <button
            data-testid="q-reject-confirm"
            className="btn btn-danger btn-sm"
            disabled={!rejectReason.trim() || act.isPending}
            onClick={() => run('reject', { reason: rejectReason.trim() })}
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
          <DocumentChain type="quotation" id={qid} />
          <ActivityLog docType="quotations" id={qid} />
        </div>
      </div>

      <AttachmentsSection parentType="QUOTATION" parentId={qid} />

      <ConfirmActionDialog
        open={confirmSend}
        busy={act.isPending}
        title={tca('qtSend.title')}
        warning={tca('qtSend.warning')}
        party={d.customerName}
        rows={[
          { label: t('vat'), value: d.vatAmount, muted: true },
          { label: t('total'), value: d.totalAmount },
        ]}
        onClose={() => setConfirmSend(false)}
        onConfirm={async () => { await run('send'); setConfirmSend(false); }}
      />
      <ConfirmActionDialog
        open={confirmAccept}
        busy={act.isPending}
        title={tca('qtAccept.title')}
        warning={tca('qtAccept.warning')}
        party={d.customerName}
        rows={[
          { label: t('vat'), value: d.vatAmount, muted: true },
          { label: t('total'), value: d.totalAmount },
        ]}
        onClose={() => setConfirmAccept(false)}
        onConfirm={async () => { await run('accept'); setConfirmAccept(false); }}
      />
    </>
  );
}
