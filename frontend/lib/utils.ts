import { clsx, type ClassValue } from 'clsx';
import { twMerge } from 'tailwind-merge';

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs));
}

/** WP4 4.4 — localStorage key for the super-admin company switcher's last pick, so
 *  a fresh login can restore it (F17: re-login always reset to the default company). */
export const LAST_COMPANY_KEY = 'teas.lastCompanyId';

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

/** WP4 4.1 — numeric dd/MM/yyyy Buddhist-Era hint shown under native <input type="date">
 *  fields (which always render in the browser's own locale/calendar). Parses the
 *  yyyy-MM-dd string directly — never via `new Date()` — so it can't drift a day across
 *  a UTC/Bangkok timezone boundary. Returns '' for an empty/invalid value. */
export function formatDateBE(yyyyMmDd: string | undefined | null): string {
  if (!yyyyMmDd) return '';
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(yyyyMmDd);
  if (!m) return '';
  const [, y, mo, d] = m;
  return `${d}/${mo}/${Number(y) + 543}`;
}

/** Absolute day gap between two yyyy-MM-dd date strings. Used by bank
 *  reconciliation to warn when a manual match confirm falls outside the
 *  ±7-day auto-suggest window (specs/bank-reconciliation.md D4) — the API
 *  stays permissive, this is FE-only warning math. */
export function dayGap(a: string, b: string): number {
  const ms = Math.abs(new Date(a).getTime() - new Date(b).getTime());
  return Math.round(ms / 86400000);
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

// Cycle A #9 — customer statement / vendor ledger rows carry the raw PascalCase
// docType SubledgerReportService emits (e.g. "TaxInvoice"). Maps to the existing
// `crossRef` i18n namespace (already used for the sales-chain doc labels) instead
// of introducing a second mapping; VendorInvoice/PaymentVoucher are AP-side and
// were added to `crossRef` alongside the AR ones this feature reused.
// Must cover every docType emitted by SubledgerReportService.cs — an unmapped
// type falls back to the raw string (renders as "crossRef.X" + console error).
const DOC_TYPE_I18N_KEY: Record<string, string> = {
  TaxInvoice: 'taxInvoice',
  Receipt: 'receipt',
  CreditNote: 'creditNote',
  DebitNote: 'debitNote',
  VendorInvoice: 'vendorInvoice',
  PaymentVoucher: 'paymentVoucher',
};

/** Raw docType string → `crossRef` i18n key (falls back to the raw value for
 *  any type not yet mapped, so an unmapped type is visible, not blank). */
export function docTypeLabelKey(docType: string): string {
  return DOC_TYPE_I18N_KEY[docType] ?? docType;
}
