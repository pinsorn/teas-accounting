'use client';

import { useTranslations } from 'next-intl';
import { useBusinessUnits } from '@/lib/queries';

// Sprint 8 — revenue-stream tag dropdown. Active BUs only. Optional unless the
// company opted in (requiresBusinessUnit) — caller passes `required`.
export function BusinessUnitSelector({
  value,
  onChange,
  required = false,
  error = false,
}: {
  value: number | null;
  onChange: (id: number | null) => void;
  required?: boolean;
  // Sprint 13i B4 — when true, render the select in an error state so an empty
  // required BU is highlighted on submit (was a generic toast only — SR9).
  error?: boolean;
}) {
  const t = useTranslations('businessUnit');
  const tt = useTranslations('toast');
  const { data: units = [], isLoading } = useBusinessUnits();

  return (
    <label className="form-control">
      <span className="label-text">
        {t('title')}{required ? ' *' : ''}
      </span>
      <select
        className={`select select-bordered${error ? ' select-error' : ''}`}
        value={value ?? ''}
        disabled={isLoading}
        aria-label={t('title')}
        aria-invalid={error || undefined}
        onChange={(e) => onChange(e.target.value ? Number(e.target.value) : null)}
      >
        <option value="">{required ? `— ${t('required')} —` : `— ${t('none')} —`}</option>
        {units.map((u) => (
          <option key={u.businessUnitId} value={u.businessUnitId}>
            {u.code} — {u.nameTh}
          </option>
        ))}
      </select>
      {error && <span className="text-error text-sm">{tt('requiredField')}</span>}
    </label>
  );
}
