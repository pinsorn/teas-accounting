'use client';

import { useState } from 'react';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { PageHeader } from '@/components/ui/PageHeader';
import { apiPost, openPdf } from '@/lib/api';

const ATTEST_KEYS = [
  'attestFirstFiling',
  'attestNoLossCf',
  'attestNoExemption',
  'attestNoRateReduction',
  'attestNoSurcharge',
] as const;
type AttestKey = (typeof ATTEST_KEYS)[number];

export default function Pnd51Page() {
  const t = useTranslations('tf');
  const [year, setYear] = useState(new Date().getFullYear());
  const [estimatedProfit, setEstimatedProfit] = useState('');
  const [whtH1, setWhtH1] = useState('');
  const [isSme, setIsSme] = useState(false);
  const [loading, setLoading] = useState(false);
  const [savingEstimate, setSavingEstimate] = useState(false);
  // Page-2 worksheet: ALL five attestations must be checked or the download stays disabled
  // (the backend additionally throws 422 pnd51.worksheet_not_attestable — this gate is UX, not the guard).
  const [fillWorksheet, setFillWorksheet] = useState(false);
  const [attest, setAttest] = useState<Record<AttestKey, boolean>>({
    attestFirstFiling: false,
    attestNoLossCf: false,
    attestNoExemption: false,
    attestNoRateReduction: false,
    attestNoSurcharge: false,
  });

  // v1: worksheet is general-rate only (SME rate radio not yet confirmed) — backend refuses isSme.
  const worksheetAllowed = !isSme;
  const allAttested = ATTEST_KEYS.every((k) => attest[k]);
  const blocked = fillWorksheet && (!allAttested || !worksheetAllowed);

  async function generate() {
    setLoading(true);
    try {
      const params = new URLSearchParams({ year: String(year) });
      if (estimatedProfit) params.set('estimatedProfit', estimatedProfit);
      if (whtH1) params.set('whtH1', whtH1);
      if (isSme) params.set('isSme', 'true');
      if (fillWorksheet) {
        params.set('fillWorksheet', 'true');
        for (const k of ATTEST_KEYS) params.set(k, String(attest[k]));
      }
      await openPdf(`tax-filings/pnd51/pdf?${params}`);
    } catch {
      toast.error(t('pnd51Error'));
    } finally {
      setLoading(false);
    }
  }

  // C-C — persist the filed method-A estimate so the year-end ม.67ตรี ±25% check
  // compares against exactly what was filed. Requires an explicit estimatedProfit
  // (the H1×2 auto-default is a preview convenience, not a filed figure).
  async function saveEstimate() {
    setSavingEstimate(true);
    try {
      const params = new URLSearchParams({
        year: String(year),
        estimatedProfit,
        whtH1: whtH1 || '0',
        isSme: String(isSme),
      });
      await apiPost(`tax-filings/pnd51/estimate?${params}`);
      toast.success(t('pnd51EstimateSaved'));
    } catch {
      toast.error(t('pnd51EstimateError'));
    } finally {
      setSavingEstimate(false);
    }
  }

  return (
    <>
      <PageHeader title={t('pnd51Title')} />
      <div className="mb-4 flex flex-wrap items-end gap-3">
        <label className="form-control">
          <span className="label-text text-xs">{t('pnd51Year')}</span>
          <input
            type="number"
            className="input input-bordered input-sm w-28"
            value={year}
            min={2000}
            max={2200}
            onChange={(e) => setYear(Number(e.target.value))}
          />
        </label>
        <label className="form-control">
          <span className="label-text text-xs">{t('pnd51EstimatedProfit')}</span>
          <input
            type="number"
            className="input input-bordered input-sm w-44"
            value={estimatedProfit}
            placeholder={t('pnd51EstimatedProfitHint')}
            onChange={(e) => setEstimatedProfit(e.target.value)}
          />
        </label>
        <label className="form-control">
          <span className="label-text text-xs">{t('pnd51WhtH1')}</span>
          <input
            type="number"
            className="input input-bordered input-sm w-36"
            value={whtH1}
            placeholder="0"
            onChange={(e) => setWhtH1(e.target.value)}
          />
        </label>
        <label className="flex cursor-pointer items-center gap-2 self-end pb-1.5">
          <input
            type="checkbox"
            className="checkbox checkbox-sm"
            checked={isSme}
            onChange={(e) => setIsSme(e.target.checked)}
          />
          <span className="label-text text-xs">{t('pnd51Sme')}</span>
        </label>
        <button
          className="btn btn-sm btn-primary self-end"
          disabled={loading || blocked}
          onClick={generate}
        >
          {loading ? t('downloading') : t('pnd51Generate')}
        </button>
        <button
          className="btn btn-sm btn-outline self-end"
          disabled={savingEstimate || !estimatedProfit}
          onClick={saveEstimate}
        >
          {savingEstimate && <span className="loading loading-spinner loading-xs" />}
          {t('pnd51SaveEstimate')}
        </button>
      </div>

      {/* ── page-2 การคำนวณภาษี worksheet + attestation gate ── */}
      <div className="mb-4 rounded-lg border border-base-300 p-3">
        <label className="flex cursor-pointer items-center gap-2">
          <input
            type="checkbox"
            className="toggle toggle-sm"
            checked={fillWorksheet}
            disabled={!worksheetAllowed}
            onChange={(e) => setFillWorksheet(e.target.checked)}
          />
          <span className="label-text text-sm font-medium">{t('pnd51Worksheet')}</span>
        </label>
        {!worksheetAllowed && (
          <p className="mt-1 text-xs text-warning">{t('pnd51WorksheetSme')}</p>
        )}
        {fillWorksheet && worksheetAllowed && (
          <div className="mt-2 flex flex-col gap-1.5">
            <p className="text-xs text-base-content/60">{t('pnd51WorksheetHint')}</p>
            {(
              [
                ['attestFirstFiling', t('pnd51AttestFirstFiling')],
                ['attestNoLossCf', t('pnd51AttestNoLossCf')],
                ['attestNoExemption', t('pnd51AttestNoExemption')],
                ['attestNoRateReduction', t('pnd51AttestNoRateReduction')],
                ['attestNoSurcharge', t('pnd51AttestNoSurcharge')],
              ] as [AttestKey, string][]
            ).map(([k, label]) => (
              <label key={k} className="flex cursor-pointer items-center gap-2">
                <input
                  type="checkbox"
                  className="checkbox checkbox-xs"
                  checked={attest[k]}
                  onChange={(e) => setAttest((a) => ({ ...a, [k]: e.target.checked }))}
                />
                <span className="label-text text-xs">{label}</span>
              </label>
            ))}
            {!allAttested && (
              <p className="text-xs text-error">{t('pnd51WorksheetIncomplete')}</p>
            )}
          </div>
        )}
      </div>

      <p className="text-xs text-base-content/60">{t('pnd51Hint')}</p>
    </>
  );
}
