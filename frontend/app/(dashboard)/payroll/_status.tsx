'use client';

import { useTranslations } from 'next-intl';

/** yyyymm → "MM/YYYY" (CE). */
export function formatPeriod(p: string): string {
  if (!/^\d{6}$/.test(p)) return p;
  return `${p.slice(4)}/${p.slice(0, 4)}`;
}

/** Payroll run status pill. "Paid" is a sub-state of Posted (shown when isPaid). */
export function PayrollStatusBadge({ status, isPaid }: { status: string; isPaid: boolean }) {
  const t = useTranslations('payroll');
  if (isPaid) return <span className="badge badge-success badge-sm">{t('statusPaid')}</span>;
  const map: Record<string, string> = {
    DRAFT: 'badge-ghost', APPROVED: 'badge-info', POSTED: 'badge-primary', VOIDED: 'badge-error',
  };
  const label: Record<string, string> = {
    DRAFT: t('statusDraft'), APPROVED: t('statusApproved'), POSTED: t('statusPosted'), VOIDED: t('statusVoided'),
  };
  return <span className={`badge badge-sm ${map[status] ?? 'badge-ghost'}`}>{label[status] ?? status}</span>;
}
