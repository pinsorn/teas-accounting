'use client';

import { useEffect, useState, type ReactNode } from 'react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { PageHeader } from '@/components/ui/PageHeader';
import { PermissionGate, useHasScope } from '@/components/PermissionGate';
import { useJournal, useJournalPost, useMePermissions } from '@/lib/queries';
import { formatTHB, formatDate } from '@/lib/utils';
import { problemToast } from '@/lib/api';

export default function JournalDetailPage() {
  const id = Number(useParams<{ id: string }>().id);
  const t = useTranslations('je');
  const tc = useTranslations('common');
  const ta = useTranslations('approve');
  // F1 (specs/fix-pnd36-payment-detection.md §3.7) — 'jv' namespace, mirrors ManualJournalForm.tsx.
  const tJv = useTranslations('jv');
  const { data: d, isLoading } = useJournal(id);
  const post = useJournalPost();
  const hasScope = useHasScope();
  // hasScope returns false while /me/permissions is still in flight — without this flag the
  // banner flashes "no permission" at a user who HAS it (seen live on the v1.27.0 smoke).
  const permsLoaded = useMePermissions().data != null;
  // B3 — ?action=approve deep-link (mirrors purchase-orders/[id]/page.tsx:54,79): the MCP
  // tool's ApprovalUrl points here so a human reviewing an agent-drafted JV sees a prominent
  // banner instead of the plain read-only view.
  const [isApproveAction, setIsApproveAction] = useState(false);
  useEffect(() => {
    const action = new URLSearchParams(window.location.search).get('action');
    if (action === 'approve') setIsApproveAction(true);
  }, []);

  async function doPost() {
    if (!window.confirm(t('confirmPost'))) return;
    try {
      const res = await post.mutateAsync(id);
      toast.success(tc('save'));
      // R3/F1 Tier-2 remediation (FIX 5) — this is the human-post half of the MCP-drafted path
      // (create_manual_journal_draft never posts; §2 C8); mirrors ManualJournalForm.tsx's toast.
      // Non-blocking — the post already succeeded above, never a blocking modal.
      if (res.advisoryCode === 'pnd36.ap_cleared_outside_pv') {
        toast.warning(tJv('pnd36AdvisoryToast'));
      }
    }
    catch (e) { problemToast(e, tc('error')); }
  }

  if (isLoading) return <p className="text-base-content/50">{tc('loading')}</p>;
  // 404 (not found OR other-tenant, identical per BE) — same handling as other doc detail pages.
  if (!d) return <p className="text-base-content/50">{tc('notFound')}</p>;

  return (
    <>
      <PageHeader title={t('title')} subtitle={d.docNo ?? undefined} />

      {/* ?action=approve — prominent post banner for agent-created drafts (B3) */}
      {isApproveAction && d.status === 'Draft' && (
        <div className="mb-4 flex items-center justify-between gap-4 rounded-lg border border-warning bg-warning/10 p-4">
          <div>
            <p className="font-semibold text-warning-content">{ta('bannerTitle')}</p>
            <p className="mt-1 text-sm text-base-content/80">{ta('bannerDesc')}</p>
          </div>
          <div className="shrink-0">
            {hasScope('gl.journal.post') ? (
              <button
                data-testid="je-post-cta"
                className="btn btn-warning btn-sm"
                disabled={post.isPending}
                onClick={doPost}
              >
                {t('post')}
              </button>
            ) : permsLoaded ? (
              <p className="text-sm font-medium text-error">{ta('noPermission')}</p>
            ) : null}
          </div>
        </div>
      )}
      {isApproveAction && d.status !== 'Draft' && (
        <div className="mb-4 rounded-lg border border-base-300 bg-base-200 p-3 text-sm text-base-content/60">
          {ta('alreadyPosted')}
        </div>
      )}

      {/* Hidden while the ?action=approve banner is up — one post CTA at a time. */}
      {d.status === 'Draft' && !isApproveAction && (
        <div className="mb-4">
          <PermissionGate scope="gl.journal.post">
            <button data-testid="je-post" className="btn btn-success btn-sm"
              disabled={post.isPending} onClick={doPost}>{t('post')}</button>
          </PermissionGate>
        </div>
      )}

      <div className="card mb-4 bg-base-100 shadow-sm">
        <div className="card-body grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
          <Field label={t('docDate')} value={formatDate(d.docDate)} />
          <Field label={t('postingDate')} value={formatDate(d.postingDate)} />
          <Field label={t('status')}>
            <span className={`badge ${d.status === 'Posted' ? 'badge-success' : 'badge-ghost'}`}>
              {d.status}
            </span>
          </Field>
          <Field label={t('description')} value={d.description} />
          <Field label={t('reference')} value={d.reference ?? '—'} />
          <Field label={t('postedAt')} value={d.postedAt ? formatDate(d.postedAt) : '—'} />
          {d.reversalOfId != null && (
            <Field label={t('reversalOf')}>
              <Link href={`/journals/${d.reversalOfId}`} className="link link-primary">
                #{d.reversalOfId}
              </Link>
            </Field>
          )}
        </div>
      </div>

      <div className="overflow-x-auto rounded-lg border border-base-300">
        <table className="table table-zebra">
          <thead>
            <tr>
              <th>{t('account')}</th>
              <th>{t('description')}</th>
              <th className="text-right">{t('debit')}</th>
              <th className="text-right">{t('credit')}</th>
            </tr>
          </thead>
          <tbody>
            {d.lines.map((l) => (
              <tr key={l.lineNo}>
                <td><span className="font-mono">{l.accountCode}</span> {l.accountNameTh}</td>
                <td>{l.description ?? d.description}</td>
                <td className="text-right tabular-nums">{l.debit ? formatTHB(l.debit) : ''}</td>
                <td className="text-right tabular-nums">{l.credit ? formatTHB(l.credit) : ''}</td>
              </tr>
            ))}
          </tbody>
          <tfoot>
            <tr className="font-bold">
              <td colSpan={2} className="text-right">{t('totalRow')}</td>
              <td className="text-right tabular-nums">{formatTHB(d.totalDebit)}</td>
              <td className="text-right tabular-nums">{formatTHB(d.totalCredit)}</td>
            </tr>
          </tfoot>
        </table>
      </div>
    </>
  );
}

function Field({ label, value, children }: { label: string; value?: string; children?: ReactNode }) {
  return (
    <div>
      <div className="text-xs text-base-content/60">{label}</div>
      <div className="font-medium">{children ?? value}</div>
    </div>
  );
}
