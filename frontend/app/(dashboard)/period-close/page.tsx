'use client';

import { useState } from 'react';
import { useTranslations, useLocale } from 'next-intl';
import { toast } from 'sonner';
import { ShieldAlert } from 'lucide-react';
import { PageHeader } from '@/components/ui/PageHeader';
import { ConfirmActionDialog } from '@/components/ui/ConfirmActionDialog';
import { PermissionGate } from '@/components/PermissionGate';
import { useConfirm } from '@/hooks/useConfirm';
import { useYearStatus, useClosePeriod, useReopenPeriod, useCloseYear, useReopenYear, useMePermissions } from '@/lib/queries';
import { errorToToast } from '@/lib/api/errors';
import { formatTHB } from '@/lib/utils';

// gl.period.close / gl.year.close — the same permissions
// PeriodEndpoints.MapPeriodEndpoints requires on the matching POST routes.
const MONTH_PERM = 'gl.period.close';
const YEAR_PERM = 'gl.year.close';

/** Format a 1-based month number as a long label (e.g. มกราคม / January) using
 *  the active locale — mirrors app/(dashboard)/page.tsx's monthFull(). */
function monthFull(locale: string, month1: number): string {
  return new Intl.DateTimeFormat(locale, { month: 'long' }).format(new Date(2000, month1 - 1, 1));
}

