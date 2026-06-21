'use client';

import { useState } from 'react';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { PageHeader } from '@/components/ui/PageHeader';
import { usePnd3, usePnd53, usePnd54 } from '@/lib/queries';
import { downloadFile, openPdf } from '@/lib/api';
import { formatTHB } from '@/lib/utils';
import type { WhtFiling } from '@/lib/types';
import { useConfirm } from '@/hooks/useConfirm';
import { RdPrepSteps } from './RdPrepSteps';

const HOOKS = { pnd3: usePnd3, pnd53: usePnd53, pnd54: usePnd54 } as const;

const FORM_LABEL = { pnd3: 'ภ.ง.ด.3', pnd53: 'ภ.ง.ด.53', pnd54: 'ภ.ง.ด.54' } as const;

function thisMonth() {
  return new Date().toISOString().slice(0, 7);
}

export function WhtFilingClient({
  form,
  titleKey,
}: {
  form: 'pnd3' | 'pnd53' | 'pnd54';
  titleKey: string;
}) {
  const t = useTranslations('tf');
  const [ym, setYm] = useState(thisMonth());
  const [filing, setFiling] = useState<WhtFiling | null>(null);
  const mut = HOOKS[form]();
  const confirm = useConfirm();
  const [downloading, setDownloading] = useState(false);

  // cont.82.1 P2 — RD batch-upload file (FORMAT กลาง). Only ภ.ง.ด.3 / 53 have a central
  // text format; ภ.ง.ด.54 (จ่ายต่างประเทศ ม.70) is not in scope.
  const canBatch = form === 'pnd3' || form === 'pnd53';

  async function downloadBatch() {
    setDownloading(true);
    try {
      const period = ym.replace('-', '');
      await downloadFile(
        `tax-filings/${form}/batch-file?period=${period}`,
        `${form.toUpperCase()}_${period}.txt`,
      );
    } catch (e: unknown) {
      toast.error(e instanceof Error ? e.message : t('batchFileError'));
    } finally {
      setDownloading(false);
    }
  }

  async function run(mode: 'preview' | 'finalize') {
    if (mode === 'finalize'
      && !(await confirm({ description: t('finalizeConfirm'), variant: 'destructive' }))) return;
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
      <PageHeader title={t(titleKey)} />
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
        <button className="btn btn-sm btn-outline" data-testid="tf-download-pdf"
          onClick={() => openPdf(`tax-filings/${form}/pdf?period=${ym.replace('-', '')}`)
            .catch((e: unknown) => toast.error(e instanceof Error ? e.message : 'Error'))}>
          {t('downloadPdf')}
        </button>
        {canBatch && (
          <button className="btn btn-sm btn-outline" data-testid="tf-batch-file"
            disabled={downloading} onClick={downloadBatch} title={t('batchFileHint')}>
            {downloading ? t('downloading') : t('downloadBatchFile')}
          </button>
        )}
        {filing && (
          <span data-testid="tf-status"
            className={`badge ${filing.status === 'Preview' ? 'badge-ghost' : 'badge-success'}`}>
            {filing.status} · {filing.submissionMode}
          </span>
        )}
      </div>

      {canBatch && (
        <RdPrepSteps formLabel={FORM_LABEL[form]} showPnd3Note={form === 'pnd3'} />
      )}

      {filing && (
        <>
          <div className="mb-3 text-sm text-base-content/70">
            {t('due')} {filing.filingDueDate}
          </div>
          <div className="overflow-x-auto rounded-lg border border-base-300">
            <table className="table table-zebra">
              <thead>
                <tr>
                  <th>{t('certNo')}</th><th>{t('payee')}</th><th>{t('payeeTaxId')}</th>
                  <th>{t('incomeType')}</th>
                  <th className="text-right">{t('income')}</th>
                  <th className="text-right">{t('whtRate')}</th>
                  <th className="text-right">{t('whtAmount')}</th>
                </tr>
              </thead>
              <tbody>
                {filing.rows.length === 0 && (
                  <tr><td colSpan={7} className="py-6 text-center text-base-content/50">—</td></tr>
                )}
                {filing.rows.map((r, i) => (
                  <tr key={i}>
                    <td className="font-mono">{r.certNo}</td>
                    <td>{r.payeeName}</td>
                    <td className="font-mono">{r.payeeTaxId ?? '—'}</td>
                    <td>{r.incomeTypeCode}</td>
                    <td className="text-right tabular-nums">{formatTHB(r.incomeAmount)}</td>
                    <td className="text-right tabular-nums">{(r.whtRate * 100).toFixed(2)}%</td>
                    <td className="text-right tabular-nums">{formatTHB(r.whtAmount)}</td>
                  </tr>
                ))}
              </tbody>
              <tfoot>
                <tr className="font-bold">
                  <td colSpan={4} className="text-right">{t('total')}</td>
                  <td className="text-right tabular-nums">{formatTHB(filing.totals.income)}</td>
                  <td />
                  <td className="text-right tabular-nums">{formatTHB(filing.totals.wht)}</td>
                </tr>
              </tfoot>
            </table>
          </div>
        </>
      )}
    </>
  );
}
