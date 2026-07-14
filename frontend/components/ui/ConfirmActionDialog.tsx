'use client';

import { AlertTriangle } from 'lucide-react';
import { useTranslations } from 'next-intl';
import { formatTHB } from '@/lib/utils';

// WP3 3.6/3.7 — shared confirmation dialog for irreversible/state-changing purchase
// actions (PO approve, PV approve, PV post, PO mark-sent). Mirrors PostConfirmDialog's
// markup/wording style (AlertTriangle title, warning alert box, totals summary, modal
// footer) but is generic over title/warning/rows instead of a fixed docType union, so
// it also covers non-"post" actions (approve, mark-sent) that PostConfirmDialog doesn't
// model. "Simple variant" = omit `party`/`rows` (used by PO mark-sent, 3.7).
export interface ConfirmActionRow {
  label: string;
  value: number;
  muted?: boolean;
}

export function ConfirmActionDialog({
  open,
  onClose,
  onConfirm,
  busy,
  title,
  warning,
  party,
  rows,
  confirmLabel,
}: {
  open: boolean;
  onClose: () => void;
  onConfirm: () => void;
  busy: boolean;
  title: string;
  warning: string;
  /** Optional label-only row (e.g. vendor name) — omit for the simple variant. */
  party?: string;
  /** Optional totals summary rows — omit for the simple variant. */
  rows?: ConfirmActionRow[];
  confirmLabel?: string;
}) {
  const tc = useTranslations('common');
  if (!open) return null;

  return (
    <div className="modal modal-open" role="dialog" aria-modal="true">
      <div className="modal-box">
        <h3 className="flex items-center gap-2 text-lg font-bold text-warning">
          <AlertTriangle className="h-5 w-5" aria-hidden />
          {title}
        </h3>

        <div role="alert" className="alert alert-warning mt-3 text-sm">
          {warning}
        </div>

        {(party || (rows && rows.length > 0)) && (
          <dl className="mt-4 space-y-1 text-sm">
            {party && (
              <div className="flex justify-between">
                <dt className="text-base-content/60">{party}</dt>
              </div>
            )}
            {rows?.map((r, i) => (
              <div key={i} className={`flex justify-between ${r.muted ? '' : 'font-bold'}`}>
                <dt>{r.label}</dt>
                <dd className="tabular-nums">{formatTHB(r.value)}</dd>
              </div>
            ))}
          </dl>
        )}

        <div className="modal-action">
          <button className="btn btn-ghost" onClick={onClose} disabled={busy}>
            {tc('cancel')}
          </button>
          <button className="btn btn-warning" onClick={onConfirm} disabled={busy}>
            {busy && <span className="loading loading-spinner loading-sm" />}
            {confirmLabel ?? tc('confirm')}
          </button>
        </div>
      </div>
      <button className="modal-backdrop" onClick={onClose} aria-label="close" />
    </div>
  );
}
