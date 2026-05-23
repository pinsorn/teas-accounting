'use client';

import type { ReactNode } from 'react';
import { StatusBadge } from '@/components/ui/StatusBadge';

// Sprint 13j-FE — document action bar above detail bodies.
// Presentational shell: status + docno on the left, page-supplied action
// buttons on the right. Workflow/state-machine logic stays in each page
// (§0a Gold Standard — DocActionBar must not encode doctype workflow), so
// pages pass their own status-conditional buttons via `actions`.
export function DocActionBar({
  status,
  docNo,
  docNoLabel = 'เลขที่เอกสาร',
  actions,
}: {
  status: string;
  docNo?: string | null;
  docNoLabel?: string;
  actions?: ReactNode;
}) {
  return (
    <div className="mb-[18px] flex flex-wrap items-center gap-4 rounded-card border border-ink-100 bg-base-100 px-[18px] py-3.5 shadow-warm-sm">
      <div className="flex items-center gap-3">
        <StatusBadge status={status} withEn />
      </div>
      <div className="flex flex-col border-l border-ink-100 pl-4">
        <span className="text-[11px] font-semibold uppercase tracking-wide text-ink-500">{docNoLabel}</span>
        <span className="text-[15px] font-bold text-ink-900">{docNo || '—'}</span>
      </div>
      {actions && <div className="ml-auto flex items-center gap-2.5">{actions}</div>}
    </div>
  );
}
