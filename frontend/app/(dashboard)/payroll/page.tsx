'use client';

import { useMemo, useState } from 'react';
import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { Plus, Printer } from 'lucide-react';
import type { ColumnDef } from '@tanstack/react-table';
import { PageHeader } from '@/components/ui/PageHeader';
import { DataTable } from '@/components/ui/DataTable';
import { PermissionGate } from '@/components/PermissionGate';
import { usePayrollRuns, useCreatePayrollRun } from '@/lib/queries';
import { openPdf } from '@/lib/api';
import type { PayrollRunListItem } from '@/lib/types';
import { errorToToast } from '@/lib/api/errors';
import { formatTHB } from '@/lib/utils';
import { PayrollStatusBadge, formatPeriod } from './_status';

const SCOPE = 'payroll.run.manage';

function thisPeriod() {
  const d = new Date();
  return `${d.getFullYear()}${String(d.getMonth() + 1).padStart(2, '0')}`;
}
function lastDayIso(period: string) {
  const y = Number(period.slice(0, 4)); const m = Number(period.slice(4));
  return new Date(y, m, 0).toISOString().slice(0, 10);   // last day of the period month
}

export default function PayrollRunsPage() {
  const t = useTranslations('payroll');
  const tc = useTranslations('common');
  const q = usePayrollRuns();
  const create = useCreatePayrollRun();
  const [open, setOpen] = useState(false);
  const [period, setPeriod] = useState(thisPeriod());
  const [payDate, setPayDate] = useState(lastDayIso(thisPeriod()));
  const [notes, setNotes] = useState('');
  const [annualYear, setAnnualYear] = useState(new Date().getFullYear());

  async function printAnnual() {
    try { await openPdf(`payroll/pnd1a/pdf?year=${annualYear}`); }
    catch (e) { toast.error(errorToToast(e)); }
  }

  const rows = q.data ?? [];
  const periodOk = /^\d{6}$/.test(period) && Number(period.slice(4)) >= 1 && Number(period.slice(4)) <= 12;

  const columns = useMemo<ColumnDef<PayrollRunListItem>[]>(() => [
    {
      accessorKey: 'periodYearMonth', header: t('period'),
      cell: ({ row, getValue }) => (
        <Link href={`/payroll/${row.original.payrollRunId}`} className="link link-hover font-medium">
          {formatPeriod(getValue<string>())}
        </Link>
      ),
    },
    { accessorKey: 'payDate', header: t('payDate'), cell: ({ getValue }) => getValue<string>().slice(0, 10) },
    {
      accessorFn: (r) => r.status, id: 'status', header: tc('status'), meta: { filter: 'select' },
      cell: ({ row }) => <PayrollStatusBadge status={row.original.status} isPaid={row.original.isPaid} />,
    },
    { accessorKey: 'docNo', header: t('docNo'), cell: ({ getValue }) => <span className="font-mono">{getValue<string>() ?? '—'}</span> },
    { accessorKey: 'employeeCount', header: t('employeeCount'), meta: { align: 'right' } },
    {
      accessorKey: 'totalNet', header: t('totalNet'), meta: { align: 'right' },
      cell: ({ getValue }) => <span className="tabular-nums">{formatTHB(getValue<number>())}</span>,
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
  ], [t, tc]);

  async function submit() {
    try {
      await create.mutateAsync({ periodYearMonth: period, payDate, notes: notes || null });
      toast.success(tc('save'));
      setOpen(false); setNotes('');
    } catch (e) { toast.error(errorToToast(e)); }
  }

  return (
    <>
      <PageHeader
        title={t('title')} subtitle={t('subtitle')}
        actions={
          <PermissionGate scope={SCOPE}>
            <div className="flex items-center gap-2">
              <input type="number" className="input input-bordered input-sm w-24 tabular-nums"
                value={annualYear} onChange={(e) => setAnnualYear(Number(e.target.value) || annualYear)}
                title={t('annualYear')} />
              <button className="btn btn-outline btn-sm gap-1" onClick={printAnnual}>
                <Printer className="h-4 w-4" aria-hidden /> {t('pnd1a')}
              </button>
              <button className="btn btn-primary btn-sm gap-1" onClick={() => { const p = thisPeriod(); setPeriod(p); setPayDate(lastDayIso(p)); setOpen(true); }}>
                <Plus className="h-4 w-4" aria-hidden /> {t('create')}
              </button>
            </div>
          </PermissionGate>
        }
      />

      <DataTable
        data={rows}
        columns={columns}
        isLoading={q.isLoading}
        getRowId={(r) => String(r.payrollRunId)}
        searchPlaceholder={t('docNo')}
      />

      {open && (
        <div className="modal modal-open" role="dialog" aria-modal="true">
          <div className="modal-box">
            <h3 className="text-lg font-bold">{t('create')}</h3>
            <p className="mt-1 text-sm text-ink-500">{t('createHint')}</p>
            <div className="mt-4 grid grid-cols-1 gap-3 sm:grid-cols-2">
              <label className="form-control">
                <span className="label-text">{t('period')} *</span>
                <input className="input input-bordered font-mono" value={period} maxLength={6}
                  placeholder="YYYYMM"
                  onChange={(e) => setPeriod(e.target.value.replace(/\D/g, ''))} />
                {!periodOk && period !== '' && <span className="label-text-alt text-error">{t('periodInvalid')}</span>}
              </label>
              <label className="form-control">
                <span className="label-text">{t('payDate')} *</span>
                <input type="date" className="input input-bordered" value={payDate}
                  onChange={(e) => setPayDate(e.target.value)} />
              </label>
              <label className="form-control sm:col-span-2">
                <span className="label-text">{t('notes')}</span>
                <input className="input input-bordered" value={notes} onChange={(e) => setNotes(e.target.value)} />
              </label>
            </div>
            <div className="modal-action">
              <button className="btn btn-ghost" onClick={() => setOpen(false)}>{tc('cancel')}</button>
              <button className="btn btn-primary" disabled={!periodOk || create.isPending} onClick={submit}>
                {t('create')}
              </button>
            </div>
          </div>
          <button className="modal-backdrop" aria-label="close" onClick={() => setOpen(false)} />
        </div>
      )}
    </>
  );
}
