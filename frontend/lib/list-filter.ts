import type { ReadonlyURLSearchParams } from 'next/navigation';

// Sprint 13i C3 — client-side list filtering shared across the 8 sales list pages.
// Reads the same URL params the <ListFilters> bar writes and filters the loaded
// rows. Acceptable for v1 (lists are small); flagged for Sprint 13j server-side
// optimization if any list exceeds ~1000 rows.
export interface ListFilterAccessors<T> {
  status?: (r: T) => string;
  businessUnitId?: (r: T) => number | null;
  customerId?: (r: T) => number;
  docDate?: (r: T) => string; // ISO yyyy-mm-dd — lexical compare is date-correct
}

export function applyListFilters<T>(
  rows: T[],
  params: ReadonlyURLSearchParams | URLSearchParams,
  acc: ListFilterAccessors<T>,
): T[] {
  const status = params.get('status') ?? '';
  const bu = params.get('bu') ?? '';
  const customerId = params.get('customerId') ?? '';
  const dateFrom = params.get('dateFrom') ?? '';
  const dateTo = params.get('dateTo') ?? '';

  return rows.filter((r) => {
    if (status && acc.status && acc.status(r) !== status) return false;
    if (bu && acc.businessUnitId && acc.businessUnitId(r) !== Number(bu)) return false;
    if (customerId && acc.customerId && acc.customerId(r) !== Number(customerId)) return false;
    if (acc.docDate) {
      const d = acc.docDate(r);
      if (dateFrom && d < dateFrom) return false;
      if (dateTo && d > dateTo) return false;
    }
    return true;
  });
}
