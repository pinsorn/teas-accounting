'use client';

import { useState } from 'react';
import { useTranslations } from 'next-intl';
import { ShieldCheck, ShieldAlert } from 'lucide-react';
import { PageHeader } from '@/components/ui/PageHeader';
import { useNumberGaps } from '@/lib/queries';

// Design(UI).md §13.3 — simple table, default empty (= compliant green state).
export default function NumberGapAuditPage() {
  const t = useTranslations('numberGaps');
  const tc = useTranslations('common');
  const [year, setYear] = useState<number | undefined>();
  const [month, setMonth] = useState<number | undefined>();
  const [docType, setDocType] = useState<string | undefined>();

  const { data, isLoading, isError } = useNumberGaps(year, month, docType);
  const gaps = data?.gaps ?? [];
  const clean = !isLoading && !isError && gaps.length === 0;

  return (
    <>
      <PageHeader title={t('title')} subtitle={t('subtitle')} />

      <div className="mb-4 flex flex-wrap items-end gap-3">
        <label className="form-control">
          <span className="label-text text-xs">{t('year')}</span>
          <input type="number" className="input input-bordered input-sm w-28"
            onChange={(e) => setYear(e.target.value ? Number(e.target.value) : undefined)} />
        </label>
        <label className="form-control">
          <span className="label-text text-xs">{t('month')}</span>
          <input type="number" min={1} max={12} className="input input-bordered input-sm w-24"
            onChange={(e) => setMonth(e.target.value ? Number(e.target.value) : undefined)} />
        </label>
        <label className="form-control">
          <span className="label-text text-xs">{t('docType')}</span>
          <input className="input input-bordered input-sm w-28" placeholder="TI / PV / JV"
            onChange={(e) => setDocType(e.target.value || undefined)} />
        </label>
      </div>

      {isLoading && <p className="text-base-content/50">{tc('loading')}</p>}
      {isError && <p className="text-error">{tc('error')}</p>}

      {clean && (
        <div role="status" className="alert alert-success">
          <ShieldCheck className="h-5 w-5" aria-hidden />
          <span>{t('clean')}</span>
        </div>
      )}

      {!isLoading && !isError && gaps.length > 0 && (
        <>
          <div role="alert" className="alert alert-error mb-4">
            <ShieldAlert className="h-5 w-5" aria-hidden />
            <span>{t('found')} ({gaps.length})</span>
          </div>
          <div className="overflow-x-auto rounded-lg border border-error/40">
            <table className="table">
              <thead>
                <tr><th>{t('series')}</th><th className="text-right">{t('missing')}</th></tr>
              </thead>
              <tbody>
                {gaps.map((g, i) => (
                  <tr key={`${g.series}-${g.missingSeqNo}-${i}`} className="text-error">
                    <td className="font-mono">{g.series}</td>
                    <td className="text-right font-mono tabular-nums">{g.missingSeqNo}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}
    </>
  );
}
