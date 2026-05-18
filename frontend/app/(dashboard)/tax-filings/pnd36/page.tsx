'use client';

import { useState } from 'react';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { PageHeader } from '@/components/ui/PageHeader';
import { usePnd36 } from '@/lib/queries';
import { formatTHB } from '@/lib/utils';
import type { Pnd36Filing } from '@/lib/types';

function thisMonth() {
  return new Date().toISOString().slice(0, 7);
}

export default function Pnd36Page() {
  const t = useTranslations('tf');
  const [ym, setYm] = useState(thisMonth());
  const [filing, setFiling] = useState<Pnd36Filing | null>(null);
  const mut = usePnd36();

  function run(mode: 'preview' | 'finalize') {
    if (mode === 'finalize' && !window.confirm(t('pnd36FinalizeConfirm'))) return;
    mut.mutate(
      { period: Number(ym.replace('-', '')), mode },
      {
        onSuccess: (f) => {
          setFiling(f);
          toast.success(mode === 'finalize' ? t('finalized') : t('previewed'));
        },
        onError: (e: unknown) =>
          toast.error(e instanceof Error ? e.message : 'Error'),
      },
    );
  }

  return (
    <>
      <PageHeader title={t('pnd36Title')} />
      <div className="mb-4 flex flex-wrap items-end gap-3">
        <label className="form-control">
          <span className="label-text text-xs">{t('period')}</span>
          <input type="month" className="input input-bordered input-sm"
            value={ym} onChange={(e) => setYm(e.target.value)} />
        </label>
        <button className="btn btn-sm btn-primary"
          disabled={mut.isPending} onClick={() => run('preview')}>
          {t('preview')}
        </button>
        <button className="btn btn-sm btn-secondary"
          disabled={mut.isPending || !filing} onClick={() => run('finalize')}>
          {t('finalize')}
        </button>
        {filing && (
          <span data-testid="tf-status"
            className={`badge ${filing.status === 'Preview' ? 'badge-ghost' : 'badge-success'}`}>
            {filing.status} · {filing.submissionMode}
          </span>
        )}
      </div>

      {filing && (
        <>
          <div className="mb-3 text-sm text-base-content/70">
            {t('due')} {filing.filingDueDate}
            {filing.reverseChargeJournalId != null && (
              <> · {t('jvPosted')} #{filing.reverseChargeJournalId}</>
            )}
          </div>
          <div className="overflow-x-auto rounded-lg border border-base-300">
            <table className="table table-zebra">
              <thead>
                <tr>
                  <th>{t('vendor')}</th><th>{t('country')}</th><th>{t('refDoc')}</th>
                  <th className="text-right">{t('serviceAmount')}</th>
                  <th className="text-right">{t('whtRate')}</th>
                  <th className="text-right">{t('vatAmount')}</th>
                </tr>
              </thead>
              <tbody>
                {filing.rows.length === 0 && (
                  <tr><td colSpan={6} className="py-6 text-center text-base-content/50">—</td></tr>
                )}
                {filing.rows.map((r, i) => (
                  <tr key={i}>
                    <td>{r.vendorName}</td>
                    <td>{r.vendorCountry ?? '—'}</td>
                    <td className="font-mono">{r.refDoc}</td>
                    <td className="text-right tabular-nums">{formatTHB(r.serviceAmountThb)}</td>
                    <td className="text-right tabular-nums">{(r.vatRate * 100).toFixed(0)}%</td>
                    <td className="text-right tabular-nums">{formatTHB(r.vatAmount)}</td>
                  </tr>
                ))}
              </tbody>
              <tfoot>
                <tr className="font-bold">
                  <td colSpan={3} className="text-right">{t('total')}</td>
                  <td className="text-right tabular-nums">{formatTHB(filing.totalService)}</td>
                  <td />
                  <td className="text-right tabular-nums">{formatTHB(filing.totalVat)}</td>
                </tr>
              </tfoot>
            </table>
          </div>
          <p data-testid="pnd36-jv-note"
            className="mt-3 text-xs text-base-content/60">{t('pnd36JvNote')}</p>
        </>
      )}
    </>
  );
}
