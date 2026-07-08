'use client';

import { useState } from 'react';
import { useTranslations } from 'next-intl';
import { PageHeader } from '@/components/ui/PageHeader';
import { CustomerSelector } from '@/components/ui/CustomerSelector';
import { useCustomerStatement } from '@/lib/queries';
import { formatTHB, bangkokMonthStart, bangkokMonthEnd, docTypeLabelKey } from '@/lib/utils';
import type { SubledgerReconciliation } from '@/lib/types';

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

export default function CustomerStatementPage() {
  const t = useTranslations('report');
  const tc = useTranslations('common');
  const tCross = useTranslations('crossRef');
  const [customerId, setCustomerId] = useState<number | null>(null);
  const [from, setFrom] = useState(bangkokMonthStart());
  const [to, setTo] = useState(bangkokMonthEnd());
  // Applied filters — the query only (re)fires when "แสดงรายงาน" is clicked (mirrors
  // reports/general-ledger/page.tsx), so partial customer/date edits don't flicker the report.
  const [appliedCustomerId, setAppliedCustomerId] = useState<number | undefined>(undefined);
  const [appliedFrom, setAppliedFrom] = useState(from);
  const [appliedTo, setAppliedTo] = useState(to);

  const statement = useCustomerStatement(appliedCustomerId, appliedFrom, appliedTo);

  function showReport() {
    if (customerId == null) return;
    setAppliedCustomerId(customerId);
    setAppliedFrom(from);
    setAppliedTo(to);
  }

  return (
    <>
      <PageHeader title={t('customerStatementTitle')} />

      <div className="mb-4 flex flex-wrap items-end gap-3">
        <div className="min-w-[16rem]">
          <CustomerSelector value={customerId} onChange={(id) => setCustomerId(id)} label={null} />
        </div>
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
        <button className="btn btn-primary btn-sm" disabled={customerId == null}
          onClick={showReport}>
          {t('showReport')}
        </button>
      </div>

      {statement.data && <ReconciliationPanel r={statement.data.reconciliation} t={t} />}

      <div className="overflow-x-auto rounded-lg border border-base-300">
        <table className="table table-zebra">
          <thead>
            <tr>
              <th>{tc('date')}</th>
              <th>{t('type')}</th>
              <th>{t('docNo')}</th>
              <th>{t('description')}</th>
              <th className="text-right">{t('debit')}</th>
              <th className="text-right">{t('credit')}</th>
              <th className="text-right">{t('runningBalance')}</th>
            </tr>
          </thead>
          <tbody>
            {appliedCustomerId == null && (
              <tr><td colSpan={7} className="py-6 text-center text-base-content/50">{tc('empty')}</td></tr>
            )}
            {appliedCustomerId != null && statement.isLoading && (
              <tr><td colSpan={7} className="py-6 text-center text-base-content/50">{tc('loading')}</td></tr>
            )}
            {statement.data && (
              <>
                <tr className="font-semibold">
                  <td colSpan={6}>{t('openingBalance')}</td>
                  <td className="text-right tabular-nums">{formatTHB(statement.data.openingBalance)}</td>
                </tr>
                {statement.data.lines.map((l, i) => (
                  <tr key={i}>
                    <td>{l.docDate}</td>
                    <td>{tCross(docTypeLabelKey(l.docType))}</td>
                    <td>{l.docNo}</td>
                    <td>{l.description}</td>
                    <td className="text-right tabular-nums">{l.debit ? formatTHB(l.debit) : ''}</td>
                    <td className="text-right tabular-nums">{l.credit ? formatTHB(l.credit) : ''}</td>
                    <td className="text-right tabular-nums">{formatTHB(l.runningBalance)}</td>
                  </tr>
                ))}
              </>
            )}
          </tbody>
          {statement.data && (
            <tfoot>
              <tr className="font-bold">
                <td colSpan={4} className="text-right">{t('totalRow')}</td>
                <td className="text-right tabular-nums">{formatTHB(statement.data.totalDebit)}</td>
                <td className="text-right tabular-nums">{formatTHB(statement.data.totalCredit)}</td>
                <td className="text-right tabular-nums" />
              </tr>
              <tr className="font-bold">
                <td colSpan={6} className="text-right">{t('closingBalance')}</td>
                <td className="text-right tabular-nums">{formatTHB(statement.data.closingBalance)}</td>
              </tr>
            </tfoot>
          )}
        </table>
      </div>
    </>
  );
}
