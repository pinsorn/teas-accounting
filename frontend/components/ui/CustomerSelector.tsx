'use client';

import { useEffect, useRef, useState } from 'react';
import { apiGet, qs } from '@/lib/api';
import { FloatingListbox } from '@/components/ui/FloatingListbox';

// component-patterns.md §6 — async combobox, search name/taxId, 300ms debounce,
// "Add new" hint when no match. Degrades to manual id entry if the list API shape
// is unexpected (defensive — backend ListAsync shape not contractually pinned yet).
interface CustomerLite {
  customerId: number;
  nameTh: string;
  taxId?: string | null;
}

function pickItems(raw: unknown): CustomerLite[] {
  const arr =
    Array.isArray(raw) ? raw
    : raw && typeof raw === 'object' && Array.isArray((raw as { items?: unknown }).items)
      ? (raw as { items: unknown[] }).items
      : [];
  return arr
    .map((x) => x as Record<string, unknown>)
    .filter((x) => typeof x.customerId === 'number' && typeof x.nameTh === 'string')
    .map((x) => ({
      customerId: x.customerId as number,
      nameTh: x.nameTh as string,
      taxId: (x.taxId as string | null | undefined) ?? null,
    }));
}

export function CustomerSelector({
  value,
  onChange,
}: {
  value: number | null;
  onChange: (id: number, label: string) => void;
}) {
  const [term, setTerm] = useState('');
  const [open, setOpen] = useState(false);
  const [items, setItems] = useState<CustomerLite[]>([]);
  const [loading, setLoading] = useState(false);
  const [selectedLabel, setSelectedLabel] = useState('');
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  // Sprint 13i B3 (SR5) — lookup-on-mount. When `value` is prefilled
  // programmatically (e.g. TI created from a Quotation) the picker has no
  // cached label and used to render the raw db id ("#5"). Resolve the id to a
  // display label via GET /customers/{id}, matching TaxInvoicePicker's pattern.
  useEffect(() => {
    if (!value) {
      setSelectedLabel('');
      return;
    }
    if (selectedLabel) return;
    let cancelled = false;
    void (async () => {
      try {
        const raw = await apiGet<Record<string, unknown>>(`customers/${value}`);
        if (cancelled) return;
        const name =
          (raw.nameTh as string | undefined) ??
          (raw.name as string | undefined) ??
          `#${value}`;
        const taxId = (raw.taxId as string | null | undefined) ?? null;
        setSelectedLabel(`${name}${taxId ? ` (${taxId})` : ''}`);
      } catch {
        // keep the "#id" fallback if the lookup fails
      }
    })();
    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [value]);

  useEffect(() => {
    if (timer.current) clearTimeout(timer.current);
    if (term.trim().length < 1) {
      setItems([]);
      return;
    }
    timer.current = setTimeout(async () => {
      setLoading(true);
      try {
        const raw = await apiGet<unknown>(`customers${qs({ search: term, pageSize: 10 })}`);
        setItems(pickItems(raw));
      } catch {
        setItems([]);
      } finally {
        setLoading(false);
      }
    }, 300);
    return () => {
      if (timer.current) clearTimeout(timer.current);
    };
  }, [term]);

  return (
    <div className="form-control">
      <span className="label-text">ลูกค้า / Customer *</span>
      <input
        ref={inputRef}
        className="input input-bordered"
        placeholder="ค้นหาชื่อ หรือเลขผู้เสียภาษี"
        value={open ? term : selectedLabel || (value ? `#${value}` : '')}
        onFocus={() => setOpen(true)}
        onChange={(e) => {
          setTerm(e.target.value);
          setOpen(true);
        }}
        onBlur={() => setTimeout(() => setOpen(false), 150)}
        aria-expanded={open}
        role="combobox"
        aria-controls="customer-listbox"
      />
      <FloatingListbox
        anchorRef={inputRef}
        open={open && term.trim().length >= 1}
        listboxId="customer-listbox"
      >
        {loading && <li className="px-3 py-2 text-sm text-base-content/50">กำลังค้นหา…</li>}
        {!loading && items.length === 0 && (
          <li className="px-3 py-2 text-sm text-base-content/50">
            ไม่พบลูกค้า — สร้างใหม่ในเมนูลูกค้า
          </li>
        )}
        {items.map((c) => (
          <li key={c.customerId}>
            <button
              type="button"
              onMouseDown={(e) => {
                e.preventDefault();
                const label = `${c.nameTh}${c.taxId ? ` (${c.taxId})` : ''}`;
                setSelectedLabel(label);
                setOpen(false);
                setTerm('');
                onChange(c.customerId, label);
              }}
            >
              <span>{c.nameTh}</span>
              {c.taxId && <span className="ml-auto font-mono text-xs opacity-60">{c.taxId}</span>}
            </button>
          </li>
        ))}
      </FloatingListbox>
    </div>
  );
}
