'use client';

import { useRef, useState } from 'react';
import { FloatingListbox } from '@/components/ui/FloatingListbox';
import { useWhtTypes } from '@/lib/queries';
import type { WhtTypeListItem } from '@/lib/types';

// Sprint 13j-tail — WHT type picker. Replaces the native DaisyUI <select> so the
// dropdown is theme-consistent with the other comboboxes (CustomerSelector etc.)
// and not clipped by overflow on the form grid. The in-force list is small and
// already cached by useWhtTypes, so no async search — just an anchored listbox.
// Only in-force types (effectiveTo === null) are selectable.
export function WhtTypeSelect({
  value,
  onChange,
  ariaLabel,
  placeholder = '—',
  className = '',
}: {
  value: number | null;
  onChange: (id: number | null, wt: WhtTypeListItem | null) => void;
  ariaLabel?: string;
  placeholder?: string;
  /** Extra classes on the trigger button — e.g. `select-sm` to match input-sm neighbours. */
  className?: string;
}) {
  const [open, setOpen] = useState(false);
  const btnRef = useRef<HTMLButtonElement>(null);
  const types = (useWhtTypes().data ?? []).filter((w) => w.effectiveTo === null);
  const selected = types.find((w) => w.whtTypeId === value) ?? null;
  const label = selected
    ? `${selected.code} — ${selected.nameTh} (${(selected.rate * 100).toFixed(2)}%)`
    : placeholder;

  function choose(id: number | null, wt: WhtTypeListItem | null) {
    onChange(id, wt);
    setOpen(false);
  }

  return (
    <>
      <button
        ref={btnRef}
        type="button"
        className={`select select-bordered flex w-full items-center text-left font-normal ${className}`}
        aria-label={ariaLabel}
        aria-expanded={open}
        aria-haspopup="listbox"
        role="combobox"
        onClick={() => setOpen((o) => !o)}
        onBlur={() => setTimeout(() => setOpen(false), 150)}
      >
        <span className={`block min-w-0 flex-1 truncate whitespace-nowrap ${selected ? '' : 'opacity-50'}`}>
          {label}
        </span>
      </button>
      <FloatingListbox anchorRef={btnRef} open={open} listboxId="wht-type-listbox">
        <li>
          <button
            type="button"
            onMouseDown={(e) => {
              e.preventDefault();
              choose(null, null);
            }}
          >
            <span className="opacity-60">{placeholder}</span>
          </button>
        </li>
        {types.map((w) => (
          <li key={w.whtTypeId}>
            <button
              type="button"
              onMouseDown={(e) => {
                e.preventDefault();
                choose(w.whtTypeId, w);
              }}
            >
              <span>
                {w.code} — {w.nameTh}
              </span>
              <span className="ml-auto font-mono text-xs opacity-60">
                {(w.rate * 100).toFixed(2)}%
              </span>
            </button>
          </li>
        ))}
      </FloatingListbox>
    </>
  );
}
