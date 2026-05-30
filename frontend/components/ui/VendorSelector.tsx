'use client';

import { useEffect, useState } from 'react';
import { ChevronDown } from 'lucide-react';
import { apiGet } from '@/lib/api';
import { EntityPickerModal, type PickerRow } from '@/components/ui/EntityPickerModal';

// ITEM 2+3 — VendorSelector is now a trigger button that opens a shared search
// MODAL (EntityPickerModal), replacing the old inline typeahead. Public props are
// unchanged (`value` / `onChange`) so every call-site keeps working; an optional
// `label` overrides/suppresses the canonical built-in label (null/"" = no label,
// e.g. filter contexts — no required "*"). The lookup-on-mount effect is preserved
// so a programmatically-prefilled `value` shows the vendor name, not "#id".

const DEFAULT_LABEL = 'ผู้ขาย / Vendor *';

function mapRow(x: Record<string, unknown>): PickerRow | null {
  if (typeof x.vendorId !== 'number' || typeof x.nameTh !== 'string') return null;
  const taxId = (x.taxId as string | null | undefined) ?? null;
  const code = (x.vendorCode as string | undefined) ?? undefined;
  return {
    id: x.vendorId,
    label: x.nameTh,
    sub: code ?? taxId ?? null,
  };
}

export function VendorSelector({
  value,
  onChange,
  label,
}: {
  value: number | null;
  onChange: (id: number, label: string) => void;
  /** Override the built-in label. `null`/"" renders no label (e.g. filters). */
  label?: string | null;
}) {
  const [open, setOpen] = useState(false);
  const [selectedLabel, setSelectedLabel] = useState('');

  // Resolve a prefilled `value` → display label via GET vendors/{id} (TI-from-PO
  // / PO→PV prefill set the id programmatically and have no cached label).
  useEffect(() => {
    if (!value) {
      setSelectedLabel('');
      return;
    }
    if (selectedLabel) return;
    let cancelled = false;
    void (async () => {
      try {
        const raw = await apiGet<Record<string, unknown>>(`vendors/${value}`);
        if (cancelled) return;
        const name =
          (raw.nameTh as string | undefined) ?? (raw.name as string | undefined) ?? `#${value}`;
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

  const showLabel = label !== null && label !== '';
  const display = selectedLabel || (value ? `#${value}` : '');

  return (
    <div className="form-control">
      {showLabel && <span className="label-text">{label ?? DEFAULT_LABEL}</span>}
      <button
        type="button"
        className="input input-bordered flex items-center justify-between text-left font-normal"
        onClick={() => setOpen(true)}
        aria-haspopup="dialog"
        aria-expanded={open}
      >
        <span className={display ? 'truncate' : 'truncate text-base-content/40'}>
          {display || 'ค้นหาชื่อ หรือรหัสผู้ขาย'}
        </span>
        <ChevronDown className="ml-2 h-4 w-4 shrink-0 opacity-50" aria-hidden />
      </button>
      <EntityPickerModal
        open={open}
        onClose={() => setOpen(false)}
        onSelect={(id, name) => {
          setSelectedLabel(name);
          onChange(id, name);
        }}
        title="เลือกผู้ขาย / Select Vendor"
        searchPlaceholder="ค้นหาชื่อ หรือรหัสผู้ขาย"
        emptyText="ไม่พบผู้ขาย — สร้างใหม่ในเมนูผู้ขาย"
        endpoint="vendors"
        mapRow={mapRow}
      />
    </div>
  );
}
