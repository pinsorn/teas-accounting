'use client';

import { useState, useEffect } from 'react';
import Link from 'next/link';
import { useParams, useRouter } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { Pencil, ReceiptText } from 'lucide-react';
import { PrintMenu } from '@/components/ui/PrintMenu';
import { toast } from 'sonner';
import { PageHeader } from '@/components/ui/PageHeader';
import { AgentPendingBadge } from '@/components/ui/AgentPendingBadge';
import { PermissionGate, useHasScope } from '@/components/PermissionGate';
import { DocActionBar } from '@/components/ui/DocActionBar';
import { PostConfirmDialog } from '@/components/ui/PostConfirmDialog';
import { PaperDocument } from '@/components/paper/PaperDocument';
import { ActivityLog } from '@/components/doc/ActivityLog';
import { DocumentChain } from '@/components/doc/DocumentChain';
import {
  useTaxInvoice, useSystemInfo, usePostTaxInvoice, usePaperDoc, useCancelTaxInvoice,
  useCancelAndReissueTaxInvoice, useReissueTaxInvoice, useDiscardTaxInvoiceReplacement,
} from '@/lib/queries';
import { CancelDocumentModal, CancelledBanner, ReplacementBanner, useCancelErrorToast } from '@/components/documents/CancelDocumentModal';
import { useConfirm } from '@/hooks/useConfirm';
import { paperDtoToProps } from '@/lib/paper-doc-config';
import { AttachmentsSection } from '@/components/attachments/AttachmentsSection';
import { NonVatGuard } from '@/components/ui/NonVatGuard';
import { problemToast } from '@/lib/api';

