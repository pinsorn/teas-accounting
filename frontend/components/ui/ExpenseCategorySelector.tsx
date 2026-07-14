'use client';

import { useEffect, useState } from 'react';
import { apiGet } from '@/lib/api';
import type { ExpenseCategoryLite } from '@/lib/types';
// PV doc number embeds the category code (MM-YYYY-PV-{CATEGORY}-NNNN, plan §17.3),
// so category is mandatory at PV creation. Non-recoverable VAT (ENT/VEHI, ม.82/5)
// shows ⚠ "ภาษีซื้อต้องห้าม"; capex shows the asset hint (informational only).
// `pick()` (defensive shape parse) lives in lib/expense-category-shape.ts (dependency-free)
// so WP1.5c's null-account detection is unit-testable under this repo's vitest setup, which
// has no `@/` alias resolution or DOM env.
import { pick } from '@/lib/expense-category-shape';

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
        data-testid="expense-category-select"
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
          <option
            key={c.categoryId}
            value={c.categoryId}
            disabled={c.defaultExpenseAccountId == null}
          >
            {c.nameTh} ({c.categoryCode})
            {!c.defaultIsRecoverableVat ? ' ⚠' : ''}
            {c.defaultExpenseAccountId == null ? ' — ยังไม่ผูกบัญชี' : ''}
          </option>
        ))}
      </select>
      {selected && !selected.defaultIsRecoverableVat && (
        <span className="label-text-alt text-warning">
          ⚠ ภาษีซื้อต้องห้าม — VAT นี้เครดิตไม่ได้ (ม.82/5)
        </span>
      )}
      {selected && selected.isCapex && (
        <span className="label-text-alt text-info">บันทึกเป็นสินทรัพย์ (CapEx)</span>
      )}
      {/* WP1.5c (F20) — some categories are savable at settings level but have no default GL
          account (default_expense_account_id NULL) — unusable on a document line (would 422
          vi/pv.expense_account_missing at save). Disabled above (no 422 mid-form). The
          /settings/expense-categories page is read-only (reference data, no CRUD) so this only
          links there to show WHICH categories need a mapping — not a self-service fix. */}
      {cats.some((c) => c.defaultExpenseAccountId == null) && (
        <span className="label-text-alt">
          บางหมวดยังไม่ผูกบัญชี GL — ดูรายการที่{' '}
          <a href="/settings/expense-categories" target="_blank" rel="noreferrer"
             className="link link-primary">
            ตั้งค่า &gt; หมวดค่าใช้จ่าย
          </a>
          {' '}(ติดต่อผู้ดูแลระบบเพื่อผูกบัญชี)
        </span>
      )}
    </div>
  );
}
