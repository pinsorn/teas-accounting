'use client';

import { useTranslations } from 'next-intl';
import type { ProductTypeStr } from '@/lib/types';

// purchase-completeness Phase 3 — per-line สินค้า/บริการ (goods/service) selector
// for the PV + VI line editors. Tier-2 F1 (fix-po-vi-vat-derivation, GPT-5.6 review) — all
// four backend taxonomy codes must be selectable: a PO-linked VI/PV row can arrive with
// productType EXEMPT_GOOD/EXEMPT_SERVICE (PurchaseOrderService.LineProductType infers it for a
// productId-null line with TaxRate 0), and with only GOOD/SERVICE as options the controlled
// <select> silently DISPLAYED 'GOOD' (first-option fallback) while the row state + save payload
// still carried the real EXEMPT_* value underneath — a UI/state desync, not a data bug.
export const PRODUCT_TYPE_OPTIONS: ProductTypeStr[] = ['GOOD', 'SERVICE', 'EXEMPT_GOOD', 'EXEMPT_SERVICE'];

export function ProductTypeSelect({
  value,
  onChange,
  testId,
}: {
  value: ProductTypeStr;
  onChange: (v: ProductTypeStr) => void;
  testId?: string;
}) {
  const t = useTranslations('productType');
  return (
    <label className="form-control">
      <span className="label-text">{t('label')}</span>
      <select
        className="select select-bordered"
        data-testid={testId}
        value={value}
        onChange={(e) => onChange(e.target.value as ProductTypeStr)}
        aria-label={t('label')}
      >
        {PRODUCT_TYPE_OPTIONS.map((o) => (
          <option key={o} value={o}>{t(o)}</option>
        ))}
      </select>
    </label>
  );
}
