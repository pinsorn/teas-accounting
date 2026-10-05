'use client';

import { useState, type ReactNode } from 'react';
import Link from 'next/link';
import { AlertTriangle } from 'lucide-react';
import { useTranslations } from 'next-intl';
import { reasonsFor, type CancelKind, type CancelMode } from '@/lib/cancel-reasons';
import { apiErrorToast, parseApiError } from '@/lib/api/errors';
import { toast } from 'sonner';

// cancel-reissue-sales-docs §3.9 — one modal for TI / receipt / invoice(BN) cancel and
// cancel-and-reissue. DaisyUI .modal-box. Reason code + free text are
// both required; the BE re-validates (cancel.reason_code_invalid / validation.reason_required).
export function CancelDocumentModal({
  kind, mode, docNo, pnd30Filed, busy, onClose, onConfirm,
}: {
  kind: CancelKind;
  mode: CancelMode;
  docNo: string;
  pnd30Filed?: boolean;
  busy?: boolean;
  onClose: () => void;
  onConfirm: (code: string, reason: string) => void | Promise<void>;
}) {
  const t = useTranslations('cancelDoc');
  const tc = useTranslations('common');
  const [code, setCode] = useState('');
  const [reason, setReason] = useState('');

  const warnings: string[] = [];
  if (mode === 'reissue') warnings.push(t('warnReissueLocked'));
  else if (kind === 'tax-invoice') {
    warnings.push(t('warnBadDebt'), t('warnValueChange'), t('warnManualJv'));
    if (pnd30Filed) warnings.push(t('warnPnd30Filed'));
  }
  if (kind === 'receipt' && mode === 'cancel') warnings.push(t('warnReceiptUnwind'));

  return (
    <div className="modal modal-open" data-testid="cancel-modal">
      <div className="modal-box">
        <h3 className="flex items-center gap-2 text-lg font-bold text-error">
          <AlertTriangle className="h-5 w-5" aria-hidden />
          {t(mode === 'reissue' ? 'modalTitleReissue' : 'modalTitleCancel')} {docNo}
        </h3>
        {warnings.map((w) => (
          <div key={w} role="alert" className="alert alert-warning mt-3 text-sm">{w}</div>
        ))}
        <label className="form-control mt-4">
          <span className="label-text text-xs">{t('reasonCode')}</span>
          <select
            data-testid="cancel-reason-code"
            className="select select-bordered select-sm"
            value={code}
            onChange={(e) => setCode(e.target.value)}
          >
            <option value="">—</option>
            {reasonsFor(kind, mode).map((c) => (
              <option key={c} value={c}>{t(`reasons.${c}`)}</option>
            ))}
          </select>
        </label>
        <label className="form-control mt-3">
          <span className="label-text text-xs">{t('reasonText')}</span>
          <textarea
            data-testid="cancel-reason-text"
            className="textarea textarea-bordered"
            rows={3}
            maxLength={500}
            value={reason}
            onChange={(e) => setReason(e.target.value)}
          />
          {!reason.trim() && <span className="mt-1 text-xs text-base-content/50">{t('reasonRequired')}</span>}
        </label>
        <div className="modal-action">
          <button className="btn btn-ghost btn-sm" disabled={busy} onClick={onClose}>{tc('cancel')}</button>
          <button
            data-testid="cancel-confirm"
            className="btn btn-error btn-sm"
            disabled={busy || !code || !reason.trim()}
            onClick={() => onConfirm(code, reason.trim())}
          >
            {tc('confirm')}
          </button>
        </div>
      </div>
    </div>
  );
}

/** Toast a cancel-flow failure: prefer the localized `cancelDoc.errors.<code>` text, else the
 *  shared Thai-by-code / backend-detail path. */
export function useCancelErrorToast() {
  const t = useTranslations('cancelDoc');
  return (e: unknown) => {
    const code = parseApiError(e).code;
    if (code && t.has(`errors.${code}`)) toast.error(t(`errors.${code}`), { duration: 8000 });
    else apiErrorToast(e);
  };
}

type CancelMeta = {
  cancelReasonCode?: string | null; cancelReason?: string | null; reversalJournalDocNo?: string | null;
  replacedById?: number | null; replacedByDocNo?: string | null; replacedByStatus?: string | null;
};

/** Voided TI/receipt: danger banner (reason label + text + reversal JV) + link to the replacement,
 *  or a reissue button when there is none (§3.9). `prefix` = test-id prefix ('ti' | 'rc'). */
export function CancelledBanner({ prefix, base, d, canReissue, busy, onReissue }: {
  prefix: 'ti' | 'rc'; base: string; d: CancelMeta;
  canReissue: boolean; busy?: boolean; onReissue: () => void;
}) {
  const t = useTranslations('cancelDoc');
  return (
    <div className="mb-4 rounded-lg border border-error bg-error/10 p-4 text-sm" data-testid={`${prefix}-cancelled-banner`}>
      <p className="font-semibold text-error">
        {t('bannerCancelled')}{d.cancelReasonCode ? ` — ${t.has(`reasons.${d.cancelReasonCode}`) ? t(`reasons.${d.cancelReasonCode}`) : d.cancelReasonCode}` : ''}
      </p>
      {d.cancelReason && <p className="mt-1 text-base-content/80">{d.cancelReason}</p>}
      {d.reversalJournalDocNo && <p className="mt-1 text-base-content/60">{t('bannerReversal', { jv: d.reversalJournalDocNo })}</p>}
      {d.replacedById ? (
        <p className="mt-2">
          <Link data-testid={`${prefix}-replacement-link`} href={`${base}/${d.replacedById}`} className="link link-primary">
            {t('bannerReplacedBy', { docNo: d.replacedByDocNo ?? `#${d.replacedById}` })}
          </Link>
        </p>
      ) : canReissue && (
        <button data-testid={`${prefix}-reissue`} className="btn btn-primary btn-sm mt-2" disabled={busy} onClick={onReissue}>
          {t('reissue')}
        </button>
      )}
    </div>
  );
}

/** Replacement (ใบแทน) draft/posted: info banner linking the original; amount lock is server-enforced (O7). */
export function ReplacementBanner({ prefix, base, d, draft, children }: {
  prefix: 'ti' | 'rc'; base: string; draft: boolean;
  d: { replacesId?: number | null; replacesDocNo?: string | null }; children?: ReactNode;
}) {
  const t = useTranslations('cancelDoc');
  const orig = d.replacesDocNo ?? `#${d.replacesId}`;
  return (
    <div className="mb-4 flex flex-wrap items-center justify-between gap-3 rounded-lg border border-info bg-info/10 p-3 text-sm" data-testid={`${prefix}-replacement-banner`}>
      <span>
        {draft ? t('bannerReplacementDraft', { docNo: orig }) : (
          <Link href={`${base}/${d.replacesId}`} className="link link-primary">{t('bannerReplaces', { docNo: orig })}</Link>
        )}
        {draft && <Link href={`${base}/${d.replacesId}`} className="link link-primary ml-2">{t('viewOriginal')}</Link>}
      </span>
      {children}
    </div>
  );
}
