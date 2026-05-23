'use client';

import { useRouter, useSearchParams } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { CustomerSelector } from '@/components/ui/CustomerSelector';
import { BusinessUnitSelector } from '@/components/ui/BusinessUnitSelector';

// Sprint 13j-FE (extends 13i C3) — shared list-page filter bar.
// 5-column responsive grid card, URL-persisted (status/bu/customerId/dateFrom/
// dateTo). `ListFilters` re-exports this. BU + Customer selectors render their
// OWN label, so this bar must NOT add a duplicate label for them; only status +
// the date inputs get a label here. Controls use the default (non-sm) height so
// Thai glyphs aren't clipped, and the grid aligns on `items-end`.
export function FilterBar({
  statusOptions,
  statusTestId,
}: {
  statusOptions?: readonly string[];
  statusTestId?: string;
}) {
  const tc = useTranslations('common');
  const router = useRouter();
  const params = useSearchParams();
  const status = params.get('status') ?? '';
  const bu = params.get('bu') ?? '';
  const customerId = params.get('customerId') ?? '';
  const dateFrom = params.get('dateFrom') ?? '';
  const dateTo = params.get('dateTo') ?? '';

  function setParam(key: string, val: string) {
    const sp = new URLSearchParams(params.toString());
    if (val) sp.set(key, val);
    else sp.delete(key);
    router.replace(sp.toString() ? `?${sp.toString()}` : '?');
  }

  return (
    <div className="mb-4 grid grid-cols-1 items-end gap-3 rounded-card border border-ink-100 bg-base-100 p-4 shadow-warm-sm md:grid-cols-2 lg:grid-cols-5">
      <label className="form-control">
        <span className="label-text text-ink-600">{tc('status')}</span>
        <select
          data-testid={statusTestId}
          className="select select-bordered"
          value={status}
          onChange={(e) => setParam('status', e.target.value)}
          aria-label={tc('status')}
          disabled={!statusOptions}
        >
          <option value="">{tc('all')}</option>
          {statusOptions?.map((s) => (
            <option key={s} value={s}>
              {s}
            </option>
          ))}
        </select>
      </label>

      {/* BusinessUnitSelector + CustomerSelector render their own <label>. */}
      <div data-testid="filter-bu">
        <BusinessUnitSelector value={bu ? Number(bu) : null} onChange={(id) => setParam('bu', id ? String(id) : '')} />
      </div>

      <div data-testid="filter-customer">
        <CustomerSelector
          value={customerId ? Number(customerId) : null}
          onChange={(id) => setParam('customerId', id ? String(id) : '')}
        />
      </div>

      <label className="form-control">
        <span className="label-text text-ink-600">{tc('dateFrom')}</span>
        <input
          type="date"
          data-testid="filter-date-from"
          className="input input-bordered"
          value={dateFrom}
          onChange={(e) => setParam('dateFrom', e.target.value)}
          aria-label={tc('dateFrom')}
        />
      </label>

      <label className="form-control">
        <span className="label-text text-ink-600">{tc('dateTo')}</span>
        <input
          type="date"
          data-testid="filter-date-to"
          className="input input-bordered"
          value={dateTo}
          onChange={(e) => setParam('dateTo', e.target.value)}
          aria-label={tc('dateTo')}
        />
      </label>
    </div>
  );
}
