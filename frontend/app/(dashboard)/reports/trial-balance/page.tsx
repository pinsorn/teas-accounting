'use client';

import { useState } from 'react';
import { useTranslations } from 'next-intl';
import { PageHeader } from '@/components/ui/PageHeader';
import { useTrialBalance } from '@/lib/queries';
import { formatTHB } from '@/lib/utils';

function today() {
  return new Date().toISOString().slice(0, 10);
}

export default function TrialBalancePage() {
  const t = useTranslations('report');
  const tc = useTranslations('common');
  const [asOf, setAsOf] = useState(today());
  const [incInactive, setIncInactive] = useState(false);
  const tb = useTrialBalance(asOf, incInactive);
  const balanced = tb.data?.totals.balanced;

  return (
    <>
      <PageHeader title={t('tbTitle')} />

      <div className="mb-4 flex flex-wrap items-end gap-3">
        <label className="form-control">
          <span className="label-text text-xs">{t('asOf')}</span>
          <input type="date" className="input input-bordered input-sm"
            value={asOf} onChange={(e) => setAsOf(e.target.value)} />
        </label>
        <label className="label cursor-pointer gap-2">
          <input type="checkbox" className="checkbox checkbox-sm"
            checked={incInactive} onChange={(e) => setIncInactive(e.target.checked)} />
          <span className="label-text text-xs">{t('includeInactive')}</span>
        </label>
        {tb.data && (
          <span data-testid="tb-balanced"
            className={`badge ${balanced ? 'badge-success' : 'badge-error'}`}>
            {balanced ? t('balanced') : t('unbalanced')}
          </span>
        )}
      </div>

      <div className="overflow-x-auto rounded-lg border border-base-300">
        <table className="table table-zebra">
          <thead>
            <tr>
              <th>{t('account')}</th><th>{t('type')}</th>
              <th className="text-right">{t('debit')}</th>
              <th className="text-right">{t('credit')}</th>
              <th className="text-right">{t('net')}</th>
            </tr>
          </thead>
          <tbody>
            {tb.isLoading && (
              <tr><td colSpan={5} className="py-6 text-center text-base-content/50">{tc('loading')}</td></tr>
            )}
            {tb.data?.rows.length === 0 && (
              <tr><td colSpan={5} className="py-6 text-center text-base-content/50">{tc('empty')}</td></tr>
            )}
            {tb.data?.rows.map((r) => (
              <tr key={r.accountCode}>
                <td><span className="font-mono">{r.accountCode}</span> {r.accountNameTh}</td>
                <td className="text-xs">{r.accountType} ({r.normalBalance})</td>
                <td className="text-right tabular-nums">{formatTHB(r.debit)}</td>
                <td className="text-right tabular-nums">{formatTHB(r.credit)}</td>
                <td className="text-right tabular-nums">{formatTHB(r.net)}</td>
              </tr>
            ))}
          </tbody>
          {tb.data && tb.data.rows.length > 0 && (
            <tfoot>
              <tr className="font-bold">
                <td colSpan={2} className="text-right">{t('totalRow')}</td>
                <td className="text-right tabular-nums">{formatTHB(tb.data.totals.debit)}</td>
                <td className="text-right tabular-nums">{formatTHB(tb.data.totals.credit)}</td>
                <td className="text-right tabular-nums">
                  {formatTHB(tb.data.totals.debit - tb.data.totals.credit)}
                </td>
              </tr>
            </tfoot>
          )}
        </table>
      </div>
    </>
  );
}
