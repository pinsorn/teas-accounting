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

const dateFmt = new Intl.DateTimeFormat('th-TH', {
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
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

/** Format Thai Tax ID for display: 0-1055-56123-45-0 */
export function formatTaxId(taxId: string | null | undefined): string {
  if (!taxId || taxId.length !== 13) return taxId ?? '-';
  return `${taxId[0]}-${taxId.slice(1, 5)}-${taxId.slice(5, 10)}-${taxId.slice(10, 12)}-${taxId[12]}`;
}
