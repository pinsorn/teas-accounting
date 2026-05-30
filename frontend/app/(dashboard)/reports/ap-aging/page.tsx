'use client';

import { useState } from 'react';
import { useTranslations } from 'next-intl';
import { PageHeader } from '@/components/ui/PageHeader';
import { VendorSelector } from '@/components/ui/VendorSelector';
import { MascotGreeting } from '@/components/layout/MascotGreeting';
import { useApAgingReport } from '@/lib/queries';
import { formatTHB } from '@/lib/utils';
import type { ApAgingRow } from '@/lib/types';

// doc_date discipline (CLAUDE.md §5): default the as-of date to *today* in
// Asia/Bangkok, never the browser's local midnight.
function bangkokToday(): string {
  return new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Asia/Bangkok',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).format(new Date());
}

// Minimal CSV cell escaping (outstanding-po has no CSV helper to reuse).
function csvCell(v: string | number): string {
  const s = String(v ?? '');
  return /[",\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s;
}

export default function ApAgingPage() {
  const t = useTranslations('apAging');
  const tc = useTranslations('common');
  const [asOf, setAsOf] = useState(bangkokToday());
  const [vendorId, setVendorId] = useState<number | null>(null);

  const q = useApAgingReport(asOf, vendorId ?? undefined);
  const rows = q.data?.rows ?? [];
  const totals = q.data?.totals;

  function exportCsv() {
    const header = [
      t('vendor'), t('taxId'), t('current'), t('bucket31To60'),
      t('bucket61To90'), t('bucketOver90'), t('total'),
    ];
    const body = rows.map((r) => [
      r.vendorName, r.vendorTaxId ?? '', r.current, r.bucket31To60,
      r.bucket61To90, r.bucketOver90, r.total,
    ]);
    const lines = [header, ...body].map((cols) => cols.map(csvCell).join(','));
    if (totals) {
      lines.push([
        t('totalsRow'), '', totals.current, totals.bucket31To60,
        totals.bucket61To90, totals.bucketOver90, totals.total,
      ].map(csvCell).join(','));
    }
    const blob = new Blob(['﻿' + lines.join('\n')], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `ap-aging-${asOf}.csv`;
    a.click();
    URL.revokeObjectURL(url);
  }

  const showEmpty = !q.isLoading && rows.length === 0;

  return (
    <>
      <PageHeader title={t('title')} />

      <div className="mb-4 flex flex-wrap items-end gap-3">
        <label className="form-control">
          <span className="label-text text-xs">{tc('to')}</span>
          <input type="date" className="input input-bordered input-sm"
            value={asOf} onChange={(e) => setAsOf(e.target.value)} />
        </label>
        <div className="min-w-[16rem]">
          <VendorSelector
            value={vendorId}
            onChange={(id) => setVendorId(id)}
            label={null}
          />
          {vendorId !== null && (
            <button type="button" className="btn btn-ghost btn-xs mt-1"
              onClick={() => setVendorId(null)}>{t('clear')}</button>
          )}
        </div>
        <button type="button" className="btn btn-outline btn-sm ml-auto"
          disabled={rows.length === 0} onClick={exportCsv}>
          {t('exportCsv')}
        </button>
      </div>

      {showEmpty ? (
        <MascotGreeting title={t('emptyTitle')} subtitle={t('emptySubtitle')} />
      ) : (
        <div className="overflow-x-auto rounded-lg border border-base-300">
          <table className="table table-zebra">
            <thead><tr>
              <th>{t('vendor')}</th>
              <th>{t('taxId')}</th>
              <th className="text-right">{t('current')}</th>
              <th className="text-right">{t('bucket31To60')}</th>
              <th className="text-right">{t('bucket61To90')}</th>
              <th className="text-right">{t('bucketOver90')}</th>
              <th className="text-right">{t('total')}</th>
            </tr></thead>
            <tbody>
              {q.isLoading && (
                <tr><td colSpan={7} className="py-6 text-center text-base-content/50">{tc('loading')}</td></tr>
              )}
              {rows.map((r: ApAgingRow) => (
                <tr key={r.vendorId}>
                  <td>{r.vendorName}</td>
                  <td className="font-mono text-xs">{r.vendorTaxId ?? '—'}</td>
                  <td className="text-right tabular-nums">{formatTHB(r.current)}</td>
                  <td className="text-right tabular-nums">{formatTHB(r.bucket31To60)}</td>
                  <td className="text-right tabular-nums">{formatTHB(r.bucket61To90)}</td>
                  <td className="text-right tabular-nums">{formatTHB(r.bucketOver90)}</td>
                  <td className="text-right font-semibold tabular-nums">{formatTHB(r.total)}</td>
                </tr>
              ))}
            </tbody>
            {totals && rows.length > 0 && (
              <tfoot>
                <tr className="font-semibold">
                  <td colSpan={2}>{t('totalsRow')}</td>
                  <td className="text-right tabular-nums">{formatTHB(totals.current)}</td>
                  <td className="text-right tabular-nums">{formatTHB(totals.bucket31To60)}</td>
                  <td className="text-right tabular-nums">{formatTHB(totals.bucket61To90)}</td>
                  <td className="text-right tabular-nums">{formatTHB(totals.bucketOver90)}</td>
                  <td className="text-right tabular-nums">{formatTHB(totals.total)}</td>
                </tr>
              </tfoot>
            )}
          </table>
        </div>
      )}
    </>
  );
}
