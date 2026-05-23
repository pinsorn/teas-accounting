'use client';

import { useEffect, useRef, useState } from 'react';
import { apiGet, qs } from '@/lib/api';
import { formatTHB } from '@/lib/utils';
import { FloatingListbox } from '@/components/ui/FloatingListbox';

// Non-VAT receipt apply-to-DO (cont. 68) — async combobox over Delivery Orders,
// mirroring TaxInvoicePicker. A non-VAT company issues no Tax Invoice (ม.86/4), so a
// credit receipt settles against the delivery document (ใบส่งของ) instead. Only
// post-issue DOs (Issued / Delivered) that haven't been combined into a TI are
// billable; results are scoped to the selected customer.
//
// Backend: GET /delivery-orders?status=… returns DeliveryOrderListItem (now incl.
// customerId + totalAmount). We fetch issued + delivered and filter client-side
// (the endpoint takes a single status). Degrades to an empty list defensively.
export interface DeliveryOrderLite {
  deliveryOrderId: number;
  docNo: string | null;
  docDate: string;
  customerId: number;
  customerName: string;
  status: string;
  totalAmount: number;
  isCombinedWithTi: boolean;
}

function pickItems(raw: unknown): DeliveryOrderLite[] {
  const arr = Array.isArray(raw)
    ? raw
    : raw && typeof raw === 'object' && Array.isArray((raw as { items?: unknown }).items)
      ? (raw as { items: unknown[] }).items
      : [];
  return arr
    .map((x) => x as Record<string, unknown>)
    .filter((x) => typeof x.deliveryOrderId === 'number')
    .map((x) => ({
      deliveryOrderId: x.deliveryOrderId as number,
      docNo: (x.docNo as string | null | undefined) ?? null,
      docDate: String(x.docDate ?? ''),
      customerId: typeof x.customerId === 'number' ? (x.customerId as number) : 0,
      customerName: (x.customerName as string | undefined) ?? '',
      status: (x.status as string | undefined) ?? '',
      totalAmount: typeof x.totalAmount === 'number' ? (x.totalAmount as number) : 0,
      isCombinedWithTi: x.isCombinedWithTi === true,
    }));
}

export function DeliveryOrderPicker({
  value,
  onChange,
  customerId = null,
  disabled = false,
  disabledHint = 'เลือกลูกค้าก่อน',
  ariaLabel = 'อ้างอิงใบส่งของ',
}: {
  value: number | null;
  onChange: (d: DeliveryOrderLite) => void;
  /** Scope results to one customer (the receipt's customer). */
  customerId?: number | null;
  disabled?: boolean;
  disabledHint?: string;
  ariaLabel?: string;
}) {
  const [term, setTerm] = useState('');
  const [open, setOpen] = useState(false);
  const [items, setItems] = useState<DeliveryOrderLite[]>([]);
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
        const raw = (await apiGet<unknown>(`delivery-orders/${value}`)) as Record<string, unknown>;
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
        // Fetch both billable states (single-status endpoint → two calls), then
        // client-filter: exclude TI-combined (VAT path) and other customers.
        const [issued, delivered] = await Promise.all([
          apiGet<unknown>(`delivery-orders${qs({ status: 'Issued' })}`),
          apiGet<unknown>(`delivery-orders${qs({ status: 'Delivered' })}`),
        ]);
        const t = term.trim().toLowerCase();
        const merged = [...pickItems(issued), ...pickItems(delivered)]
          .filter((d) => !d.isCombinedWithTi)
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
        aria-controls="deliveryorder-listbox"
      />
      {disabled && <span className="mt-1 text-xs text-base-content/50">{disabledHint}</span>}
      <FloatingListbox anchorRef={inputRef} open={open && !disabled} listboxId="deliveryorder-listbox">
        {loading && <li className="px-3 py-2 text-sm text-base-content/50">กำลังค้นหา…</li>}
        {!loading && items.length === 0 && (
          <li className="px-3 py-2 text-sm text-base-content/50">ไม่พบใบส่งของ</li>
        )}
        {items.map((d) => (
          <li key={d.deliveryOrderId}>
            <button
              type="button"
              onMouseDown={(e) => {
                e.preventDefault();
                const label = `${d.docNo ?? `(ร่าง #${d.deliveryOrderId})`} · ${d.customerName}`;
                setSelectedLabel(label);
                setOpen(false);
                setTerm('');
                onChange(d);
              }}
            >
              <div className="flex w-full flex-col items-start">
                <span className="font-mono text-sm">{d.docNo ?? `(ร่าง #${d.deliveryOrderId})`}</span>
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
