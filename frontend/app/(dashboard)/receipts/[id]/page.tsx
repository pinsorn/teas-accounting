'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { useParams, useRouter } from 'next/navigation';
import { Pencil } from 'lucide-react';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { PageHeader } from '@/components/ui/PageHeader';
import { DocActionBar } from '@/components/ui/DocActionBar';
import { PrintMenu } from '@/components/ui/PrintMenu';
import { BusinessUnitBadge } from '@/components/ui/BusinessUnitBadge';
import { PaperDocument } from '@/components/paper/PaperDocument';
import { ActivityLog } from '@/components/doc/ActivityLog';
import { DocumentChain } from '@/components/doc/DocumentChain';
import { ReceiptWhtCertSection } from '@/components/doc/ReceiptWhtCertSection';
import {
  useReceipt, useCompanyProfile, usePaperDoc, usePostReceipt, useCancelReceipt,
  useCancelAndReissueReceipt, useReissueReceipt, useDiscardReceiptReplacement,
} from '@/lib/queries';
import { CancelDocumentModal, CancelledBanner, ReplacementBanner, useCancelErrorToast } from '@/components/documents/CancelDocumentModal';
import { useConfirm } from '@/hooks/useConfirm';
import { formatTHB } from '@/lib/utils';
import { paperDtoToProps } from '@/lib/paper-doc-config';
import { AttachmentsSection } from '@/components/attachments/AttachmentsSection';
import { useHasScope } from '@/components/PermissionGate';
import { AgentPendingBadge } from '@/components/ui/AgentPendingBadge';
import { problemToast } from '@/lib/api';

