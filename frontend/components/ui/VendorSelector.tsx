'use client';

import { useEffect, useRef, useState } from 'react';
import { apiGet, qs } from '@/lib/api';

// Mirrors CustomerSelector (component-patterns.md §6): async combobox, 300ms
// debounce, defensive shape parsing (backend list shape not contractually pinned).
interface VendorLite {
  vendorId: number;
  nameTh: string;
  taxId?: string | null;
  vendorCode?: string;
}

function pickItems(raw: unknown): VendorLite[] {
  const arr =
    Array.isArray(raw) ? raw
    : raw && typeof raw === 'object' && Array.isArray((raw as { items?: unknown }).items)
      ? (raw as { items: unknown[] }).items
      : [];
  return arr
    .map((x) => x as Record<string, unknown>)
    .filter((x) => typeof x.vendorId === 'number' && typeof x.nameTh === 'string')
    .map((x) => ({
      vendorId: x.vendorId as number,
      nameTh: x.nameTh as string,
      taxId: (x.taxId as string | null | undefined) ?? null,
      vendorCode: (x.vendorCode as string | undefined) ?? undefined,
    }));
}

export function VendorSelector({
  value,
  onChange,
}: {
  value: number | null;
  onChange: (id: number, label: string) => void;
}) {
  const [term, setTerm] = useState('');
  const [open, setOpen] = useState(false);
  const [items, setItems] = useState<VendorLite[]>([]);
  const [loading, setLoading] = useState(false);
  const [selectedLabel, setSelectedLabel] = useState('');
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    if (timer.current) clearTimeout(timer.current);
    if (term.trim().length < 1) {
      setItems([]);
      return;
    }
    timer.current = setTimeout(async () => {
      setLoading(true);
      try {
        const raw = await apiGet<unknown>(`vendors${qs({ search: term, pageSize: 10 })}`);
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
    <div className="form-control relative">
      <span className="label-text">ผู้ขาย / Vendor *</span>
      <input
        className="input input-bordered"
        placeholder="ค้นหาชื่อ หรือรหัสผู้ขาย"
        value={open ? term : selectedLabel || (value ? `#${value}` : '')}
        onFocus={() => setOpen(true)}
        onChange={(e) => {
          setTerm(e.target.value);
          setOpen(true);
        }}
        onBlur={() => setTimeout(() => setOpen(false), 150)}
        aria-expanded={open}
        role="combobox"
        aria-controls="vendor-listbox"
      />
      {open && term.trim().length >= 1 && (
        <ul
          id="vendor-listbox"
          role="listbox"
          className="menu absolute top-full z-10 mt-1 max-h-60 w-full overflow-auto rounded-box bg-base-100 shadow"
        >
          {loading && <li className="px-3 py-2 text-sm text-base-content/50">กำลังค้นหา…</li>}
          {!loading && items.length === 0 && (
            <li className="px-3 py-2 text-sm text-base-content/50">
              ไม่พบผู้ขาย — สร้างใหม่ในเมนูผู้ขาย
            </li>
          )}
          {items.map((v) => (
            <li key={v.vendorId}>
              <button
                type="button"
                onMouseDown={(e) => {
                  e.preventDefault();
                  const label = `${v.nameTh}${v.taxId ? ` (${v.taxId})` : ''}`;
                  setSelectedLabel(label);
                  setOpen(false);
                  setTerm('');
                  onChange(v.vendorId, label);
                }}
              >
                <span>{v.nameTh}</span>
                {v.vendorCode && (
                  <span className="ml-auto font-mono text-xs opacity-60">{v.vendorCode}</span>
                )}
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
