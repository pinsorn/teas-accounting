'use client';

import { useState } from 'react';
import { useTranslations } from 'next-intl';
import { PageHeader } from '@/components/ui/PageHeader';
import { CustomerSelector } from '@/components/ui/CustomerSelector';
import { MascotGreeting } from '@/components/layout/MascotGreeting';
import { useArAgingReport } from '@/lib/queries';
import { formatTHB, bangkokToday } from '@/lib/utils';
import type { ArAgingRow, SubledgerReconciliation } from '@/lib/types';

function ReconciliationPanel({ r, t }: { r: SubledgerReconciliation; t: (k: string) => string }) {
  return (
    <div className="mb-4 flex flex-wrap items-center gap-4 rounded-lg border border-base-300 p-3 text-sm">
      <div>
        <span className="text-base-content/60">{t('controlAccount')} ({r.controlAccountCode})</span>{' '}
        <span className="font-semibold tabular-nums">{formatTHB(r.controlAccountBalance)}</span>
      </div>
      <div>
        <span className="text-base-content/60">{t('subLedgerTotal')}</span>{' '}
        <span className="font-semibold tabular-nums">{formatTHB(r.subLedgerTotal)}</span>
      </div>
      {!r.balanced && (
        <div>
          <span className="text-base-content/60">{t('difference')}</span>{' '}
          <span className="font-semibold tabular-nums text-error">{formatTHB(r.difference)}</span>
        </div>
      )}
      <span className={`badge ml-auto ${r.balanced ? 'badge-success' : 'badge-error'}`}>
        {r.balanced ? t('balanced') : t('notReconciled')}
      </span>
    </div>
  );
}

export default function ArAgingPage() {
  const t = useTranslations('report');
  const tc = useTranslations('common');
  const [asOf, setAsOf] = useState(bangkokToday());
  const [customerId, setCustomerId] = useState<number | null>(null);

  const q = useArAgingReport(asOf, customerId ?? undefined);
  const rows = q.data?.rows ?? [];
  const totals = q.data?.totals;

  const showEmpty = !q.isLoading && rows.length === 0;

  return (
    <>
      <PageHeader title={t('arAgingTitle')} />

      <div className="mb-4 flex flex-wrap items-end gap-3">
        <label className="form-control">
          <span className="label-text text-xs">{t('asOf')}</span>
          <input type="date" className="input input-bordered input-sm"
            value={asOf} onChange={(e) => setAsOf(e.target.value)} />
        </label>
        <div className="min-w-[16rem]">
          <CustomerSelector
            value={customerId}
            onChange={(id) => setCustomerId(id)}
            label={null}
          />
          {customerId !== null && (
            <button type="button" className="btn btn-ghost btn-xs mt-1"
              onClick={() => setCustomerId(null)}>{t('clear')}</button>
          )}
        </div>
      </div>

      {q.data && <ReconciliationPanel r={q.data.reconciliation} t={t} />}

      {showEmpty ? (
        <MascotGreeting title={t('arAgingEmptyTitle')} subtitle={t('arAgingEmptySubtitle')} />
      ) : (
        <div className="overflow-x-auto rounded-lg border border-base-300">
          <table className="table table-zebra">
            <thead><tr>
              <th>{t('customer')}</th>
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
              {rows.map((r: ArAgingRow) => (
                <tr key={r.customerId}>
                  <td>{r.customerName}</td>
                  <td className="font-mono text-xs">{r.customerTaxId ?? '—'}</td>
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
                  <td colSpan={2}>{t('totalRow')}</td>
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
