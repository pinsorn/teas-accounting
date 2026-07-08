'use client';

import { useState } from 'react';
import { useTranslations } from 'next-intl';
import { PageHeader } from '@/components/ui/PageHeader';
import { useBalanceSheet } from '@/lib/queries';
import { formatTHB, bangkokToday } from '@/lib/utils';
import type { BalanceSheetSection } from '@/lib/types';

function Section({ title, section, tc }: {
  title: string; section: BalanceSheetSection | undefined; tc: (k: string) => string;
}) {
  return (
    <div className="overflow-x-auto rounded-lg border border-base-300">
      <table className="table table-zebra">
        <thead>
          <tr>
            <th colSpan={2}>{title}</th>
          </tr>
        </thead>
        <tbody>
          {!section && (
            <tr><td colSpan={2} className="py-6 text-center text-base-content/50">{tc('loading')}</td></tr>
          )}
          {section?.rows.length === 0 && (
            <tr><td colSpan={2} className="py-6 text-center text-base-content/50">{tc('empty')}</td></tr>
          )}
          {section?.rows.map((r) => (
            <tr key={r.accountCode}>
              <td><span className="font-mono">{r.accountCode}</span> {r.accountNameTh}</td>
              <td className="text-right tabular-nums">{formatTHB(r.balance)}</td>
            </tr>
          ))}
        </tbody>
        {section && (
          <tfoot>
            <tr className="font-bold">
              <td className="text-right">{title}</td>
              <td className="text-right tabular-nums">{formatTHB(section.total)}</td>
            </tr>
          </tfoot>
        )}
      </table>
    </div>
  );
}

export default function BalanceSheetPage() {
  const t = useTranslations('report');
  const tc = useTranslations('common');
  const [asOf, setAsOf] = useState(bangkokToday());
  const bs = useBalanceSheet(asOf);
  const balanced = bs.data?.balanced;

  return (
    <>
      <PageHeader title={t('bsTitle')} />

      <div className="mb-4 flex flex-wrap items-end gap-3">
        <label className="form-control">
          <span className="label-text text-xs">{t('asOf')}</span>
          <input type="date" className="input input-bordered input-sm"
            value={asOf} onChange={(e) => setAsOf(e.target.value)} />
        </label>
        {bs.data && (
          <span data-testid="bs-balanced"
            className={`badge ${balanced ? 'badge-success' : 'badge-error'}`}>
            {balanced ? t('balanced') : t('unbalanced')}
          </span>
        )}
      </div>

      <div className="flex flex-col gap-4">
        <Section title={t('assets')} section={bs.data?.assets} tc={tc} />
        <Section title={t('liabilities')} section={bs.data?.liabilities} tc={tc} />
        <Section title={t('equity')} section={bs.data?.equity} tc={tc} />

        {bs.data && (
          <div className="overflow-x-auto rounded-lg border border-base-300">
            <table className="table">
              <tbody>
                <tr>
                  <td>{t('currentPeriodEarnings')}</td>
                  <td className="text-right tabular-nums">{formatTHB(bs.data.currentPeriodEarnings)}</td>
                </tr>
                <tr className="font-bold">
                  <td>{t('liabilitiesAndEquityTotal')}</td>
                  <td className="text-right tabular-nums">{formatTHB(bs.data.liabilitiesAndEquityTotal)}</td>
                </tr>
              </tbody>
            </table>
          </div>
        )}
      </div>
    </>
  );
}