export default function ReceiptDetailPage() {
  const id = Number(useParams<{ id: string }>().id);
  const tr = useTranslations('rc');
  const tc = useTranslations('common');
  const tw = useTranslations('rc.wht');
  const ta = useTranslations('approve');
  const { data: d, isLoading, isError } = useReceipt(id);
  // cont.121 — paper preview data comes from the canonical /paper DTO (screen ==
  // print); the company profile stays ONLY as the logo source (not in the DTO).
  const company = useCompanyProfile();
  const paper = usePaperDoc('receipts', id);
  const post = usePostReceipt();
  const hasScope = useHasScope();
  const router = useRouter();
  const tcd = useTranslations('cancelDoc');
  const confirm = useConfirm();
  const cancelErr = useCancelErrorToast();
  const cancel = useCancelReceipt();
  const cancelReissue = useCancelAndReissueReceipt();
  const reissue = useReissueReceipt();
  const discard = useDiscardReceiptReplacement();
  const [cancelMode, setCancelMode] = useState<'cancel' | 'reissue' | null>(null);
  const [isApproveAction, setIsApproveAction] = useState(false);

  useEffect(() => {
    const action = new URLSearchParams(window.location.search).get('action');
    if (action === 'approve') setIsApproveAction(true);
  }, []);

  // Shared post handler — reused by the ?action=approve banner CTA and the normal
  // DocActionBar Post CTA (B8: a human-saved Draft must be postable without the
  // agent-approval deep-link).
  async function doPost() {
    try {
      await post.mutateAsync(id);
      toast.success(tc('posted'));
    } catch (e) {
      problemToast(e, tc('error'));
    }
  }

  const canCancel = hasScope('sales.receipt.cancel');
  const canReissue = canCancel && hasScope('sales.receipt.create');

  async function doReissue() {
    try {
      const r = await reissue.mutateAsync(id);
      router.push(`/receipts/${r.replacementReceiptId}/edit`);
    } catch (e) { cancelErr(e); }
  }
  async function doDiscard() {
    if (!(await confirm({ description: tcd('confirmDiscard'), variant: 'destructive' }))) return;
    try {
      await discard.mutateAsync(id);
      toast.success(tc('save'));
      if (d?.replacesId) router.push(`/receipts/${d.replacesId}`);
    } catch (e) { cancelErr(e); }
  }
  async function doCancel(code: string, reason: string) {
    try {
      if (cancelMode === 'reissue') {
        const r = await cancelReissue.mutateAsync({ id, reasonCode: code, reason });
        setCancelMode(null);
        if (r.replacementReceiptId) router.push(`/receipts/${r.replacementReceiptId}/edit`);
      } else {
        await cancel.mutateAsync({ id, reasonCode: code, reason });
        setCancelMode(null);
        toast.success(tc('save'));
      }
    } catch (e) { cancelErr(e); }
  }

  if (isLoading || paper.isLoading) return <p className="text-base-content/50">{tc('loading')}</p>;
  if (isError || !d || !paper.data) return <p className="text-error">{tc('error')}</p>;

  const extraMeta = (
    <>
      <dt>{tr('method')}</dt>
      <dd>{d.paymentMethod}{d.chequeNo ? ` (${d.chequeNo})` : ''}</dd>
      {d.whtAmount > 0 && (
        <>
          <dt>{tw('amount')}</dt>
          <dd>({formatTHB(d.whtAmount)})</dd>
          <dt>{tw('cashReceived')}</dt>
          <dd>{formatTHB(d.cashReceived)}</dd>
        </>
      )}
    </>
  );

  return (
    <>
      <PageHeader
        title={tr('title')}
        subtitle={d.docNo ?? undefined}
        actions={<PrintMenu docType="receipts" id={id} fiscal />}
      />

      {/* B3 — agent-draft badge on normal navigation (independent of ?action=approve). */}
      {d.status === 'Draft' && d.createdViaApiKey && (
        <div className="mb-4"><AgentPendingBadge /></div>
      )}

      {/* S10 — BU wasn't visible anywhere on the RC detail page though the API carries
          the code (ReceiptDetail has no numeric businessUnitId, code only). */}
      <div className="mb-4"><BusinessUnitBadge businessUnitId={null} code={d.businessUnitCode} /></div>

      {/* ?action=approve — prominent approval banner for agent-created drafts */}
      {isApproveAction && d.status === 'Draft' && (
        <div className="mb-4 flex items-center justify-between gap-4 rounded-lg border border-warning bg-warning/10 p-4">
          <div>
            <p className="font-semibold text-warning-content">{ta('bannerTitle')}</p>
            <p className="mt-1 text-sm text-base-content/80">{ta('bannerDesc')}</p>
          </div>
          <div className="shrink-0">
            {hasScope('sales.receipt.post') ? (
              <button
                data-testid="rc-approve-cta"
                className="btn btn-warning btn-sm"
                disabled={post.isPending}
                onClick={doPost}
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
        <CancelledBanner prefix="rc" base="/receipts" d={d} canReissue={canReissue} busy={reissue.isPending} onReissue={doReissue} />
      )}
      {d.replacesId && d.status === 'Draft' && (
        <ReplacementBanner prefix="rc" base="/receipts" d={d} draft>
          {canCancel && (
            <button data-testid="rc-discard-replacement" className="btn btn-ghost btn-sm text-error" disabled={discard.isPending} onClick={doDiscard}>
              {tcd('discardReplacement')}
            </button>
          )}
        </ReplacementBanner>
      )}
      {d.replacesId && d.status === 'Posted' && <ReplacementBanner prefix="rc" base="/receipts" d={d} draft={false} />}

      <DocActionBar
        status={d.status}
        docNo={d.docNo ?? `#${d.receiptId}`}
        actions={
          // B8 — a human-saved Draft receipt must be postable via normal
          // navigation, not only the agent ?action=approve deep-link.
          // Post is hidden while the ?action=approve banner is up — one post CTA at a time;
          // Edit stays visible in both layouts (a human may fix an agent draft before approving).
          <>
            {d.status === 'Draft' && hasScope('sales.receipt.create') && (
              <Link data-testid="rc-edit" href={`/receipts/${id}/edit`} className="btn btn-secondary btn-sm gap-1">
                <Pencil className="h-4 w-4" aria-hidden /> {tc('edit')}
              </Link>
            )}
            {d.status === 'Posted' && canCancel && (
              <button data-testid="rc-cancel" className="btn btn-danger btn-sm" onClick={() => setCancelMode('cancel')}>
                {tcd('cancel')}
              </button>
            )}
            {d.status === 'Posted' && canReissue && (
              <button data-testid="rc-cancel-reissue" className="btn btn-danger btn-sm" onClick={() => setCancelMode('reissue')}>
                {tcd('cancelReissue')}
              </button>
            )}
            {d.status === 'Draft' && !isApproveAction && hasScope('sales.receipt.post') && (
              <button
                data-testid="rc-post-action"
                className="btn btn-primary btn-sm"
                disabled={post.isPending}
                onClick={doPost}
              >
                {tr('post')}
              </button>
            )}
          </>
        }
      />

      <div className="detail-grid">
        <div className="paper-wrap">
          <PaperDocument
            {...paperDtoToProps(paper.data, { logo: company.data?.logoUrl })}
            extraMetaBlock={extraMeta}
          />
        </div>
        <div className="detail-side">
          {d.whtLines && d.whtLines.length > 0 && (
            <div className="rounded-lg border border-base-300 p-3">
              <h3 className="mb-2 text-sm font-semibold">{tw('title')}</h3>
              {/* overflow-x-auto: narrow 2-col side column (~320px) is thinner than the
                  table's min-content — scroll inside the card instead of spilling out. */}
              <div className="overflow-x-auto">
              <table className="table table-sm">
                <thead><tr>
                  <th>{tw('type')}</th>
                  <th className="text-right">{tw('rate')}</th>
                  <th className="text-right">{tw('base')}</th>
                  <th className="text-right">{tw('amount')}</th>
                </tr></thead>
                <tbody>
                  {d.whtLines.map((w) => (
                    <tr key={w.whtTypeId}>
                      <td>{w.whtTypeCode}</td>
                      <td className="text-right tabular-nums">{(w.whtRate * 100).toFixed(2)}%</td>
                      <td className="text-right tabular-nums">{formatTHB(w.baseAmount)}</td>
                      <td className="text-right tabular-nums">{formatTHB(w.whtAmount)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
              </div>
            </div>
          )}
          <DocumentChain type="receipt" id={id} />
          <ActivityLog docType="receipts" id={id} />
        </div>
      </div>

      <ReceiptWhtCertSection
        receiptId={id}
        whtAmount={d.whtAmount}
        certNo={d.customerWhtCertNo}
        certDate={d.customerWhtCertDate}
      />

      <AttachmentsSection parentType="RECEIPT" parentId={id} />

      {cancelMode && (
        <CancelDocumentModal
          kind="receipt"
          mode={cancelMode}
          docNo={d.docNo ?? `#${id}`}
          busy={cancel.isPending || cancelReissue.isPending}
          onClose={() => setCancelMode(null)}
          onConfirm={doCancel}
        />
      )}
    </>
  );
}
