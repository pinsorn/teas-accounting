'use client';

import { useTranslations } from 'next-intl';
import type { ProductTypeStr } from '@/lib/types';

// purchase-completeness Phase 3 — per-line สินค้า/บริการ (goods/service) selector
// for the PV + VI line editors. v1 exposes GOOD/SERVICE (the two common cases);
// the exempt variants are accepted by the backend but not surfaced here yet.
export const PRODUCT_TYPE_OPTIONS: ProductTypeStr[] = ['GOOD', 'SERVICE'];

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
        className="select select-bordered select-sm"
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
