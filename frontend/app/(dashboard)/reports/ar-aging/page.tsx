'use client';

import { useState } from 'react';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { PageHeader } from '@/components/ui/PageHeader';
import { CustomerSelector } from '@/components/ui/CustomerSelector';
import { MascotGreeting } from '@/components/layout/MascotGreeting';
import { useArAgingReport } from '@/lib/queries';
import { downloadFile, qs } from '@/lib/api';
import { errorToToast } from '@/lib/api/errors';
import { formatTHB, bangkokToday } from '@/lib/utils';
import type { ArAgingRow, SubledgerReconciliation } from '@/lib/types';

// WP3 (specs/fix-swarm-findings-all.md, chief01 MED) — a net-credit/overpayment bucket (or
// total) reads as a negative number but was rendered identically to a positive one; flag it
// visually so it doesn't look like a data error at a glance.
function amountClass(v: number, extra = ''): string {
  return `text-right tabular-nums${v < 0 ? ' text-error' : ''}${extra ? ` ${extra}` : ''}`;
}

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

  // Backend-generated CSV (mirrors /general-ledger/export — ap-aging's own export is FE-only
  // Blob download with no backend endpoint to reuse; see specs/ar-aging-csv-export.md).
  async function exportCsv() {
    try {
      await downloadFile(
        `reports/ar-aging/export${qs({ asOf, customerId: customerId ?? undefined })}`,
        `ar-aging-${asOf}.csv`,
      );
    } catch (e) {
      toast.error(errorToToast(e));
    }
  }

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
        <button type="button" className="btn btn-outline btn-sm ml-auto"
          disabled={rows.length === 0} onClick={exportCsv}>
          {t('exportCsv')}
        </button>
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
                  <td className={amountClass(r.current)}>{formatTHB(r.current)}</td>
                  <td className={amountClass(r.bucket31To60)}>{formatTHB(r.bucket31To60)}</td>
                  <td className={amountClass(r.bucket61To90)}>{formatTHB(r.bucket61To90)}</td>
                  <td className={amountClass(r.bucketOver90)}>{formatTHB(r.bucketOver90)}</td>
                  <td className={amountClass(r.total, 'font-semibold')}>{formatTHB(r.total)}</td>
                </tr>
              ))}
            </tbody>
            {totals && rows.length > 0 && (
              <tfoot>
                <tr className="font-semibold">
                  <td colSpan={2}>{t('totalRow')}</td>
                  <td className={amountClass(totals.current)}>{formatTHB(totals.current)}</td>
                  <td className={amountClass(totals.bucket31To60)}>{formatTHB(totals.bucket31To60)}</td>
                  <td className={amountClass(totals.bucket61To90)}>{formatTHB(totals.bucket61To90)}</td>
                  <td className={amountClass(totals.bucketOver90)}>{formatTHB(totals.bucketOver90)}</td>
                  <td className={amountClass(totals.total)}>{formatTHB(totals.total)}</td>
                </tr>
              </tfoot>
            )}
          </table>
        </div>
      )}
    </>
  );
}
