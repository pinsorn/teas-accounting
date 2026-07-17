'use client';

import { useState } from 'react';
import { useTranslations } from 'next-intl';
import { PageHeader } from '@/components/ui/PageHeader';
import { useSalesSummary } from '@/lib/queries';
import { formatTHB, bangkokMonthStart, bangkokMonthEnd,
  bangkokYearStart, bangkokYearEnd } from '@/lib/utils';

export default function SalesSummaryPage() {
  const t = useTranslations('report');
  const tc = useTranslations('common');
  // R7 — default to the current month instead of an empty range.
  const [from, setFrom] = useState(bangkokMonthStart());
  const [to, setTo] = useState(bangkokMonthEnd());
  const [groupBy, setGroupBy] = useState('customer');
  const ss = useSalesSummary(from, to, groupBy);

  function presetThisMonth() { setFrom(bangkokMonthStart()); setTo(bangkokMonthEnd()); }
  function presetThisYear() { setFrom(bangkokYearStart()); setTo(bangkokYearEnd()); }

  return (
    <>
      <PageHeader title={t('ssTitle')} />

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
          <span className="label-text text-xs">{t('groupBy')}</span>
          <select className="select select-bordered select-sm"
            value={groupBy} onChange={(e) => setGroupBy(e.target.value)}>
            <option value="customer">{t('byCustomer')}</option>
            <option value="business_unit">{t('byBusinessUnit')}</option>
            <option value="product">{t('byProduct')}</option>
          </select>
        </label>
      </div>

      <div className="overflow-x-auto rounded-lg border border-base-300">
        <table className="table table-zebra">
          <thead>
            <tr>
              <th>{groupBy === 'customer' ? t('byCustomer')
                : groupBy === 'product' ? t('byProduct') : t('byBusinessUnit')}</th>
              <th className="text-right">{t('docCount')}</th>
              <th className="text-right">{t('subtotal')}</th>
              <th className="text-right">{t('vat')}</th>
              <th className="text-right">{t('totalRow')}</th>
            </tr>
          </thead>
          <tbody>
            {(!from || !to) && (
              <tr><td colSpan={5} className="py-6 text-center text-base-content/50">{t('from')} / {t('to')}…</td></tr>
            )}
            {from && to && ss.isLoading && (
              <tr><td colSpan={5} className="py-6 text-center text-base-content/50">{tc('loading')}</td></tr>
            )}
            {ss.data?.rows.length === 0 && (
              <tr><td colSpan={5} className="py-6 text-center text-base-content/50">
                <div>{tc('empty')}</div>
                <div className="mx-auto mt-1 max-w-md text-xs">{t('ssBasisNote')}</div>
              </td></tr>
            )}
            {ss.data?.rows.map((r, i) => (
              <tr key={i}>
                <td>{r.label}</td>
                <td className="text-right tabular-nums">{r.docCount}</td>
                <td className="text-right tabular-nums">{formatTHB(r.subtotal)}</td>
                <td className="text-right tabular-nums">{formatTHB(r.vat)}</td>
                <td className="text-right tabular-nums">{formatTHB(r.total)}</td>
              </tr>
            ))}
          </tbody>
          {ss.data && ss.data.rows.length > 0 && (
            <tfoot>
              <tr className="font-bold">
                <td className="text-right">{t('totalRow')}</td>
                <td className="text-right tabular-nums">{ss.data.totals.docCount}</td>
                <td className="text-right tabular-nums">{formatTHB(ss.data.totals.subtotal)}</td>
                <td className="text-right tabular-nums">{formatTHB(ss.data.totals.vat)}</td>
                <td className="text-right tabular-nums">{formatTHB(ss.data.totals.total)}</td>
              </tr>
            </tfoot>
          )}
        </table>
      </div>
      <p className="mt-3 text-xs text-base-content/60">{t('ssBasisNote')}</p>
    </>
  );
}
