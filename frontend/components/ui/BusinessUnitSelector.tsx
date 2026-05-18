'use client';

import { useTranslations } from 'next-intl';
import { useBusinessUnits } from '@/lib/queries';

// Sprint 8 — revenue-stream tag dropdown. Active BUs only. Optional unless the
// company opted in (requiresBusinessUnit) — caller passes `required`.
export function BusinessUnitSelector({
  value,
  onChange,
  required = false,
}: {
  value: number | null;
  onChange: (id: number | null) => void;
  required?: boolean;
}) {
  const t = useTranslations('businessUnit');
  const { data: units = [], isLoading } = useBusinessUnits();

  return (
    <label className="form-control">
      <span className="label-text">
        {t('title')}{required ? ' *' : ''}
      </span>
      <select
        className="select select-bordered"
        value={value ?? ''}
        disabled={isLoading}
        aria-label={t('title')}
        onChange={(e) => onChange(e.target.value ? Number(e.target.value) : null)}
      >
        <option value="">{required ? `— ${t('required')} —` : `— ${t('none')} —`}</option>
        {units.map((u) => (
          <option key={u.businessUnitId} value={u.businessUnitId}>
            {u.code} — {u.nameTh}
          </option>
        ))}
      </select>
    </label>
  );
}