export default function PeriodClosePage() {
  const t = useTranslations('periodClose');
  const tc = useTranslations('common');
  const locale = useLocale();
  const confirm = useConfirm();
  const perms = useMePermissions();
  const [year, setYear] = useState(new Date().getFullYear());

  // Cycle A #7 follow-up (year-end closing #5) — ONE call for all 12 fiscal
  // months' status + real closedAt, replacing the earlier 12-way useQueries
  // fan-out (GET /periods/{y}/{m}/status had no closedAt at all).
  const status = useYearStatus(year);
  const closePeriod = useClosePeriod();
  const reopenPeriod = useReopenPeriod();
  const closeYear = useCloseYear();
  const reopenYear = useReopenYear();

  const [busyMonth, setBusyMonth] = useState<number | null>(null);
  const [reopenMonth, setReopenMonth] = useState<{ year: number; month: number } | null>(null);
  const [yearNotes, setYearNotes] = useState('');
  const [yearActionBusy, setYearActionBusy] = useState(false);

  async function handleCloseMonth(pYear: number, pMonth: number) {
    const label = monthFull(locale, pMonth);
    const ok = await confirm({
      title: t('closeConfirmTitle'),
      description: t('closeConfirmDesc', { month: label, year: pYear }),
      confirmText: t('close'),
      variant: 'destructive',
    });
    if (!ok) return;

    setBusyMonth(pMonth);
    try {
      await closePeriod.mutateAsync({ year: pYear, month: pMonth });
      toast.success(t('closeSuccess', { month: label }));
    } catch (e) {
      toast.error(errorToToast(e));
    } finally {
      setBusyMonth(null);
    }
  }

  async function handleReopenMonth() {
    if (!reopenMonth) return;
    const target = reopenMonth;
    const label = monthFull(locale, target.month);
    setBusyMonth(target.month);
    try {
      await reopenPeriod.mutateAsync(target);
      toast.success(t('reopenSuccess', { month: label }));
      setReopenMonth(null);
    } catch (e) {
      toast.error(errorToToast(e));
    } finally {
      setBusyMonth(null);
    }
  }
  async function handleCloseYear() {
    const ok = await confirm({
      title: t('closeYearConfirmTitle', { year }),
      description: t('closeYearConfirmDesc', { year }),
      confirmText: t('closeYear'),
      variant: 'destructive',
    });
    if (!ok) return;

    setYearActionBusy(true);
    try {
      await closeYear.mutateAsync({ year, notes: yearNotes.trim() || undefined });
      toast.success(t('closeYearSuccess', { year }));
      setYearNotes('');
    } catch (e) {
      toast.error(errorToToast(e));
    } finally {
      setYearActionBusy(false);
    }
  }

  async function handleReopenYear() {
    const ok = await confirm({
      title: t('reopenYearConfirmTitle', { year }),
      description: t('reopenYearConfirmDesc', { year }),
      confirmText: t('reopenYear'),
      variant: 'destructive',
    });
    if (!ok) return;

    setYearActionBusy(true);
    try {
      await reopenYear.mutateAsync(year);
      toast.success(t('reopenYearSuccess', { year }));
    } catch (e) {
      toast.error(errorToToast(e));
    } finally {
      setYearActionBusy(false);
    }
  }

  const canAccess = perms.data?.isSuperAdmin || (perms.data?.permissions.includes(MONTH_PERM) ?? false);
  if (perms.data && !canAccess) {
    return (
      <>
        <PageHeader title={t('title')} />
        <div className="flex flex-col items-center gap-2 py-12 text-center" data-testid="state-no-access">
          <ShieldAlert className="h-10 w-10 text-warning" aria-hidden />
          <div className="font-semibold">{tc('noAccessTitle')}</div>
          <div className="max-w-md text-sm text-base-content/60">{tc('noAccessBody', { perm: MONTH_PERM })}</div>
        </div>
      </>
    );
  }

  const fy = status.data;

  return (
    <>
      <PageHeader title={t('title')} />

      <div className="mb-4 flex flex-wrap items-end gap-3">
        <label className="form-control">
          <span className="label-text text-xs">{t('year')}</span>
          <input
            type="number"
            className="input input-bordered input-sm w-28"
            value={year}
            min={2000}
            max={2200}
            onChange={(e) => setYear(Number(e.target.value))}
          />
        </label>
      </div>

      {/* Year-end closing (#5) — fiscal-year-level close/reopen, gl.year.close. */}
      <div className="mb-6 rounded-lg border border-base-300 p-4">
        <h2 className="mb-3 font-semibold">{t('yearSectionTitle')}</h2>
        {status.isLoading && <span className="text-sm text-base-content/50">{tc('loading')}</span>}
        {fy && (fy.isClosed ? (
          <div className="flex flex-wrap items-center gap-4 text-sm">
            <span className="badge badge-success">{t('closed')}</span>
            <span>
              {t('netProfit')}:{' '}
              <span className="font-semibold tabular-nums">{formatTHB(fy.netProfit ?? 0)}</span>
            </span>
            <span className="text-base-content/60">
              {t('closedAt')}: {fy.closedAt?.slice(0, 10) ?? '—'}
            </span>
            <PermissionGate scope={YEAR_PERM}>
              <button
                className="btn btn-outline btn-error btn-sm ml-auto"
                disabled={yearActionBusy}
                onClick={handleReopenYear}
              >
                {yearActionBusy && <span className="loading loading-spinner loading-xs" />}
                {t('reopenYear')}
              </button>
            </PermissionGate>
          </div>
        ) : (
          <div className="flex flex-wrap items-end gap-3">
            <label className="form-control min-w-[16rem] flex-1">
              <span className="label-text text-xs">{t('notesOptional')}</span>
              <input
                className="input input-bordered input-sm"
                value={yearNotes}
                placeholder={t('notesPlaceholder')}
                onChange={(e) => setYearNotes(e.target.value)}
              />
            </label>
            <PermissionGate scope={YEAR_PERM}>
              <button
                className="btn btn-error btn-sm"
                disabled={!fy.allPeriodsClosed || yearActionBusy}
                onClick={handleCloseYear}
              >
                {yearActionBusy && <span className="loading loading-spinner loading-xs" />}
                {t('closeYear')}
              </button>
            </PermissionGate>
            {!fy.allPeriodsClosed && (
              <span className="text-xs text-base-content/60">{t('closeYearNeedsAllPeriods')}</span>
            )}
          </div>
        ))}
      </div>

      <div className="overflow-x-auto rounded-lg border border-base-300">
        <table className="table table-zebra">
          <thead>
            <tr>
              <th>{t('month')}</th>
              <th>{t('status')}</th>
              <th>{t('closedAt')}</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {status.isLoading && (
              <tr><td colSpan={4} className="py-6 text-center text-base-content/50">{tc('loading')}</td></tr>
            )}
            {/* Periods come back in FISCAL order (fiscalYearStartMonth may not be
                1), each carrying its own calendar year — never assume it equals
                the selected fiscal year, and always label both. */}
            {fy?.periods.map((p) => {
              const open = p.status === 'Open';
              return (
                <tr key={`${p.year}-${p.month}`}>
                  <td>{monthFull(locale, p.month)} {p.year}</td>
                  <td>
                    <span className={`badge ${open ? 'badge-success' : 'badge-ghost'}`}>
                      {open ? t('open') : t('closed')}
                    </span>
                  </td>
                  <td className="tabular-nums">{p.closedAt?.slice(0, 10) ?? '—'}</td>
                  <td className="text-right">
                    {open && (
                      <PermissionGate scope={MONTH_PERM}>
                        <button
                          className="btn btn-error btn-xs"
                          disabled={busyMonth === p.month}
                          onClick={() => handleCloseMonth(p.year, p.month)}
                        >
                          {busyMonth === p.month && <span className="loading loading-spinner loading-xs" />}
                          {t('close')}
                        </button>
                      </PermissionGate>
                    )}
                    {!open && (
                      <PermissionGate scope={MONTH_PERM}>
                        <button
                          className="btn btn-outline btn-warning btn-xs"
                          disabled={busyMonth === p.month}
                          onClick={() => setReopenMonth({ year: p.year, month: p.month })}
                        >
                          {busyMonth === p.month && <span className="loading loading-spinner loading-xs" />}
                          {t('reopen')}
                        </button>
                      </PermissionGate>
                    )}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>

      <ConfirmActionDialog
        open={reopenMonth !== null}
        busy={reopenPeriod.isPending}
        title={t('reopenConfirmTitle', {
          month: reopenMonth ? monthFull(locale, reopenMonth.month) : '',
          year: reopenMonth?.year ?? '',
        })}
        warning={t('reopenConfirmWarning')}
        confirmLabel={t('reopen')}
        onClose={() => setReopenMonth(null)}
        onConfirm={handleReopenMonth}
      />
    </>
  );
}
