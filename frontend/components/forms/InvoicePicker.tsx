'use client';

import { useEffect, useRef, useState } from 'react';
import { apiGet, qs } from '@/lib/api';
import { formatTHB } from '@/lib/utils';
import { FloatingListbox } from '@/components/ui/FloatingListbox';

// Phase 2a — non-VAT receipt apply-to-Invoice. Async combobox over Invoices
// (ใบแจ้งหนี้; BE BillingNote, route/api still `billing-notes`). The new flow is
// DO → Invoice → Receipt, so a non-VAT credit receipt settles an issued Invoice
// instead of a Delivery Order. Only Issued invoices are billable; results are
// scoped to the selected customer. Mirrors DeliveryOrderPicker.
//
// Backend: GET /billing-notes?status=Issued returns BillingNoteListItem
// (incl. customerId + totalAmount). Degrades to an empty list defensively.
export interface BillingNoteLite {
  billingNoteId: number;
  docNo: string | null;
  docDate: string;
  customerId: number;
  customerName: string;
  status: string;
  totalAmount: number;
}

function pickItems(raw: unknown): BillingNoteLite[] {
  const arr = Array.isArray(raw)
    ? raw
    : raw && typeof raw === 'object' && Array.isArray((raw as { items?: unknown }).items)
      ? (raw as { items: unknown[] }).items
      : [];
  return arr
    .map((x) => x as Record<string, unknown>)
    .filter((x) => typeof x.billingNoteId === 'number')
    .map((x) => ({
      billingNoteId: x.billingNoteId as number,
      docNo: (x.docNo as string | null | undefined) ?? null,
      docDate: String(x.docDate ?? ''),
      customerId: typeof x.customerId === 'number' ? (x.customerId as number) : 0,
      customerName: (x.customerName as string | undefined) ?? '',
      status: (x.status as string | undefined) ?? '',
      totalAmount: typeof x.totalAmount === 'number' ? (x.totalAmount as number) : 0,
    }));
}

export function InvoicePicker({
  value,
  onChange,
  customerId = null,
  disabled = false,
  disabledHint = 'เลือกลูกค้าก่อน',
  ariaLabel = 'อ้างอิงใบแจ้งหนี้',
}: {
  value: number | null;
  onChange: (d: BillingNoteLite) => void;
  /** Scope results to one customer (the receipt's customer). */
  customerId?: number | null;
  disabled?: boolean;
  disabledHint?: string;
  ariaLabel?: string;
}) {
  const [term, setTerm] = useState('');
  const [open, setOpen] = useState(false);
  const [items, setItems] = useState<BillingNoteLite[]>([]);
  const [loading, setLoading] = useState(false);
  const [selectedLabel, setSelectedLabel] = useState('');
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  // Hydrate the label when a non-null value arrives without a fresh pick.
  useEffect(() => {
    if (!value || selectedLabel) return;
    let cancelled = false;
    (async () => {
      try {
        const raw = (await apiGet<unknown>(`billing-notes/${value}`)) as Record<string, unknown>;
        if (cancelled) return;
        const docNo = (raw.docNo as string | null | undefined) ?? null;
        const customerName = (raw.customerName as string | undefined) ?? '';
        setSelectedLabel(docNo ? `${docNo} · ${customerName}` : customerName);
      } catch {
        /* leave empty; placeholder shows */
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [value, selectedLabel]);

  useEffect(() => {
    if (!open || disabled) return;
    if (timer.current) clearTimeout(timer.current);
    timer.current = setTimeout(async () => {
      setLoading(true);
      try {
        // Only issued invoices are billable; client-filter to the receipt's customer.
        const issued = await apiGet<unknown>(`billing-notes${qs({ status: 'Issued' })}`);
        const t = term.trim().toLowerCase();
        const merged = pickItems(issued)
          .filter((d) => (customerId ? d.customerId === customerId : true))
          .filter((d) =>
            t
              ? (d.docNo ?? '').toLowerCase().includes(t) ||
                d.customerName.toLowerCase().includes(t)
              : true,
          )
          .slice(0, 10);
        setItems(merged);
      } catch {
        setItems([]);
      } finally {
        setLoading(false);
      }
    }, 300);
    return () => {
      if (timer.current) clearTimeout(timer.current);
    };
  }, [term, open, disabled, customerId]);

  return (
    <div className="form-control">
      <input
        ref={inputRef}
        className="input input-bordered"
        placeholder="ค้นหาเลขเอกสาร หรือชื่อลูกค้า"
        disabled={disabled}
        value={open ? term : selectedLabel}
        onFocus={() => !disabled && setOpen(true)}
        onChange={(e) => {
          setTerm(e.target.value);
          setOpen(true);
        }}
        onBlur={() => setTimeout(() => setOpen(false), 150)}
        aria-expanded={open}
        aria-label={ariaLabel}
        role="combobox"
        aria-controls="invoice-listbox"
      />
      {disabled && <span className="mt-1 text-xs text-base-content/50">{disabledHint}</span>}
      <FloatingListbox anchorRef={inputRef} open={open && !disabled} listboxId="invoice-listbox">
        {loading && <li className="px-3 py-2 text-sm text-base-content/50">กำลังค้นหา…</li>}
        {!loading && items.length === 0 && (
          <li className="px-3 py-2 text-sm text-base-content/50">ไม่พบใบแจ้งหนี้</li>
        )}
        {items.map((d) => (
          <li key={d.billingNoteId}>
            <button
              type="button"
              onMouseDown={(e) => {
                e.preventDefault();
                const label = `${d.docNo ?? `(ร่าง #${d.billingNoteId})`} · ${d.customerName}`;
                setSelectedLabel(label);
                setOpen(false);
                setTerm('');
                onChange(d);
              }}
            >
              <div className="flex w-full flex-col items-start">
                <span className="font-mono text-sm">{d.docNo ?? `(ร่าง #${d.billingNoteId})`}</span>
                <span className="flex w-full justify-between text-xs opacity-70">
                  <span>{d.customerName}</span>
                  <span className="tabular-nums">
                    {d.docDate} · {formatTHB(d.totalAmount)}
                  </span>
                </span>
              </div>
            </button>
          </li>
        ))}
      </FloatingListbox>
    </div>
  );
}
