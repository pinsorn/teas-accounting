'use client';

import { useState } from 'react';
import { useParams } from 'next/navigation';
import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { Pencil, ShieldAlert } from 'lucide-react';
import { PageHeader } from '@/components/ui/PageHeader';
import { PermissionGate } from '@/components/PermissionGate';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { AttachmentsSection } from '@/components/attachments/AttachmentsSection';
import {
  useExpenseClaim, useSubmitExpenseClaim, useApproveExpenseClaim, useRejectExpenseClaim,
  usePayExpenseClaim, useCancelExpenseClaim, useBankAccounts, useMePermissions,
} from '@/lib/queries';
import { problemToast } from '@/lib/api';
import { bangkokToday } from '@/lib/utils';

const READ_SCOPE = 'expense.claim.read';

// Cycle C (specs/expense-claims.md §5) — detail + lifecycle actions. Simpler than
// payment-vouchers/[id] (no PaperDocument — a claim has no print layout in scope); mirrors
// its PermissionGate + disabled={m.isPending} idiom for every action button.
export default function ExpenseClaimDetailPage() {
  const id = Number(useParams<{ id: string }>().id);
  const t = useTranslations('expenseClaims');
  const tc = useTranslations('common');
  const { data: d, isLoading, isError } = useExpenseClaim(id);
  const submit = useSubmitExpenseClaim();
  const approve = useApproveExpenseClaim();
  const reject = useRejectExpenseClaim();
  const pay = usePayExpenseClaim();
  const cancel = useCancelExpenseClaim();
  const bankAccounts = useBankAccounts();
  const perms = useMePermissions();

  const [rejectOpen, setRejectOpen] = useState(false);
  const [rejectReason, setRejectReason] = useState('');
  const [payOpen, setPayOpen] = useState(false);
  const [payMethod, setPayMethod] = useState<'CASH' | 'TRANSFER'>('TRANSFER');
  const [payBankAccountId, setPayBankAccountId] = useState<number | null>(null);

  // D3 (specs/fix-army-findings-2026-07-22.md, B-ec F2) — same client-side permission-check
  // pattern as expense-claims/new/page.tsx: a 403 (no expense.claim.read) rendered the bare
  // generic "เกิดข้อผิดพลาด" instead of a permission-named clean-deny state.
  const canRead = perms.data?.isSuperAdmin || (perms.data?.permissions.includes(READ_SCOPE) ?? false);
  if (perms.isPending) return null;
  if (perms.data && !canRead) {
    return (
      <div className="flex flex-col items-center gap-2 py-12 text-center" data-testid="state-no-access">
        <ShieldAlert className="h-10 w-10 text-warning" aria-hidden />
        <div className="font-semibold">{tc('noAccessTitle')}</div>
        <div className="max-w-md text-sm text-base-content/60">{tc('noAccessBody', { perm: READ_SCOPE })}</div>
      </div>
    );
  }

  if (isLoading) return <p className="text-base-content/50">{tc('loading')}</p>;
  if (isError || !d) return <p className="text-error">{tc('error')}</p>;

  async function doSubmit() {
    try { await submit.mutateAsync(id); toast.success(t('submitted')); }
    catch (e) { problemToast(e, tc('error')); }
  }
  async function doApprove() {
    try { await approve.mutateAsync(id); toast.success(t('approved')); }
    catch (e) { problemToast(e, tc('error')); }
  }
  async function doReject() {
    if (!rejectReason.trim()) { toast.error(t('rejectReasonRequired')); return; }
    try {
      await reject.mutateAsync({ id, req: { reason: rejectReason.trim() } });
      toast.success(t('rejected'));
      setRejectOpen(false); setRejectReason('');
    } catch (e) { problemToast(e, tc('error')); }
  }
  async function doPay() {
    if (payMethod === 'TRANSFER' && !payBankAccountId) { toast.error(t('bankAccountRequired')); return; }
    try {
      await pay.mutateAsync({ id, req: { paymentMethod: payMethod, bankAccountId: payMethod === 'TRANSFER' ? payBankAccountId : null } });
      toast.success(t('paid'));
      setPayOpen(false);
    } catch (e) { problemToast(e, tc('error')); }
  }
  async function doCancel() {
    try { await cancel.mutateAsync(id); toast.success(t('cancelled')); }
    catch (e) { problemToast(e, tc('error')); }
  }

  return (
    <>
      <PageHeader
        title={t('title')}
        subtitle={d.docNo ?? undefined}
        actions={
          <div className="flex gap-2">
            {(d.status === 'Draft' || d.status === 'Rejected') && (
              <PermissionGate scope="expense.claim.create">
                <Link data-testid="ec-edit" href={`/expense-claims/${id}/edit`} className="btn btn-secondary btn-sm gap-1">
                  <Pencil className="h-4 w-4" aria-hidden /> {tc('edit')}
                </Link>
              </PermissionGate>
            )}
            {(d.status === 'Draft' || d.status === 'Rejected') && (
              <PermissionGate scope="expense.claim.create">
                <button data-testid="ec-submit" className="btn btn-secondary btn-sm" disabled={submit.isPending} onClick={doSubmit}>
                  {t('submit')}
                </button>
              </PermissionGate>
            )}
            {(d.status === 'Draft' || d.status === 'Rejected') && (
              <PermissionGate scope="expense.claim.create">
                <button data-testid="ec-cancel" className="btn btn-ghost btn-sm text-error" disabled={cancel.isPending} onClick={doCancel}>
                  {t('cancel')}
                </button>
              </PermissionGate>
            )}
            {d.status === 'Submitted' && (
              <PermissionGate scope="expense.claim.approve">
                <button data-testid="ec-approve" className="btn btn-secondary btn-sm" disabled={approve.isPending} onClick={doApprove}>
                  {t('approve')}
                </button>
              </PermissionGate>
            )}
            {d.status === 'Submitted' && (
              <PermissionGate scope="expense.claim.approve">
                <button data-testid="ec-reject" className="btn btn-ghost btn-sm text-error" disabled={reject.isPending}
                  onClick={() => setRejectOpen(true)}>
                  {t('reject')}
                </button>
              </PermissionGate>
            )}
            {d.status === 'Approved' && (
              <PermissionGate scope="expense.claim.pay">
                <button data-testid="ec-pay" className="btn btn-primary btn-sm" disabled={pay.isPending}
                  onClick={() => setPayOpen(true)}>
                  {t('pay')}
                </button>
              </PermissionGate>
            )}
          </div>
        }
      />

      <div className="mb-4 flex flex-wrap items-center gap-3">
        <StatusBadge status={d.status} />
        <span className="text-sm text-base-content/60">{d.claimDate}</span>
        {d.rejectReason && (
          <span className="text-sm text-error">{t('rejectReason')}: {d.rejectReason}</span>
        )}
      </div>

      <section className="rounded-card border border-ink-100 bg-base-100 p-5 shadow-warm-sm">
        <div className="grid grid-cols-1 gap-3 text-sm md:grid-cols-2">
          <div><span className="text-base-content/60">{t('employee')}:</span> {d.employeeName}</div>
          <div><span className="text-base-content/60">{t('claimTitle')}:</span> {d.title ?? '—'}</div>
          {d.paymentMethod && (
            <div><span className="text-base-content/60">{t('method')}:</span> {d.paymentMethod}</div>
          )}
          {d.notes && (
            <div className="md:col-span-2"><span className="text-base-content/60">{t('notes')}:</span> {d.notes}</div>
          )}
        </div>

        <div className="mt-4 overflow-x-auto rounded-lg border border-base-300">
          <table className="table table-sm">
            <thead>
              <tr>
                <th>{t('lineDescription')}</th>
                <th>{t('category')}</th>
                <th className="text-right">{t('amount')}</th>
                <th className="text-right">VAT</th>
                <th className="text-right">{t('lineTotal')}</th>
              </tr>
            </thead>
            <tbody>
              {d.lines.map((l) => (
                <tr key={l.expenseClaimLineId}>
                  <td>{l.description}</td>
                  <td>{l.categoryName}</td>
                  <td className="text-right">{l.amount.toLocaleString('th-TH', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}</td>
                  <td className="text-right">
                    {l.vatAmount.toLocaleString('th-TH', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}
                    {!l.isRecoverableVat && <span className="ml-1 text-warning">⚠</span>}
                  </td>
                  <td className="text-right">{l.lineTotal.toLocaleString('th-TH', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        <div className="mt-4 flex flex-col items-end gap-1 text-sm">
          <div>{t('subtotal')}: {d.subtotalAmount.toLocaleString('th-TH', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}</div>
          <div>{t('vat')}: {d.vatAmount.toLocaleString('th-TH', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}</div>
          <div className="text-lg font-bold">
            {t('totalAmount')}: {d.totalAmount.toLocaleString('th-TH', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}
          </div>
        </div>
      </section>

      <AttachmentsSection parentType="EXPENSE_CLAIM" parentId={id} />

      {rejectOpen && (
        <div className="modal modal-open">
          <div className="modal-box">
            <h3 className="mb-3 text-lg font-bold">{t('reject')}</h3>
            <label className="form-control">
              <span className="label-text">{t('rejectReason')} *</span>
              <textarea className="textarea textarea-bordered" value={rejectReason}
                onChange={(e) => setRejectReason(e.target.value)} />
            </label>
            <div className="modal-action">
              <button className="btn btn-ghost btn-sm" onClick={() => setRejectOpen(false)}>{tc('cancel')}</button>
              <button className="btn btn-error btn-sm" disabled={reject.isPending} onClick={doReject}>{t('reject')}</button>
            </div>
          </div>
        </div>
      )}

      {payOpen && (
        <div className="modal modal-open">
          <div className="modal-box">
            <h3 className="mb-3 text-lg font-bold">{t('pay')}</h3>
            {/* C2 item 3 (specs/fix-c2-fe-cleanup.md, r2 L4-7) — PAY posts the JE on the
                payment date, not the claim date. Informational only, never blocking. */}
            {d.claimDate.slice(0, 7) !== bangkokToday().slice(0, 7) && (
              <p className="mb-3 text-xs text-info">{t('backDatedPayNote')}</p>
            )}
            <label className="form-control">
              <span className="label-text">{t('method')}</span>
              <select className="select select-bordered" value={payMethod}
                onChange={(e) => setPayMethod(e.target.value as 'CASH' | 'TRANSFER')}>
                <option value="TRANSFER">TRANSFER</option>
                <option value="CASH">CASH</option>
              </select>
            </label>
            {payMethod === 'TRANSFER' && (
              <label className="form-control mt-2">
                <span className="label-text">{t('bankAccount')} *</span>
                <select className="select select-bordered" value={payBankAccountId ?? ''}
                  onChange={(e) => setPayBankAccountId(e.target.value ? Number(e.target.value) : null)}>
                  <option value="">{`— ${t('bankAccount')} —`}</option>
                  {(bankAccounts.data ?? []).map((b) => (
                    <option key={b.bankAccountId} value={b.bankAccountId}>{b.bankName} — {b.accountNo}</option>
                  ))}
                </select>
              </label>
            )}
            <div className="modal-action">
              <button className="btn btn-ghost btn-sm" onClick={() => setPayOpen(false)}>{tc('cancel')}</button>
              <button className="btn btn-primary btn-sm" disabled={pay.isPending} onClick={doPay}>{t('pay')}</button>
            </div>
          </div>
        </div>
      )}
    </>
  );
}
