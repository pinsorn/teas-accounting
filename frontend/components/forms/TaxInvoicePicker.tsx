'use client';

import { useEffect, useRef, useState } from 'react';
import { apiGet, qs } from '@/lib/api';
import { formatTHB } from '@/lib/utils';
import { FloatingListbox } from '@/components/ui/FloatingListbox';

// Sprint 13e P3 — async combobox over Tax Invoices. Replaces the raw numeric
// taxInvoiceId inputs in RC / CN / DN. Search by doc_no or customer name;
// preview row shows doc_no · customer · date · total so the user picks the
// right TI instead of copying an internal id from another tab.
//
// Backend: GET /tax-invoices supports search / customerId / status / unpaid.
// Degrades to an empty list if the API shape is unexpected (same defensive
// posture as CustomerSelector).
export interface TaxInvoiceLite {
  taxInvoiceId: number;
  docNo: string | null;
  docDate: string;
  customerName: string;
  totalAmount: number;
  currencyCode: string;
  paymentStatus: string;
}

function pickItems(raw: unknown): TaxInvoiceLite[] {
  const arr =
    Array.isArray(raw) ? raw
    : raw && typeof raw === 'object' && Array.isArray((raw as { items?: unknown }).items)
      ? (raw as { items: unknown[] }).items
      : [];
  return arr
    .map((x) => x as Record<string, unknown>)
    .filter((x) => typeof x.taxInvoiceId === 'number' && typeof x.customerName === 'string')
    .map((x) => ({
      taxInvoiceId: x.taxInvoiceId as number,
      docNo: (x.docNo as string | null | undefined) ?? null,
      docDate: String(x.docDate ?? ''),
      customerName: x.customerName as string,
      totalAmount: typeof x.totalAmount === 'number' ? (x.totalAmount as number) : 0,
      currencyCode: (x.currencyCode as string | undefined) ?? 'THB',
      paymentStatus: (x.paymentStatus as string | undefined) ?? '',
    }));
}

export function TaxInvoicePicker({
  value,
  onChange,
  customerId = null,
  status = null,
  unpaid = false,
  disabled = false,
  disabledHint = 'เลือกลูกค้าก่อน',
  ariaLabel = 'อ้างอิงใบกำกับภาษี',
}: {
  value: number | null;
  onChange: (ti: TaxInvoiceLite) => void;
  /** Scope results to one customer (RC: only that customer's TIs). */
  customerId?: number | null;
  /** e.g. 'Posted' — CN/DN may only reference a posted TI. */
  status?: string | null;
  /** RC: only TIs with a remaining balance. */
  unpaid?: boolean;
  disabled?: boolean;
  disabledHint?: string;
  ariaLabel?: string;
}) {
  const [term, setTerm] = useState('');
  const [open, setOpen] = useState(false);
  const [items, setItems] = useState<TaxInvoiceLite[]>([]);
  const [loading, setLoading] = useState(false);
  const [selectedLabel, setSelectedLabel] = useState('');
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  // Sprint 13h P2 — hydrate selectedLabel when the parent passes a non-null
  // `value` without a fresh pick (e.g. editing a Draft RC). Avoids the
  // "#1" fallback display.
  useEffect(() => {
    if (!value || selectedLabel) return;
    let cancelled = false;
    (async () => {
      try {
        const raw = (await apiGet<unknown>(`tax-invoices/${value}`)) as Record<string, unknown>;
        if (cancelled) return;
        const docNo = (raw.docNo as string | null | undefined) ?? null;
        const customerName = (raw.customerName as string | undefined) ?? '';
        setSelectedLabel(docNo ? `${docNo} · ${customerName}` : customerName);
      } catch {
        /* leave selectedLabel empty; placeholder will show */
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
        const raw = await apiGet<unknown>(
          `tax-invoices${qs({
            search: term.trim() || undefined,
            customerId: customerId ?? undefined,
            status: status ?? undefined,
            unpaid: unpaid || undefined,
            limit: 10,
          })}`,
        );
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
  }, [term, open, disabled, customerId, status, unpaid]);

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
        aria-controls="taxinvoice-listbox"
      />
      {disabled && (
        <span className="mt-1 text-xs text-base-content/50">{disabledHint}</span>
      )}
      <FloatingListbox
        anchorRef={inputRef}
        open={open && !disabled}
        listboxId="taxinvoice-listbox"
      >
        {loading && <li className="px-3 py-2 text-sm text-base-content/50">กำลังค้นหา…</li>}
        {!loading && items.length === 0 && (
          <li className="px-3 py-2 text-sm text-base-content/50">ไม่พบใบกำกับภาษี</li>
        )}
        {items.map((ti) => (
          <li key={ti.taxInvoiceId}>
            <button
              type="button"
              onMouseDown={(e) => {
                e.preventDefault();
                const label = `${ti.docNo ?? `(ร่าง #${ti.taxInvoiceId})`} · ${ti.customerName}`;
                setSelectedLabel(label);
                setOpen(false);
                setTerm('');
                onChange(ti);
              }}
            >
              <div className="flex w-full flex-col items-start">
                <span className="font-mono text-sm">{ti.docNo ?? `(ร่าง #${ti.taxInvoiceId})`}</span>
                <span className="flex w-full justify-between text-xs opacity-70">
                  <span>{ti.customerName}</span>
                  <span className="tabular-nums">
                    {ti.docDate} · {formatTHB(ti.totalAmount)}
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
