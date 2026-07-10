'use client';

import { useState } from 'react';
import Link from 'next/link';
import { useTranslations, useLocale } from 'next-intl';
import { toast } from 'sonner';
import { PageHeader } from '@/components/ui/PageHeader';
import { PermissionGate } from '@/components/PermissionGate';
import { useConfirm } from '@/hooks/useConfirm';
import { useDepreciationRuns, useGenerateDepreciation } from '@/lib/queries';
import { ApiError, problemToast } from '@/lib/api';

const money = (n: number) => n.toLocaleString('th-TH', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

/** Format a 1-based month number as a long label using the active locale — mirrors
 *  app/(dashboard)/period-close/page.tsx's monthFull(). */
function monthFull(locale: string, month1: number): string {
  return new Intl.DateTimeFormat(locale, { month: 'long' }).format(new Date(2000, month1 - 1, 1));
}

// Cycle D (specs/fixed-assets.md §9.15) — month/year picker + "Generate depreciation"
// (gated fixedasset.depreciation.run) + past-runs list. Mirrors period-close/page.tsx's
// confirm-then-mutate idiom; depreciation.already_posted (422) is surfaced as a graceful
// toast, not a raw error screen.
export default function DepreciationPage() {
  const t = useTranslations('depreciation');
  const tc = useTranslations('common');
  const locale = useLocale();
  const confirm = useConfirm();
  const today = new Date();
  const [year, setYear] = useState(today.getFullYear());
  const [month, setMonth] = useState(today.getMonth() + 1);

  const runs = useDepreciationRuns();
  const generate = useGenerateDepreciation();

  async function handleGenerate() {
    const label = monthFull(locale, month);
    const ok = await confirm({
      title: t('generateConfirmTitle', { month: label, year }),
      description: t('generateConfirmDesc'),
      confirmText: t('generate'),
      variant: 'destructive',
    });
    if (!ok) return;

    try {
      const res = await generate.mutateAsync({ year, month });
      if (res.depreciationRunId == null) {
        toast.success(t('noAssetsDue'));
      } else {
        toast.success(t('generateSuccess', { month: label, year }));
      }
    } catch (e) {
      if (e instanceof ApiError && e.code === 'depreciation.already_posted') {
        toast.info(t('alreadyPosted'));
      } else {
        problemToast(e, tc('error'));
      }
    }
  }

  return (
    <>
      <PageHeader title={t('title')} />

      <div className="mb-6 flex flex-wrap items-end gap-3 rounded-card border border-ink-100 bg-base-100 p-4 shadow-warm-sm">
        <label className="form-control">
          <span className="label-text text-xs">{t('year')}</span>
          <input type="number" className="input input-bordered input-sm w-28" value={year}
            min={2000} max={2200} onChange={(e) => setYear(Number(e.target.value))} />
        </label>
        <label className="form-control">
          <span className="label-text text-xs">{t('month')}</span>
          <select className="select select-bordered select-sm w-40" value={month}
            onChange={(e) => setMonth(Number(e.target.value))}>
            {Array.from({ length: 12 }, (_, i) => i + 1).map((m) => (
              <option key={m} value={m}>{monthFull(locale, m)}</option>
            ))}
          </select>
        </label>
        <PermissionGate scope="fixedasset.depreciation.run">
          <button className="btn btn-primary btn-sm" disabled={generate.isPending} onClick={handleGenerate}>
            {generate.isPending && <span className="loading loading-spinner loading-xs" />}
            {t('generate')}
          </button>
        </PermissionGate>
      </div>

      <h2 className="mb-3 font-semibold">{t('pastRuns')}</h2>
      <div className="overflow-x-auto rounded-lg border border-base-300">
        <table className="table table-zebra">
          <thead>
            <tr>
              <th>{t('month')}</th>
              <th>{t('runDate')}</th>
              <th className="text-right">{t('totalAmount')}</th>
              <th className="text-right">{t('assetCount')}</th>
              <th>{t('journalEntry')}</th>
            </tr>
          </thead>
          <tbody>
            {runs.isLoading && (
              <tr><td colSpan={5} className="py-6 text-center text-base-content/50">{tc('loading')}</td></tr>
            )}
            {!runs.isLoading && (runs.data ?? []).length === 0 && (
              <tr><td colSpan={5} className="py-8 text-center text-base-content/50">{t('noRuns')}</td></tr>
            )}
            {(runs.data ?? []).map((r) => (
              <tr key={r.depreciationRunId}>
                <td>{monthFull(locale, r.periodMonth)} {r.periodYear}</td>
                <td className="tabular-nums">{r.runDate}</td>
                <td className="text-right tabular-nums">{money(r.totalAmount)}</td>
                <td className="text-right tabular-nums">{r.assetCount}</td>
                <td>
                  <Link href={`/journals/${r.journalEntryId}`} className="link link-primary text-sm">
                    {r.journalEntryId}
                  </Link>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </>
  );
}
