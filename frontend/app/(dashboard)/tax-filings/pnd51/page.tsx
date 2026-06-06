'use client';

import { useState } from 'react';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { PageHeader } from '@/components/ui/PageHeader';
import { openPdf } from '@/lib/api';

export default function Pnd51Page() {
  const t = useTranslations('tf');
  const [year, setYear] = useState(new Date().getFullYear());
  const [estimatedProfit, setEstimatedProfit] = useState('');
  const [whtH1, setWhtH1] = useState('');
  const [isSme, setIsSme] = useState(false);
  const [loading, setLoading] = useState(false);

  async function generate() {
    setLoading(true);
    try {
      const params = new URLSearchParams({ year: String(year) });
      if (estimatedProfit) params.set('estimatedProfit', estimatedProfit);
      if (whtH1) params.set('whtH1', whtH1);
      if (isSme) params.set('isSme', 'true');
      await openPdf(`tax-filings/pnd51/pdf?${params}`);
    } catch {
      toast.error(t('pnd51Error'));
    } finally {
      setLoading(false);
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
          disabled={loading}
          onClick={generate}
        >
          {loading ? t('downloading') : t('pnd51Generate')}
        </button>
      </div>
      <p className="text-xs text-base-content/60">{t('pnd51Hint')}</p>
    </>
  );
}
