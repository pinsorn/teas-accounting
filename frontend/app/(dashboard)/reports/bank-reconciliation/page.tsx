'use client';

import { useState } from 'react';
import { useTranslations } from 'next-intl';
import { PageHeader } from '@/components/ui/PageHeader';
import { useBankAccounts, useBankReconciliationReport } from '@/lib/queries';
import { formatTHB, formatDate } from '@/lib/utils';
import type { ReconciliationReportItem } from '@/lib/types';

// Bank reconciliation (specs/bank-reconciliation.md B5.3) — mirrors reports/ap-aging/page.tsx:
// manual `table table-zebra` + filters. CSV export uses explicit "\r\n" (NOT AppendLine/'\n' —
// the folded footgun) + a UTF-8 BOM, per the spec's own instruction (deviates from ap-aging's
// own '\n' join on purpose, for strict Excel/RFC4180 compatibility).
function bangkokToday(): string {
  return new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Bangkok' }).format(new Date());
}
function monthStart(): string {
  const d = new Date(bangkokToday());
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-01`;
}
function csvCell(v: string | number): string {
  const s = String(v ?? '');
  return /[",\r\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s;
}

export default function BankReconciliationReportPage() {
  const t = useTranslations('bank');
  const tc = useTranslations('common');
  const banks = useBankAccounts();
  const [bankAccountId, setBankAccountId] = useState<number | null>(null);
  const [from, setFrom] = useState(monthStart());
  const [to, setTo] = useState(bangkokToday());

  const q = useBankReconciliationReport(bankAccountId ?? 0, from, to);
  const report = q.data;

  function exportCsv() {
    if (!report) return;
    const summary: (string | number)[][] = [
      [t('statementClosingBalance'), report.statementClosingBalance],
      [t('glBalance'), report.glBalance],
      [t('depositsInTransit'), report.depositsInTransitTotal],
      [t('outstandingPayments'), report.outstandingPaymentsTotal],
      [t('unmatchedLinesNet'), report.unmatchedLinesNet],
      [t('difference'), report.difference],
    ];
    const section = (title: string, items: ReconciliationReportItem[]) => [
      [title],
      [t('importDescription'), t('importDate'), t('importAmount')],
      ...items.map((x) => [x.description, x.date, x.amount]),
    ];
    const rows: (string | number)[][] = [
      [t('matching'), `${from} — ${to}`], [],
      ...summary, [],
      ...section(t('unmatchedLinesLabel'), report.unmatchedLines), [],
      ...section(t('depositsInTransit'), report.depositsInTransit), [],
      ...section(t('outstandingPayments'), report.outstandingPayments),
    ];
    const csv = rows.map((cols) => cols.map(csvCell).join(',')).join('\r\n');
    const blob = new Blob(['﻿' + csv], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `bank-reconciliation-${bankAccountId}-${from}-${to}.csv`;
    a.click();
    URL.revokeObjectURL(url);
  }

  return (
    <>
      <PageHeader title={t('reportTitle')} />

      <div className="mb-4 flex flex-wrap items-end gap-3">
        <label className="form-control">
          <span className="label-text text-xs">{t('bankAccountFilter')}</span>
          <select className="select select-bordered select-sm" value={bankAccountId ?? ''}
            onChange={(e) => setBankAccountId(e.target.value ? Number(e.target.value) : null)}>
            <option value="">{tc('all')}</option>
            {(banks.data ?? []).map((b) => (
              <option key={b.bankAccountId} value={b.bankAccountId}>{b.bankName} — {b.accountNo}</option>
            ))}
          </select>
        </label>
        <label className="form-control">
          <span className="label-text text-xs">{tc('from')}</span>
          <input type="date" className="input input-bordered input-sm" value={from} onChange={(e) => setFrom(e.target.value)} />
        </label>
        <label className="form-control">
          <span className="label-text text-xs">{tc('to')}</span>
          <input type="date" className="input input-bordered input-sm" value={to} onChange={(e) => setTo(e.target.value)} />
        </label>
        <button type="button" className="btn btn-outline btn-sm ml-auto" disabled={!report} onClick={exportCsv}>
          {t('exportCsv')}
        </button>
      </div>

      {!bankAccountId && (
        <p className="text-sm text-base-content/50">{t('selectBankAccount')}</p>
      )}

      {bankAccountId != null && q.isLoading && <p className="text-sm text-base-content/50">{tc('loading')}</p>}

      {report && (
        <>
          <div className="mb-6 grid grid-cols-2 gap-4 md:grid-cols-3 lg:grid-cols-6">
            <SummaryTile label={t('statementClosingBalance')} value={report.statementClosingBalance} />
            <SummaryTile label={t('glBalance')} value={report.glBalance} />
            <SummaryTile label={t('depositsInTransit')} value={report.depositsInTransitTotal} />
            <SummaryTile label={t('outstandingPayments')} value={report.outstandingPaymentsTotal} />
            <SummaryTile label={t('unmatchedLinesNet')} value={report.unmatchedLinesNet} />
            <SummaryTile label={t('difference')} value={report.difference}
              highlight={report.difference !== 0} />
          </div>

          <ReportSection title={t('unmatchedLinesLabel')} items={report.unmatchedLines} />
          <ReportSection title={t('depositsInTransit')} items={report.depositsInTransit} />
          <ReportSection title={t('outstandingPayments')} items={report.outstandingPayments} />
        </>
      )}
    </>
  );
}

function SummaryTile({ label, value, highlight }: { label: string; value: number; highlight?: boolean }) {
  return (
    <div className={`rounded-lg border p-3 ${highlight ? 'border-status-danger bg-status-danger-bg' : 'border-base-300'}`}>
      <div className="text-xs text-base-content/60">{label}</div>
      <div className={`text-right tabular-nums font-semibold ${highlight ? 'text-status-danger' : ''}`}>
        {formatTHB(value)}
      </div>
    </div>
  );
}

function ReportSection({ title, items }: { title: string; items: ReconciliationReportItem[] }) {
  const t = useTranslations('bank');
  const tc = useTranslations('common');
  if (items.length === 0) return null;
  return (
    <div className="mb-6">
      <h2 className="mb-2 font-semibold">{title} ({items.length})</h2>
      <div className="overflow-x-auto rounded-lg border border-base-300">
        <table className="table table-zebra table-sm">
          <thead>
            <tr>
              <th>{t('importDescription')}</th>
              <th>{t('importDate')}</th>
              <th className="text-right">{t('importAmount')}</th>
            </tr>
          </thead>
          <tbody>
            {items.map((x, i) => (
              // eslint-disable-next-line react/no-array-index-key
              <tr key={i}>
                <td>{x.description}</td>
                <td className="whitespace-nowrap">{formatDate(x.date)}</td>
                <td className="text-right tabular-nums">{formatTHB(x.amount)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
