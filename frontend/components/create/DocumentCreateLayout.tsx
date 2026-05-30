'use client';

import type { ReactNode } from 'react';

// Create-page redesign (cont.80) — the shared 2-column create-page shell.
//   • a top header row: page title (+ optional doc meta "เลขที่ …") on the left,
//     the action buttons (ยกเลิก / บันทึกร่าง / primary) on the right;
//   • a left FORM column (`children`) — a stack of SectionCards;
//   • a right LIVE PREVIEW column (`preview`), sticky on large screens.
// Grid: form minmax(440px,500px) + preview 1fr, gap-6, collapses to 1 column on
// small. Action buttons live in the header (outside any <form>) — pages that
// still need form submit semantics must wire their buttons via handleSubmit /
// the `form=` attribute (the pilot does this).
export function DocumentCreateLayout({
  title,
  docMeta,
  actions,
  children,
  preview,
}: {
  title: string;
  /** Optional doc meta shown under the title (e.g. "เลขที่ …"). */
  docMeta?: ReactNode;
  /** The action buttons row (ยกเลิก / บันทึกร่าง / primary). */
  actions?: ReactNode;
  /** The left form column — typically a stack of <SectionCard>. */
  children: ReactNode;
  /** The right preview column — typically a <LivePreviewPane>. */
  preview: ReactNode;
}) {
  return (
    <div>
      <div className="mb-6 flex flex-wrap items-start justify-between gap-4 border-b border-ink-100 pb-4">
        <div>
          <h1 className="text-2xl font-bold text-ink-900">{title}</h1>
          {docMeta != null && <div className="mt-1 text-sm text-ink-500">{docMeta}</div>}
        </div>
        {actions != null && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
      </div>

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-[3fr_2fr]">
        <div className="min-w-0 space-y-6">{children}</div>
        <div className="min-w-0 lg:sticky lg:top-4 lg:self-start">{preview}</div>
      </div>
    </div>
  );
}
