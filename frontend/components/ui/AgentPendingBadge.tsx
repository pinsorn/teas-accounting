'use client';

import { useTranslations } from 'next-intl';

/**
 * M4b — small inline badge shown on DRAFT list rows that were created via an
 * API key (agent). Rendered alongside the StatusBadge when
 * `status === 'Draft' && createdViaApiKey != null`.
 * Reuses the same span/class shape as StatusBadge (info tone).
 */
export function AgentPendingBadge() {
  const tc = useTranslations('common');
  return (
    <span className="inline-flex items-center gap-1.5 whitespace-nowrap rounded-full px-2.5 py-0.5 text-xs font-semibold bg-status-info-bg text-status-info">
      <span className="h-1.5 w-1.5 rounded-full bg-current" aria-hidden />
      {tc('agentPending')}
    </span>
  );
}
