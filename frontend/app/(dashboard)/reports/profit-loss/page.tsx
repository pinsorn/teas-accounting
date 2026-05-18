'use client';

import { useState } from 'react';
import { useTranslations } from 'next-intl';
import { PageHeader } from '@/components/ui/PageHeader';
import { useProfitLoss, useBusinessUnits } from '@/lib/queries';
import { formatTHB } from '@/lib/utils';

export default function ProfitLossPage() {
  const t = useTranslations('report');
  const tc = useTranslations('common');
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [buId, setBuId] = useState<number | undefined>(undefined);
  const [incUnspec, setIncUnspec] = useState(true);
  const bus = useBusinessUnits();
  const pl = useProfitLoss(from, to, buId, incUnspec);

  return (
    <>
      <PageHeader title={t('plTitle')} />

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

      {pl.data?.note && (
        <p data-testid="pl-note" className="mt-3 text-xs text-base-content/60">
          {pl.data.note}
        </p>
      )}
    </>
  );
}
