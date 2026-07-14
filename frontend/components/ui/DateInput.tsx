'use client';

import { bangkokToday, formatDateBE } from '@/lib/utils';

// component-patterns.md §5 — Asia/Bangkok, default today, LOCKED for Tax Invoice
// doc_date (server is authoritative; user never types it — CLAUDE.md §10, §0 DO-NOT list).
export function DateInput({
  value,
  locked = true,
  label,
}: {
  value?: string;
  locked?: boolean;
  label?: string;
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
        {locked ? `ล็อกเป็นวันนี้ (Asia/Bangkok) · = ${formatDateBE(v)}` : `= ${formatDateBE(v)}`}
      </span>
    </label>
  );
}
