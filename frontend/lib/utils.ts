import { clsx, type ClassValue } from 'clsx';
import { twMerge } from 'tailwind-merge';

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs));
}

const THB = new Intl.NumberFormat('th-TH', {
  style: 'currency',
  currency: 'THB',
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});

export function formatTHB(value: number | string): string {
  const n = typeof value === 'string' ? Number(value) : value;
  if (!Number.isFinite(n)) return '-';
  return THB.format(n);
}

// Sprint 13h P3 — single-source Thai Buddhist-Era date display.
// `dateStyle: 'medium'` → "20 พ.ค. 2569". The `buddhist` calendar is explicit
// because `th-TH` defaults to Gregorian (Christian-Era) in V8 Intl.
const dateFmt = new Intl.DateTimeFormat('th-TH', {
  dateStyle: 'medium',
  calendar: 'buddhist',
  timeZone: 'Asia/Bangkok',
});

export function formatDate(d: Date | string): string {
  const date = typeof d === 'string' ? new Date(d) : d;
  return dateFmt.format(date);
}

/** Today's date in Asia/Bangkok as yyyy-MM-dd. doc_date is server-authoritative;
 *  this is only the locked UI display (CLAUDE.md §10 — never user-typed). */
export function bangkokToday(): string {
  return new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Asia/Bangkok',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).format(new Date());
}

/** First day of the CURRENT Bangkok-local month, as yyyy-MM-dd.
 *  Built from `bangkokToday()` (never `Date.toISOString()`, which shifts to
 *  UTC and produces an off-by-one date range near midnight in TZ+07:00). */
export function bangkokMonthStart(): string {
  const [y, m] = bangkokToday().split('-');
  return `${y}-${m}-01`;
}

/** Last day of the CURRENT Bangkok-local month, as yyyy-MM-dd. Day-count is
 *  computed via `Date.UTC` arithmetic (day 0 of next month), never a local
 *  `new Date(y, m, 0)`, so the result can't be shifted by the browser's own
 *  timezone. */
export function bangkokMonthEnd(): string {
  const [y, m] = bangkokToday().split('-').map(Number) as [number, number];
  const lastDay = new Date(Date.UTC(y, m, 0)).getUTCDate();
  return `${y}-${String(m).padStart(2, '0')}-${String(lastDay).padStart(2, '0')}`;
}

/** Format Thai Tax ID for display: 0-1055-56123-45-0 */
export function formatTaxId(taxId: string | null | undefined): string {
  if (!taxId || taxId.length !== 13) return taxId ?? '-';
  return `${taxId[0]}-${taxId.slice(1, 5)}-${taxId.slice(5, 10)}-${taxId.slice(10, 12)}-${taxId[12]}`;
}
