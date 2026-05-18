'use client';

import { useEffect, useState } from 'react';
import { apiGet } from '@/lib/api';
import type { ExpenseCategoryLite } from '@/lib/types';

// PV doc number embeds the category code (MM-YYYY-PV-{CATEGORY}-NNNN, plan §17.3),
// so category is mandatory at PV creation. Non-recoverable VAT (ENT/VEHI, ม.82/5)
// shows ⚠ "ภาษีซื้อต้องห้าม"; capex shows the asset hint (informational only).
// Defensive shape parse — list endpoint contract not pinned.
function pick(raw: unknown): ExpenseCategoryLite[] {
  const arr =
    Array.isArray(raw) ? raw
    : raw && typeof raw === 'object' && Array.isArray((raw as { items?: unknown }).items)
      ? (raw as { items: unknown[] }).items
      : [];
  return arr
    .map((x) => x as Record<string, unknown>)
    .filter((x) => typeof x.categoryId === 'number')
    .map((x) => ({
      categoryId: x.categoryId as number,
      categoryCode: String(x.categoryCode ?? ''),
      nameTh: String(x.nameTh ?? x.categoryCode ?? ''),
      isRecoverableVat: x.isRecoverableVat !== false,
      isCapex: x.isCapex === true,
    }));
}

export function ExpenseCategorySelector({
  value,
  onChange,
}: {
  value: number | null;
  onChange: (id: number, cat: ExpenseCategoryLite) => void;
}) {
  const [cats, setCats] = useState<ExpenseCategoryLite[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let alive = true;
    (async () => {
      try {
        const raw = await apiGet<unknown>('expense-categories');
        if (alive) setCats(pick(raw));
      } catch {
        if (alive) setCats([]);
      } finally {
        if (alive) setLoading(false);
      }
    })();
    return () => {
      alive = false;
    };
  }, []);

  const selected = cats.find((c) => c.categoryId === value) ?? null;

  return (
    <div className="form-control">
      <span className="label-text">หมวดค่าใช้จ่าย / Expense Category *</span>
      <select
        className="select select-bordered"
        value={value ?? ''}
        disabled={loading}
        onChange={(e) => {
          const id = Number(e.target.value);
          const c = cats.find((x) => x.categoryId === id);
          if (c) onChange(id, c);
        }}
      >
        <option value="" disabled>
          {loading ? 'กำลังโหลด…' : '— เลือกหมวด —'}
        </option>
        {cats.map((c) => (
          <option key={c.categoryId} value={c.categoryId}>
            {c.nameTh} ({c.categoryCode}){!c.isRecoverableVat ? ' ⚠' : ''}
          </option>
        ))}
      </select>
      {selected && !selected.isRecoverableVat && (
        <span className="label-text-alt text-warning">
          ⚠ ภาษีซื้อต้องห้าม — VAT นี้เครดิตไม่ได้ (ม.82/5)
        </span>
      )}
      {selected && selected.isCapex && (
        <span className="label-text-alt text-info">บันทึกเป็นสินทรัพย์ (CapEx)</span>
      )}
    </div>
  );
}
