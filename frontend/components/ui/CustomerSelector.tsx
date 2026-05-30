'use client';

import { useEffect, useState } from 'react';
import { ChevronDown } from 'lucide-react';
import { apiGet } from '@/lib/api';
import { EntityPickerModal, type PickerRow } from '@/components/ui/EntityPickerModal';

// ITEM 2+3 — CustomerSelector is now a trigger button that opens a shared search
// MODAL (EntityPickerModal), replacing the old inline typeahead. Public props are
// unchanged (`value` / `onChange`); an optional `label` overrides/suppresses the
// canonical built-in label (null/"" = no label, e.g. filter contexts). The
// lookup-on-mount effect is preserved so a programmatically-prefilled `value`
// (e.g. TI created from a Quotation) shows the customer name, not "#id".

const DEFAULT_LABEL = 'ลูกค้า / Customer *';

function mapRow(x: Record<string, unknown>): PickerRow | null {
  if (typeof x.customerId !== 'number' || typeof x.nameTh !== 'string') return null;
  const taxId = (x.taxId as string | null | undefined) ?? null;
  return {
    id: x.customerId,
    label: x.nameTh,
    sub: taxId,
  };
}

export function CustomerSelector({
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

  // Resolve a prefilled `value` → display label via GET customers/{id}.
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
          {display || 'ค้นหาชื่อ หรือเลขผู้เสียภาษี'}
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
        title="เลือกลูกค้า / Select Customer"
        searchPlaceholder="ค้นหาชื่อ หรือเลขผู้เสียภาษี"
        emptyText="ไม่พบลูกค้า — สร้างใหม่ในเมนูลูกค้า"
        endpoint="customers"
        mapRow={mapRow}
      />
    </div>
  );
}
