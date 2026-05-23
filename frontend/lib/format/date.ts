// Sprint 13h P3 — single source of truth for Thai-locale date formatting.
//
// Display convention: Buddhist-Era short form `20 พ.ค. 2569` (TH locale,
// `calendar: 'buddhist'`).  All chapter-3 user-facing dates flow through this.
//
// Storage convention: internal storage stays CE ISO `YYYY-MM-DD` (CLAUDE.md §10);
// these helpers convert at the display boundary only.
//
// Form date *inputs* (`<input type="date">`) cannot be localized natively in
// the browser — they remain CE under the user's OS locale.  P3 ships an
// acceptance label clarifying the CE input vs. BE display split.  A Thai date
// picker component is a future polish item.

const FMT = new Intl.DateTimeFormat('th-TH', {
  dateStyle: 'medium',
  calendar: 'buddhist',
  timeZone: 'Asia/Bangkok',
});

const FMT_LONG = new Intl.DateTimeFormat('th-TH', {
  dateStyle: 'long',
  calendar: 'buddhist',
  timeZone: 'Asia/Bangkok',
});

export function formatDateTH(value: string | Date | null | undefined): string {
  if (!value) return '-';
  const d = typeof value === 'string' ? new Date(value) : value;
  if (Number.isNaN(d.getTime())) return '-';
  return FMT.format(d);
}

export function formatDateTHLong(value: string | Date | null | undefined): string {
  if (!value) return '-';
  const d = typeof value === 'string' ? new Date(value) : value;
  if (Number.isNaN(d.getTime())) return '-';
  return FMT_LONG.format(d);
}
