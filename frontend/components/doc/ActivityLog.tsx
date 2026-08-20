'use client';

import { Clock, FilePlus, Send, Lock, CheckCheck, ArrowRight, Truck, X, Mail, Check } from 'lucide-react';
import { useTranslations } from 'next-intl';
import { useDocumentActivity } from '@/lib/queries';
import { ApiError } from '@/lib/api';
import { formatDate } from '@/lib/utils';
import type { ActivityDocType } from '@/lib/types';

// Sprint 13j-FE D3 — vertical activity timeline for the doc detail side rail.
// Data is pulled from the real BE endpoint (useDocumentActivity); never mocked.
// Renders a graceful empty state until transition logging is wired for sales
// doctypes (currently only ApiKey writes audit.activity_log — see report).

const ICON: Record<string, typeof Clock> = {
  created: FilePlus,
  issued: Send,
  sent: Send,
  posted: Lock,
  accepted: CheckCheck,
  converted: ArrowRight,
  delivered: Truck,
  cancelled: X,
  voided: X,
  emailed: Mail,
};

function iconFor(action: string): typeof Clock {
  const key = action.toLowerCase();
  for (const k of Object.keys(ICON)) {
    const ic = ICON[k];
    if (ic && key.includes(k)) return ic;
  }
  return Check;
}

// S12 (R6-parity) — action and toStatus often localize to the SAME Thai label (e.g.
// action=Sent, toStatus=Sent → "ส่งแล้ว"); showing both then reads as a redundant
// "ส่งแล้ว → ส่งแล้ว". Collapse to the single label whenever the localized strings match.
// Pure (already-localized strings in, no i18n hook) so it's unit-testable in isolation.
export function activityHeadline(actionLabel: string, toStatusLabel: string | null): string {
  if (toStatusLabel && actionLabel !== toStatusLabel) return `${actionLabel} → ${toStatusLabel}`;
  return toStatusLabel ?? actionLabel;
}

export function ActivityLog({ docType, id }: { docType: ActivityDocType; id: number }) {
  const tc = useTranslations('common');
  // WP4 4.3 — activity entries carry raw BE event codes (Created/MarkedSent/Posted/…);
  // map to the Thai label when known, else fall back to the raw code (F11).
  const tAction = useTranslations('common.activityAction');
  const label = (code: string): string => (tAction.has(code) ? tAction(code) : code);
  // R3-FE (ui-document-creation-test-2026-08-20 — Activity panel กลบ 403 เป็น "ยังไม่มี
  // ประวัติกิจกรรม") — `data ?? []` alone can't distinguish "really empty" from "the
  // query errored"; both fell into the same neutral empty state, telling a 403'd normal
  // operator (creator of their OWN doc) that no activity happened at all. isError/error
  // give the real cause; 403 gets its own honest message, any other error a generic one.
  // NEVER let a query error render as the empty-history text.
  const { data, isLoading, isError, error } = useDocumentActivity(docType, id);
  const entries = data ?? [];
  const isForbidden = isError && error instanceof ApiError && error.status === 403;

  return (
    <div className="rounded-card border border-ink-100 bg-base-100 shadow-warm-sm">
      <div className="flex items-center gap-2 border-b border-ink-100 px-5 py-3.5">
        <Clock className="h-4 w-4 text-ink-600" aria-hidden />
        <h3 className="text-[15px] font-bold text-ink-900">{tc('activityLog')}</h3>
      </div>
      <div className="p-5">
        {isLoading ? (
          <div className="flex flex-col gap-3" aria-busy="true" aria-label={tc('loading')}>
            {[1, 2, 3].map((i) => (
              <div key={i} className="flex gap-3">
                <div className="skeleton-shimmer h-7 w-7 shrink-0 rounded-full" />
                <div className="flex flex-1 flex-col gap-1.5 pt-1">
                  <div className="skeleton-shimmer h-3 w-3/4 rounded" />
                  <div className="skeleton-shimmer h-2.5 w-1/2 rounded" />
                </div>
              </div>
            ))}
          </div>
        ) : isForbidden ? (
          <p className="text-[13px] font-medium text-error">{tc('activityNoPermission')}</p>
        ) : isError ? (
          <p className="text-[13px] font-medium text-error">{tc('activityLoadError')}</p>
        ) : entries.length === 0 ? (
          <p className="text-[13px] text-ink-500">{tc('activityEmpty')}</p>
        ) : (
          <div className="flex flex-col gap-3.5">
            {entries
              .slice()
              .reverse()
              .map((e, i) => {
                const Icon = iconFor(e.action);
                const active = i === 0;
                return (
                  <div key={i} className="flex gap-3">
                    <span
                      className={`grid h-7 w-7 shrink-0 place-items-center rounded-full border ${
                        active
                          ? 'border-peach-300 bg-peach-100 text-peach-700'
                          : 'border-ink-100 bg-base-300 text-ink-600'
                      }`}
                    >
                      <Icon className="h-3.5 w-3.5" aria-hidden />
                    </span>
                    <div className="min-w-0">
                      <div className="text-[13.5px] font-semibold text-ink-900">
                        {activityHeadline(label(e.action), e.toStatus ? label(e.toStatus) : null)}
                      </div>
                      <div className="mt-0.5 text-[12px] text-ink-500">
                        {formatDate(e.at)} · {e.actor}
                      </div>
                    </div>
                  </div>
                );
              })}
          </div>
        )}
      </div>
    </div>
  );
}
