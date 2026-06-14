'use client';

import { useParams, useRouter } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { Printer, Trash2, FileText } from 'lucide-react';
import { PageHeader } from '@/components/ui/PageHeader';
import { PermissionGate } from '@/components/PermissionGate';
import { useConfirm } from '@/hooks/useConfirm';
import {
  usePayrollRun, useApprovePayrollRun, usePostPayrollRun, usePayPayrollRun, useDeletePayrollRun,
} from '@/lib/queries';
import { openPdf, downloadFile } from '@/lib/api';
import { errorToToast } from '@/lib/api/errors';
import { formatTHB } from '@/lib/utils';
import { PayrollStatusBadge, formatPeriod } from '../_status';

export default function PayrollRunDetailPage() {
  const params = useParams();
  const router = useRouter();
  const id = Number(params.id);
  const t = useTranslations('payroll');
  const tc = useTranslations('common');
  const confirm = useConfirm();

  const q = usePayrollRun(id);
  const approve = useApprovePayrollRun();
  const post = usePostPayrollRun();
  const pay = usePayPayrollRun();
  const del = useDeletePayrollRun();
  const run = q.data;

  async function act(fn: () => Promise<unknown>, ok: string, confirmMsg?: string) {
    if (confirmMsg && !(await confirm({ description: confirmMsg }))) return;
    try { await fn(); toast.success(ok); } catch (e) { toast.error(errorToToast(e)); }
  }

  async function pr(path: string) {
    try { await openPdf(path); } catch (e) { toast.error(errorToToast(e)); }
  }

  async function dl(path: string, filename: string) {
    try { await downloadFile(path, filename); } catch (e) { toast.error(errorToToast(e)); }
  }

  if (q.isLoading) return <div className="p-6 text-ink-500">{tc('loading')}</div>;
  if (!run) return <div className="p-6 text-ink-500">{tc('notFound')}</div>;

  const busy = approve.isPending || post.isPending || pay.isPending || del.isPending;

  return (
    <>
      <PageHeader
        title={`${t('title')} · ${formatPeriod(run.periodYearMonth)}`}
        subtitle={run.docNo ?? undefined}
        actions={
          <div className="flex items-center gap-2">
            <PayrollStatusBadge status={run.status} isPaid={run.paidAt != null} />
            {run.status === 'DRAFT' && (
              <PermissionGate scope="payroll.run.post">
                <button data-testid="pr-approve" className="btn btn-primary btn-sm" disabled={busy}
                  onClick={() => act(() => approve.mutateAsync(id), t('approved'))}>{t('approve')}</button>
              </PermissionGate>
            )}
            {run.status === 'APPROVED' && (
              <PermissionGate scope="payroll.run.post">
                <button className="btn btn-primary btn-sm" disabled={busy}
                  onClick={() => act(() => post.mutateAsync(id), t('posted'), t('postConfirm'))}>{t('post')}</button>
              </PermissionGate>
            )}
            {run.status === 'POSTED' && run.paidAt == null && (
              <PermissionGate scope="payroll.run.pay">
                <button className="btn btn-success btn-sm" disabled={busy}
                  onClick={() => act(() => pay.mutateAsync(id), t('paid'), t('payConfirm'))}>{t('pay')}</button>
              </PermissionGate>
            )}
            {run.status === 'DRAFT' && (
              <PermissionGate scope="payroll.run.manage">
                <button data-testid="pr-delete" className="btn btn-ghost btn-sm text-error gap-1" disabled={busy}
                  onClick={() => act(async () => { await del.mutateAsync(id); router.push('/payroll'); }, tc('delete'), t('deleteConfirm'))}>
                  <Trash2 className="h-4 w-4" aria-hidden /> {tc('delete')}
                </button>
              </PermissionGate>
            )}
          </div>
        }
      />

      {/* Totals */}
      <div className="mb-4 grid grid-cols-2 gap-3 sm:grid-cols-4">
        <Stat label={t('totalGross')} value={formatTHB(run.totalGrossTaxable + run.totalGrossNonTaxable)} />
        <Stat label={t('totalPit')} value={formatTHB(run.totalPit)} />
        <Stat label={t('totalSso')} value={formatTHB(run.totalSsoEmployee + run.totalSsoEmployer)} />
        <Stat label={t('totalNet')} value={formatTHB(run.totalNet)} strong />
      </div>

      <div className="mb-3 flex items-center justify-between">
        <h2 className="font-semibold">{t('payslips')} ({run.payslips.length})</h2>
        {run.status === 'POSTED' && (
          <div className="flex items-center gap-2">
            <button className="btn btn-outline btn-sm gap-1"
              onClick={() => pr(`payroll/runs/${id}/pnd1/pdf`)}>
              <Printer className="h-4 w-4" aria-hidden /> {t('pnd1')}
            </button>
            <button className="btn btn-outline btn-sm gap-1"
              onClick={() => dl(`payroll/runs/${id}/sso/file`, `sps1-10_${run.periodYearMonth}.txt`)}>
              <FileText className="h-4 w-4" aria-hidden /> {t('ssoFile')}
            </button>
            <button className="btn btn-outline btn-sm gap-1"
              onClick={() => pr(`payroll/runs/${id}/sso/pdf`)}>
              <Printer className="h-4 w-4" aria-hidden /> {t('ssoPdf')}
            </button>
          </div>
        )}
      </div>

      <div className="overflow-x-auto rounded-box bg-base-100">
        <table className="table table-zebra">
          <thead>
            <tr>
              <th>{t('employee')}</th>
              <th className="text-right">{t('gross')}</th>
              <th className="text-right">{t('pit')}</th>
              <th className="text-right">{t('sso')}</th>
              <th className="text-right">{t('net')}</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {run.payslips.map((p) => (
              <tr key={p.payslipId}>
                <td>
                  <div className="font-medium">{p.employeeName}</div>
                  <div className="font-mono text-xs text-ink-500">{p.employeeCode}</div>
                </td>
                <td className="text-right tabular-nums">{formatTHB(p.grossTaxable + p.grossNonTaxable)}</td>
                <td className="text-right tabular-nums">{formatTHB(p.pitWithheld)}</td>
                <td className="text-right tabular-nums">{formatTHB(p.ssoEmployee)}</td>
                <td className="text-right font-medium tabular-nums">{formatTHB(p.netPay)}</td>
                <td className="text-right">
                  <button className="btn btn-ghost btn-xs gap-1"
                    onClick={() => pr(`payroll/runs/${id}/payslips/${p.employeeId}/pdf`)}>
                    <Printer className="h-3 w-3" aria-hidden /> {tc('print')}
                  </button>
                  {run.status === 'POSTED' && (
                    // P-D #4 — annual 50ทวิ (ม.50ทวิ) for this employee; year = the run's
                    // PAYMENT year (same ม.59 basis as ภ.ง.ด.1/1ก).
                    <button className="btn btn-ghost btn-xs gap-1"
                      onClick={() => pr(`payroll/employees/${p.employeeId}/wht50tawi/pdf`
                        + `?year=${new Date(run.payDate).getFullYear()}`)}>
                      <FileText className="h-3 w-3" aria-hidden /> {t('wht50tawi')}
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </>
  );
}

function Stat({ label, value, strong }: { label: string; value: string; strong?: boolean }) {
  return (
    <div className="rounded-box border border-ink-100 bg-base-100 p-3">
      <div className="text-xs text-ink-500">{label}</div>
      <div className={`tabular-nums ${strong ? 'text-lg font-bold' : 'font-medium'}`}>{value}</div>
    </div>
  );
}
