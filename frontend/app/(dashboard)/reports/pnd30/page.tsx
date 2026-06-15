'use client';

import { useState } from 'react';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { PageHeader } from '@/components/ui/PageHeader';
import { openPdf } from '@/lib/api';
import { usePnd30, useSystemInfo } from '@/lib/queries';
import { formatTHB } from '@/lib/utils';
import type { Pnd30Filing } from '@/lib/types';
import { useConfirm } from '@/hooks/useConfirm';

function thisMonth() {
  return new Date().toISOString().slice(0, 7); // yyyy-MM
}
const toPeriod = (ym: string) => Number(ym.replace('-', '')); // 202605

export default function Pnd30Page() {
  const t = useTranslations('report');
  const [ym, setYm] = useState(thisMonth());
  const [filing, setFiling] = useState<Pnd30Filing | null>(null);
  const pnd30 = usePnd30();
  const confirm = useConfirm();
  // ม.86 — non-VAT companies do not file ภ.พ.30. Guard against direct URL access.
  const vatMode = useSystemInfo().data?.vatMode ?? true;

  async function run(mode: 'preview' | 'finalize') {
    if (mode === 'finalize' &&
        !(await confirm({ description: t('pnd30FinalizeConfirm'), variant: 'destructive' }))) return;
    pnd30.mutate(
      { period: toPeriod(ym), mode },
      {
        onSuccess: (f) => {
          setFiling(f);
          toast.success(mode === 'finalize' ? t('pnd30Finalized') : t('pnd30Previewed'));
        },
        onError: (e: unknown) =>
          toast.error(e instanceof Error ? e.message : 'Error'),
      },
    );
  }

  const L = filing?.lines;

  if (!vatMode) {
    return (
      <>
        <PageHeader title={t('pnd30Title')} />
        <div className="rounded-card border border-ink-100 bg-base-100 p-8 text-center text-ink-600">
          {t('pnd30NonVat')}
        </div>
      </>
    );
  }

  return (
    <>
      <PageHeader title={t('pnd30Title')} />

      <div className="mb-4 flex flex-wrap items-end gap-3">
        <label className="form-control">
          <span className="label-text text-xs">{t('period')}</span>
          <input type="month" className="input input-bordered input-sm"
            value={ym} onChange={(e) => setYm(e.target.value)} />
        </label>
        <button className="btn btn-sm btn-primary"
          disabled={pnd30.isPending} onClick={() => run('preview')}>
          {t('preview')}
        </button>
        <button className="btn btn-sm btn-secondary"
          disabled={pnd30.isPending || !filing} onClick={() => run('finalize')}>
          {t('finalize')}
        </button>
        <button className="btn btn-sm btn-outline" data-testid="pnd30-download-pdf"
          onClick={() => openPdf(`tax-filings/pnd30/pdf?period=${toPeriod(ym)}`)
            .catch((e: unknown) => toast.error(e instanceof Error ? e.message : 'Error'))}>
          {t('pnd30DownloadPdf')}
        </button>
        {filing && (
          <span data-testid="pnd30-status"
            className={`badge ${filing.status === 'Preview' ? 'badge-ghost' : 'badge-success'}`}>
            {filing.status} · {filing.submissionMode}
          </span>
        )}
      </div>

      {L && (
        <>
          <div className="mb-4 text-sm text-base-content/70">
            {filing!.company.nameTh} · {filing!.company.taxId} ·{' '}
            {t('pnd30Due')} {filing!.filingDueDate}
          </div>
          <div className="overflow-x-auto rounded-lg border border-base-300">
            <table className="table">
              <tbody>
                <tr><td>{t('salesTaxable')}</td>
                  <td className="text-right tabular-nums">{formatTHB(L.salesTaxable.amount)}</td>
                  <td className="text-right tabular-nums">{formatTHB(L.salesTaxable.vat)}</td></tr>
                <tr><td>{t('salesZeroRated')}</td>
                  <td className="text-right tabular-nums">{formatTHB(L.salesZeroRated.amount)}</td>
                  <td className="text-right tabular-nums">{formatTHB(L.salesZeroRated.vat)}</td></tr>
                <tr><td>{t('salesExempt')}</td>
                  <td className="text-right tabular-nums">{formatTHB(L.salesExempt.amount)}</td>
                  <td className="text-right tabular-nums">{formatTHB(L.salesExempt.vat)}</td></tr>
                <tr className="font-semibold"><td>{t('outputVat')}</td>
                  <td /><td className="text-right tabular-nums">{formatTHB(L.outputVatTotal)}</td></tr>
                <tr><td>{t('purchaseTaxable')}</td>
                  <td className="text-right tabular-nums">{formatTHB(L.purchaseTaxable.amount)}</td>
                  <td className="text-right tabular-nums">{formatTHB(L.purchaseTaxable.vat)}</td></tr>
                <tr><td>{t('claimRatio')} (ม.82/6)</td>
                  <td className="text-right tabular-nums">
                    {(L.purchaseProportionalApportionment.claimRatio * 100).toFixed(2)}%</td>
                  <td className="text-right tabular-nums">
                    {formatTHB(L.purchaseProportionalApportionment.claimableAmount)}</td></tr>
                <tr className="font-semibold"><td>{t('inputVat')}</td>
                  <td /><td className="text-right tabular-nums">{formatTHB(L.inputVatTotal)}</td></tr>
                <tr className="font-bold border-t-2 border-base-300">
                  <td>{t('netVatPayable')}</td><td />
                  <td className="text-right tabular-nums">{formatTHB(L.netVatPayable)}</td></tr>
                {L.creditCarryForward > 0 && (
                  <tr className="font-bold"><td>{t('creditCarry')}</td><td />
                    <td className="text-right tabular-nums">{formatTHB(L.creditCarryForward)}</td></tr>
                )}
              </tbody>
            </table>
          </div>
          {filing!.warnings.length > 0 && (
            <ul data-testid="pnd30-warnings" className="mt-3 space-y-1 text-xs text-warning">
              {filing!.warnings.map((w, i) => <li key={i}>⚠ {w}</li>)}
            </ul>
          )}
        </>
      )}
    </>
  );
}
