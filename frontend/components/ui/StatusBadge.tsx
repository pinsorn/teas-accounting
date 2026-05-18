'use client';

import { Lock, Pencil, X, Check } from 'lucide-react';
import { useTranslations } from 'next-intl';

// component-patterns.md §2 — status conveyed via icon + text, never colour only (a11y).
const MAP: Record<string, { cls: string; Icon: typeof Lock }> = {
  Draft:   { cls: 'badge-ghost',  Icon: Pencil },
  Approved:{ cls: 'badge-info',   Icon: Check },
  Posted:  { cls: 'badge-success', Icon: Lock },
  Voided:  { cls: 'badge-error',  Icon: X },
  PAID:    { cls: 'badge-info',   Icon: Check },
  PARTIAL: { cls: 'badge-warning', Icon: Check },
  UNPAID:  { cls: 'badge-ghost',  Icon: Pencil },
};

export function StatusBadge({ status }: { status: string }) {
  const t = useTranslations('status');
  const cfg = MAP[status] ?? { cls: 'badge-ghost', Icon: Pencil };
  const Icon = cfg.Icon;
  const label = (() => {
    try { return t(status); } catch { return status; }
  })();
  return (
    <span className={`badge ${cfg.cls} gap-1 whitespace-nowrap`}>
      <Icon className="h-3 w-3" aria-hidden />
      {label}
    </span>
  );
}
