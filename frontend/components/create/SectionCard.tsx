'use client';

import type { ReactNode } from 'react';

// Create-page redesign (cont.80) — a numbered section card. A dark filled circle
// (ink-900, ~22px) with a white number, a bold ink title, an optional muted
// right-meta, and a card body holding the section's fields. Generic: every
// create page composes a stack of these. Tokens match the spec
// (card bg-white, rounded-card=14px, border ink-100, shadow-warm-sm).
export function SectionCard({
  number,
  title,
  rightMeta,
  children,
}: {
  number: number;
  title: string;
  rightMeta?: ReactNode;
  children: ReactNode;
}) {
  return (
    <section className="rounded-card border border-ink-100 bg-base-100 p-5 shadow-warm-sm">
      <div className="mb-4 flex items-center gap-3">
        <span
          className="flex h-[22px] w-[22px] shrink-0 items-center justify-center rounded-full bg-ink-900 text-xs font-semibold text-white"
          aria-hidden
        >
          {number}
        </span>
        <h2 className="flex-1 text-[15px] font-bold text-ink-900">{title}</h2>
        {rightMeta != null && (
          <span className="text-sm text-ink-500">{rightMeta}</span>
        )}
      </div>
      {children}
    </section>
  );
}
