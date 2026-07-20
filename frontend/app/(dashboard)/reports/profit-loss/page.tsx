'use client';

import { useState } from 'react';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { Download } from 'lucide-react';
import { PageHeader } from '@/components/ui/PageHeader';
import { useProfitLoss, useBusinessUnits } from '@/lib/queries';
import { openPdf, qs } from '@/lib/api';
import { errorToToast } from '@/lib/api/errors';
import { formatTHB, bangkokToday, bangkokMonthStart, bangkokMonthEnd,
  bangkokYearStart, bangkokYearEnd, formatDateBE, csvCell } from '@/lib/utils';

export default function ProfitLossPage() {
  const t = useTranslations('report');
  const tc = useTranslations('common');
  // R7 — default to the current month instead of an empty range.
  const [from, setFrom] = useState(bangkokMonthStart());
  const [to, setTo] = useState(bangkokMonthEnd());
  const [buId, setBuId] = useState<number | undefined>(undefined);
  const [incUnspec, setIncUnspec] = useState(true);
  const bus = useBusinessUnits();
  const pl = useProfitLoss(from, to, buId, incUnspec);

  function presetThisMonth() { setFrom(bangkokMonthStart()); setTo(bangkokMonthEnd()); }
  function presetThisYear() { setFrom(bangkokYearStart()); setTo(bangkokYearEnd()); }

  // R5a — full-year BS+P&L PDF (existing backend endpoint, year = the selected "to" date's year).
  async function exportPdf() {
    const year = Number((to || bangkokToday()).slice(0, 4));
    try {
      await openPdf(`reports/financial-statements/pdf${qs({ year })}`);
    } catch (e) {
      toast.error(errorToToast(e));
    }
  }

  function exportCsv() {
    if (!pl.data) return;
    const header = [t('bu'), t('revenue'), t('expense'), t('netProfit')];
    const body = pl.data.groups.map((g) => [g.groupName, g.revenue, g.expense, g.netProfit]);
    const lines = [header, ...body].map((cols) => cols.map(csvCell).join(','));
    lines.push([t('totalRow'), pl.data.totals.revenue, pl.data.totals.expense,
      pl.data.totals.netProfit].map(csvCell).join(','));
    const blob = new Blob(['﻿' + lines.join('\n')], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `profit-loss-${from}-${to}.csv`;
    a.click();
    URL.revokeObjectURL(url);
  }

  // HIGH (chief01) — TB/BS default "as-of today" while P&L defaults to the full current
  // month, which can pull in future-dated (but already-posted) docs like a payroll run paid
  // ahead of "today" — two reports can show wildly different numbers for what looks like "the
  // same period" with no on-screen hint that their cutoffs differ. Surface the exact range
  // always, and flag it explicitly when it extends past today. Never changes the numbers.
  const rangeExtendsPastToday = !!from && !!to && to > bangkokToday();

  return (
    <>
      <PageHeader title={t('plTitle')}
        subtitle={from && to ? t('periodBasis', { from: formatDateBE(from), to: formatDateBE(to) }) : undefined}
        actions={
          <>
            <button className="btn btn-outline btn-sm gap-1" onClick={exportPdf}>
              <Download className="h-4 w-4" aria-hidden /> {t('exportFinancialPdf')}
            </button>
            <button className="btn btn-outline btn-sm gap-1" disabled={!pl.data} onClick={exportCsv}>
              <Download className="h-4 w-4" aria-hidden /> {t('exportCsv')}
            </button>
          </>
        }
      />

      {rangeExtendsPastToday && (
        <p className="mb-3 text-xs text-warning">⚠ {t('periodFutureWarning', { date: formatDateBE(to) })}</p>
      )}

      <div className="mb-4 flex flex-wrap items-end gap-3">
        <label className="form-control">
          <span className="label-text text-xs">{t('from')}</span>
          <input type="date" className="input input-bordered input-sm"
            value={from} onChange={(e) => setFrom(e.target.value)} />
        </label>
        <label className="form-control">
          <span className="label-text text-xs">{t('to')}</span>
          <input type="date" className="input input-bordered input-sm"
            value={to} onChange={(e) => setTo(e.target.value)} />
        </label>
        <div className="flex gap-1">
          <button type="button" className="btn btn-xs" onClick={presetThisMonth}>{t('presetThisMonth')}</button>
          <button type="button" className="btn btn-xs" onClick={presetThisYear}>{t('presetThisYear')}</button>
        </div>
        <label className="form-control">
          <span className="label-text text-xs">{t('bu')}</span>
          <select className="select select-bordered select-sm"
            value={buId ?? ''} onChange={(e) =>
              setBuId(e.target.value ? Number(e.target.value) : undefined)}>
            <option value="">— {t('allBu')} —</option>
            {bus.data?.map((b) => (
              <option key={b.businessUnitId} value={b.businessUnitId}>
                {b.code} · {b.nameTh}
              </option>
            ))}
          </select>
        </label>
        <label className="label cursor-pointer gap-2">
          <input type="checkbox" className="checkbox checkbox-sm"
            checked={incUnspec} onChange={(e) => setIncUnspec(e.target.checked)} />
          <span className="label-text text-xs">{t('inclUnspec')}</span>
        </label>
      </div>

      <div className="overflow-x-auto rounded-lg border border-base-300">
        <table className="table table-zebra">
          <thead>
            <tr>
              <th>{t('bu')}</th>
              <th className="text-right">{t('revenue')}</th>
              <th className="text-right">{t('expense')}</th>
              <th className="text-right">{t('netProfit')}</th>
            </tr>
          </thead>
          <tbody>
            {(!from || !to) && (
              <tr><td colSpan={4} className="py-6 text-center text-base-content/50">{t('from')} / {t('to')}…</td></tr>
            )}
            {from && to && pl.isLoading && (
              <tr><td colSpan={4} className="py-6 text-center text-base-content/50">{tc('loading')}</td></tr>
            )}
            {pl.data?.groups.length === 0 && (
              <tr><td colSpan={4} className="py-6 text-center text-base-content/50">{tc('empty')}</td></tr>
            )}
            {pl.data?.groups.map((g) => (
              <tr key={g.businessUnitId ?? 'none'}>
                <td>{g.groupName}</td>
                <td className="text-right tabular-nums">{formatTHB(g.revenue)}</td>
                <td className="text-right tabular-nums">{formatTHB(g.expense)}</td>
                <td className="text-right tabular-nums">{formatTHB(g.netProfit)}</td>
              </tr>
            ))}
          </tbody>
          {pl.data && pl.data.groups.length > 0 && (
            <tfoot>
              <tr className="font-bold">
                <td className="text-right">{t('totalRow')}</td>
                <td className="text-right tabular-nums">{formatTHB(pl.data.totals.revenue)}</td>
                <td className="text-right tabular-nums">{formatTHB(pl.data.totals.expense)}</td>
                <td className="text-right tabular-nums">{formatTHB(pl.data.totals.netProfit)}</td>
              </tr>
            </tfoot>
          )}
        </table>
      </div>

    </>
  );
}