export default function TaxInvoiceDetailPage() {
  const params = useParams<{ id: string }>();
  const id = Number(params.id);
  const t = useTranslations('ti.detail');
  const tc = useTranslations('common');
  const ta = useTranslations('approve');
  const { data: d, isLoading, isError } = useTaxInvoice(id);
  // cont.121 — paper preview data comes from the canonical /paper DTO (screen ==
  // print; seller/customer stay the POSTED SNAPSHOT server-side, ม.86/4). The TI
  // paper carries no logo today, so no extras.logo is passed.
  const paper = usePaperDoc('tax-invoices', id);
  const { data: sys } = useSystemInfo();
  const post = usePostTaxInvoice();
  const [confirmPost, setConfirmPost] = useState(false);
  const [isApproveAction, setIsApproveAction] = useState(false);
  const hasScope = useHasScope();
  const router = useRouter();
  const tcd = useTranslations('cancelDoc');
  const confirm = useConfirm();
  const cancelErr = useCancelErrorToast();
  const cancel = useCancelTaxInvoice();
  const cancelReissue = useCancelAndReissueTaxInvoice();
  const reissue = useReissueTaxInvoice();
  const discard = useDiscardTaxInvoiceReplacement();
  const [cancelMode, setCancelMode] = useState<'cancel' | 'reissue' | null>(null);

  useEffect(() => {
    const action = new URLSearchParams(window.location.search).get('action');
    if (action === 'approve') setIsApproveAction(true);
  }, []);

  const canCancel = hasScope('sales.tax_invoice.cancel');
  const canReissue = canCancel && hasScope('sales.tax_invoice.create');

  async function doReissue() {
    try {
      const r = await reissue.mutateAsync(id);
      router.push(`/tax-invoices/${r.replacementTaxInvoiceId}/edit`);
    } catch (e) { cancelErr(e); }
  }
  async function doDiscard() {
    if (!(await confirm({ description: tcd('confirmDiscard'), variant: 'destructive' }))) return;
    try {
      await discard.mutateAsync(id);
      toast.success(tc('save'));
      if (d?.replacesId) router.push(`/tax-invoices/${d.replacesId}`);
    } catch (e) { cancelErr(e); }
  }
  async function doCancel(code: string, reason: string) {
    try {
      if (cancelMode === 'reissue') {
        const r = await cancelReissue.mutateAsync({ id, reasonCode: code, reason });
        setCancelMode(null);
        if (r.replacementTaxInvoiceId) router.push(`/tax-invoices/${r.replacementTaxInvoiceId}/edit`);
      } else {
        await cancel.mutateAsync({ id, reasonCode: code, reason });
        setCancelMode(null);
        toast.success(tc('save'));
      }
    } catch (e) { cancelErr(e); }
  }

  if (!sys?.vatMode && sys !== undefined) return <NonVatGuard title={t('title')} />;
  if (isLoading || paper.isLoading) return <p className="text-base-content/50">{tc('loading')}</p>;
  if (isError || !d || !paper.data) return <p className="text-error">{tc('error')}</p>;

  return (
    <>
      <PageHeader
        title={t('title')}
        subtitle={d.docNo ?? undefined}
        actions={
          <>
            {/* Sprint 13j-tail — issue a Receipt against this posted TI (prefilled). */}
            {d.status === 'Posted' && d.paymentStatus !== 'PAID' && (
              <Link
                href={`/receipts/new?ti=${id}&customer=${d.customerId}&amount=${d.totalAmount}`}
                className="btn btn-primary btn-sm gap-1"
              >
                <ReceiptText className="h-4 w-4" aria-hidden /> {t('createReceipt')}
              </Link>
            )}
            <PrintMenu docType="tax-invoices" id={id} fiscal />
            {/* e-Tax XML download + resend-email buttons REMOVED while the e-Tax
                pipeline is inert Phase-1 scaffolding (signer inert, mock RD client) —
                they rendered on every status incl. Draft and the resend button was a
                permanent no-op. Re-add when e-Tax goes live, gated on
                sys.etaxEnabled (surface ETax:Enabled via /system/info) AND
                d.status === 'Posted'. See plan.md "e-Tax UI tech debt". */}
          </>
        }
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
            {hasScope('sales.tax_invoice.post') ? (
              <button
                data-testid="ti-approve-cta"
                className="btn btn-warning btn-sm"
                disabled={post.isPending}
                onClick={() => setConfirmPost(true)}
              >
                {ta('cta')}
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

      {d.status === 'Voided' && (
        <CancelledBanner prefix="ti" base="/tax-invoices" d={d} canReissue={canReissue} busy={reissue.isPending} onReissue={doReissue} />
      )}
      {d.replacesId && d.status === 'Draft' && (
        <ReplacementBanner prefix="ti" base="/tax-invoices" d={d} draft>
          {canCancel && (
            <button data-testid="ti-discard-replacement" className="btn btn-ghost btn-sm text-error" disabled={discard.isPending} onClick={doDiscard}>
              {tcd('discardReplacement')}
            </button>
          )}
        </ReplacementBanner>
      )}
      {d.replacesId && d.status === 'Posted' && <ReplacementBanner prefix="ti" base="/tax-invoices" d={d} draft={false} />}

      <DocActionBar
        status={d.status}
        docNo={d.docNo ?? `#${d.taxInvoiceId}`}
        actions={
          // A TI created from an Invoice lands as Draft (no number yet). Posting
          // assigns the sequential number + fires e-Tax (§4.3/§4.4) — guarded by
          // PostConfirmDialog so the user reviews buyer tax fields first (ม.86/4 #3).
          // Post is hidden while the ?action=approve banner is up — one post CTA at a time;
          // Edit stays visible in both layouts.
          <>
            {d.status === 'Draft' && hasScope('sales.tax_invoice.create') && (
              <Link data-testid="ti-edit" href={`/tax-invoices/${id}/edit`} className="btn btn-secondary btn-sm gap-1">
                <Pencil className="h-4 w-4" aria-hidden /> {tc('edit')}
              </Link>
            )}
            {d.status === 'Posted' && canCancel && (
              <button data-testid="ti-cancel" className="btn btn-danger btn-sm" onClick={() => setCancelMode('cancel')}>
                {tcd('cancel')}
              </button>
            )}
            {d.status === 'Posted' && canReissue && (
              <button data-testid="ti-cancel-reissue" className="btn btn-danger btn-sm" onClick={() => setCancelMode('reissue')}>
                {tcd('cancelReissue')}
              </button>
            )}
            {d.status === 'Draft' && !isApproveAction && (
              <PermissionGate scope="sales.tax_invoice.post">
                <button
                  data-testid="ti-post-action"
                  className="btn btn-primary btn-sm"
                  disabled={post.isPending}
                  onClick={() => setConfirmPost(true)}
                >
                  {t('post')}
                </button>
              </PermissionGate>
            )}
          </>
        }
      />

      <div className="detail-grid">
        <div className="paper-wrap">
          <PaperDocument {...paperDtoToProps(paper.data)} />
        </div>
        <div className="detail-side">
          <DocumentChain type="tax-invoice" id={id} />
          <ActivityLog docType="tax-invoices" id={id} />
        </div>
      </div>

      <AttachmentsSection parentType="TAX_INVOICE" parentId={id} />

      {cancelMode && (
        <CancelDocumentModal
          kind="tax-invoice"
          mode={cancelMode}
          docNo={d.docNo ?? `#${id}`}
          pnd30Filed={d.pnd30FiledForMonth}
          busy={cancel.isPending || cancelReissue.isPending}
          onClose={() => setCancelMode(null)}
          onConfirm={doCancel}
        />
      )}

      <PostConfirmDialog
        docType="tax_invoice"
        open={confirmPost}
        busy={post.isPending}
        summary={{ customer: d.customerName, total: d.totalAmount, vat: d.taxAmount }}
        recipients={[]}
        onClose={() => setConfirmPost(false)}
        onConfirm={async () => {
          try {
            await post.mutateAsync(id);
            toast.success(tc('posted'));
          } catch (e) {
            problemToast(e, tc('error'));
          } finally {
            setConfirmPost(false);
          }
        }}
      />
    </>
  );
}
