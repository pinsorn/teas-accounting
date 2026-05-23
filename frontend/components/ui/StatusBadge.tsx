'use client';

import { useTranslations } from 'next-intl';

// component-patterns.md §2 — status conveyed via text (+ dot), never colour only.
// Sprint 13j-FE — repalette to warm peach/ink tokens (design/claude-design),
// add `withEn` ("ตอบรับแล้ว · Accepted") + `dot` prefix. Status keys are the
// REAL spec-driven keys (PascalCase) — §0a Gold Standard, mockup is visual only.
// All existing call sites (`<StatusBadge status="Posted" />`) stay back-compat.

type Tone = 'success' | 'warning' | 'danger' | 'info' | 'draft';

const TONE_CLS: Record<Tone, string> = {
  success: 'bg-status-success-bg text-status-success',
  warning: 'bg-status-warning-bg text-status-warning',
  danger: 'bg-status-danger-bg text-status-danger',
  info: 'bg-status-info-bg text-status-info',
  draft: 'bg-status-draft-bg text-status-draft',
};

const MAP: Record<string, { tone: Tone; en: string }> = {
  Draft: { tone: 'draft', en: 'Draft' },
  Approved: { tone: 'info', en: 'Approved' },
  Posted: { tone: 'success', en: 'Posted' },
  Voided: { tone: 'danger', en: 'Voided' },
  PAID: { tone: 'success', en: 'Paid' },
  PARTIAL: { tone: 'warning', en: 'Partial' },
  UNPAID: { tone: 'draft', en: 'Unpaid' },
  Sent: { tone: 'info', en: 'Sent' },
  Accepted: { tone: 'success', en: 'Accepted' },
  Rejected: { tone: 'danger', en: 'Rejected' },
  Expired: { tone: 'warning', en: 'Expired' },
  Cancelled: { tone: 'danger', en: 'Cancelled' },
  Closed: { tone: 'success', en: 'Closed' },
  Issued: { tone: 'info', en: 'Issued' },
  Delivered: { tone: 'success', en: 'Delivered' },
  Settled: { tone: 'success', en: 'Settled' },
};

export function StatusBadge({
  status,
  withEn = false,
  dot = true,
}: {
  status: string;
  withEn?: boolean;
  dot?: boolean;
}) {
  const t = useTranslations('status');
  const cfg = MAP[status] ?? { tone: 'draft' as Tone, en: status };
  const label = (() => {
    try {
      return t(status);
    } catch {
      return status;
    }
  })();
  return (
    <span
      className={`inline-flex items-center gap-1.5 whitespace-nowrap rounded-full px-2.5 py-0.5 text-xs font-semibold ${TONE_CLS[cfg.tone]}`}
    >
      {dot && <span className="h-1.5 w-1.5 rounded-full bg-current" aria-hidden />}
      {label}
      {withEn && cfg.en ? ` · ${cfg.en}` : ''}
    </span>
  );
}
