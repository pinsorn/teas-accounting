'use client';

import { AlertTriangle } from 'lucide-react';
import { useTranslations } from 'next-intl';
import { formatTHB } from '@/lib/utils';

// component-patterns.md §9 — irreversible-post warning + summary preview + recipients.
export function PostConfirmDialog({
  open,
  onClose,
  onConfirm,
  busy,
  summary,
  recipients,
}: {
  open: boolean;
  onClose: () => void;
  onConfirm: () => void;
  busy: boolean;
  summary: { customer: string; total: number; vat: number };
  recipients: string[];
}) {
  const t = useTranslations('ti.postConfirm');
  if (!open) return null;

  return (
    <div className="modal modal-open" role="dialog" aria-modal="true">
      <div className="modal-box">
        <h3 className="flex items-center gap-2 text-lg font-bold text-warning">
          <AlertTriangle className="h-5 w-5" aria-hidden />
          {t('title')}
        </h3>

        <div role="alert" className="alert alert-warning mt-3 text-sm">
          {t('warning')}
        </div>

        <dl className="mt-4 space-y-1 text-sm">
          <div className="flex justify-between">
            <dt className="text-base-content/60">{summary.customer}</dt>
          </div>
          <div className="flex justify-between">
            <dt>VAT</dt>
            <dd className="tabular-nums">{formatTHB(summary.vat)}</dd>
          </div>
          <div className="flex justify-between font-bold">
            <dt>Total</dt>
            <dd className="tabular-nums">{formatTHB(summary.total)}</dd>
          </div>
        </dl>

        {recipients.length > 0 && (
          <div className="mt-3 text-xs text-base-content/60">
            {t('recipients')}: {recipients.join(', ')}
          </div>
        )}

        <div className="modal-action">
          <button className="btn btn-ghost" onClick={onClose} disabled={busy}>
            {t('cancel')}
          </button>
          <button className="btn btn-warning" onClick={onConfirm} disabled={busy}>
            {busy && <span className="loading loading-spinner loading-sm" />}
            {t('confirm')}
          </button>
        </div>
      </div>
      <button className="modal-backdrop" onClick={onClose} aria-label="close" />
    </div>
  );
}
