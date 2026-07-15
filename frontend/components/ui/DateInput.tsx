'use client';

import { bangkokToday, formatDateBE } from '@/lib/utils';

// component-patterns.md §5 — Asia/Bangkok, default today, LOCKED for Tax Invoice
// doc_date (server is authoritative; user never types it — CLAUDE.md §10, §0 DO-NOT list).
export function DateInput({
  value,
  locked = true,
  label,
  lockedHint,
}: {
  value?: string;
  locked?: boolean;
  label?: string;
  // R2 (PO edit, 2026-07-15) — override the default "locked to today" hint for a caller
  // whose locked value ISN'T today (e.g. a stored/preserved date). Optional, backward
  // compatible: every other call site keeps the original text unchanged.
  lockedHint?: string;
}) {
  const v = value ?? bangkokToday();
  return (
    <label className="form-control">
      {label && <span className="label-text">{label}</span>}
      <input
        type="date"
        className="input input-bordered"
        value={v}
        readOnly={locked}
        disabled={locked}
        aria-label={label ?? 'date'}
      />
      <span className="label-text-alt text-base-content/50">
        {locked
          ? `${lockedHint ?? 'ล็อกเป็นวันนี้ (Asia/Bangkok)'} · = ${formatDateBE(v)}`
          : `= ${formatDateBE(v)}`}
      </span>
    </label>
  );
}
