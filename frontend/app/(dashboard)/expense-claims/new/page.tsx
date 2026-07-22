'use client';

import { useState } from 'react';
import { useRouter } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { Plus, ShieldAlert, Trash2 } from 'lucide-react';
import { PageHeader } from '@/components/ui/PageHeader';
import { EmployeeSelector } from '@/components/ui/EmployeeSelector';
import { ExpenseCategorySelector } from '@/components/ui/ExpenseCategorySelector';
import { BusinessUnitSelector } from '@/components/ui/BusinessUnitSelector';
import { useCreateExpenseClaim, useMePermissions, useSystemInfo } from '@/lib/queries';
import { problemToast } from '@/lib/api';
import { bangkokToday } from '@/lib/utils';

// Cycle C (specs/expense-claims.md §5) — multi-line create. Clones payment-vouchers/new's
// `rows` state + add/remove idiom (simplified — no PaperDocument preview / WHT machinery,
// neither applies to a self-contained cash disbursement). Save draft -> id -> user attaches
// receipts on the detail page (header parent, FOOTGUN 7) -> optional submit from there.
interface Row {
  key: number; expenseCategoryId: number | null; description: string;
  expenseDate: string; amount: number; vatRate: number; isRecoverableVat: boolean;
}
const emptyRow = (k: number): Row => ({
  key: k, expenseCategoryId: null, description: '', expenseDate: bangkokToday(),
  amount: 0, vatRate: 0, isRecoverableVat: true,
});

const SCOPE = 'expense.claim.create';

export default function NewExpenseClaimPage() {
  const t = useTranslations('expenseClaims');
  const tc = useTranslations('common');
  const router = useRouter();
  const create = useCreateExpenseClaim();
  const perms = useMePermissions();
  // F-B (specs/fix-purchase-nonvat-ux.md) — a non-VAT-registered company cannot credit input
  // VAT (no ภ.พ.30); same §4.6 vatRegistered/system-info hook the VAT-mode features already use
  // (mirrors vendor-invoices/new's companyVatRegistered), no new plumbing. Defaults true until
  // /system/info resolves, matching the VI form's own default.
  const companyVatRegistered = useSystemInfo().data?.vatMode ?? true;

  const [employeeId, setEmployeeId] = useState<number | null>(null);
  const [claimDate, setClaimDate] = useState(bangkokToday());
  const [title, setTitle] = useState('');
  const [notes, setNotes] = useState('');
  const [businessUnitId, setBusinessUnitId] = useState<number | null>(null);
  const [rows, setRows] = useState<Row[]>([emptyRow(1)]);
  const [busy, setBusy] = useState(false);

  function setRow(key: number, patch: Partial<Row>) {
    setRows((rs) => rs.map((r) => (r.key === key ? { ...r, ...patch } : r)));
  }

  // F-B — a non-VAT company never has a real per-line VAT rate (the select is hidden below);
  // defend the totals the same way even if a row's in-memory vatRate is stale.
  const effectiveVatRate = (r: Row) => (companyVatRegistered ? r.vatRate : 0);
  const subtotal = rows.reduce((s, r) => s + r.amount, 0);
  const vat = rows.reduce((s, r) => s + Math.round(r.amount * effectiveVatRate(r) * 100) / 100, 0);
  const total = subtotal + vat;

  const canSave =
    employeeId !== null && claimDate !== '' &&
    rows.every((r) => r.expenseCategoryId !== null && r.description.trim() !== '' && r.amount > 0);

  async function saveDraft() {
    if (!employeeId) return;
    setBusy(true);
    try {
      const res = await create.mutateAsync({
        employeeId,
        claimDate,
        title: title.trim() || null,
        notes: notes.trim() || null,
        businessUnitId,
        lines: rows.map((r) => ({
          expenseCategoryId: r.expenseCategoryId!,
          expenseAccountId: null,
          description: r.description,
          expenseDate: r.expenseDate || null,
          amount: r.amount,
          taxCodeId: null,
          // F-B (specs/fix-purchase-nonvat-ux.md) — a non-VAT company submits vatRate 0 /
          // isRecoverableVat false regardless of in-memory row state (the controls are hidden
          // below; the backend guard in ExpenseClaimService.BuildLinesAsync also enforces this).
          vatRate: companyVatRegistered ? r.vatRate : 0,
          isRecoverableVat: companyVatRegistered && r.isRecoverableVat,
        })),
      });
      toast.success(tc('save'));
      router.push(`/expense-claims/${res.expense_claim_id}`);
    } catch (e) {
      problemToast(e, tc('error'));
    } finally {
      setBusy(false);
    }
  }

  const canCreate = perms.data?.isSuperAdmin || (perms.data?.permissions.includes(SCOPE) ?? false);
  if (perms.isPending) return null;
  if (perms.data && !canCreate) {
    return (
      <div className="flex flex-col items-center gap-2 py-12 text-center" data-testid="state-no-access">
        <ShieldAlert className="h-10 w-10 text-warning" aria-hidden />
        <div className="font-semibold">{tc('noAccessTitle')}</div>
        <div className="max-w-md text-sm text-base-content/60">{tc('noAccessBody', { perm: SCOPE })}</div>
      </div>
    );
  }

  return (
    <>
      <PageHeader title={t('create')} />
      <div className="max-w-4xl space-y-5">
        <section className="rounded-card border border-ink-100 bg-base-100 p-5 shadow-warm-sm">
          <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
            <EmployeeSelector value={employeeId} onChange={setEmployeeId} />
            <label className="form-control">
              <span className="label-text">{t('claimDate')} *</span>
              <input type="date" className="input input-bordered" value={claimDate}
                onChange={(e) => setClaimDate(e.target.value)} aria-label={t('claimDate')} />
            </label>
            <label className="form-control">
              <span className="label-text">{t('claimTitle')}</span>
              <input className="input input-bordered" value={title}
                onChange={(e) => setTitle(e.target.value)} aria-label={t('claimTitle')} />
            </label>
            <BusinessUnitSelector value={businessUnitId} onChange={setBusinessUnitId} />
            <label className="form-control md:col-span-2">
              <span className="label-text">{t('notes')}</span>
              <textarea className="textarea textarea-bordered" value={notes}
                onChange={(e) => setNotes(e.target.value)} aria-label={t('notes')} />
            </label>
          </div>
        </section>

        <section className="rounded-card border border-ink-100 bg-base-100 p-5 shadow-warm-sm">
          <h2 className="mb-3 font-semibold">{t('lines')}</h2>
          <div className="space-y-3">
            {rows.map((r, i) => (
              <div key={r.key} className="grid grid-cols-1 gap-3 rounded-lg border border-base-300 p-3 md:grid-cols-6">
                <div className="md:col-span-2">
                  <ExpenseCategorySelector
                    value={r.expenseCategoryId}
                    onChange={(id, cat) => setRow(r.key, {
                      expenseCategoryId: id,
                      // F-B — a non-VAT company never defaults a line into "recoverable" (the
                      // checkbox is hidden below; saveDraft() also forces this at submit time).
                      isRecoverableVat: companyVatRegistered && cat.defaultIsRecoverableVat,
                    })}
                  />
                </div>
                {/* F-C (specs/fix-purchase-nonvat-ux.md) — default-height controls throughout
                    this row (matches the doc-create forms convention; ExpenseCategorySelector
                    above was always default-height, these siblings were the -sm outliers). */}
                <label className="form-control">
                  <span className="label-text">{t('lineDescription')} *</span>
                  <input className="input input-bordered" value={r.description}
                    aria-label={`${t('lineDescription')} ${i + 1}`}
                    onChange={(e) => setRow(r.key, { description: e.target.value })} />
                </label>
                <label className="form-control">
                  <span className="label-text">{t('expenseDate')}</span>
                  <input type="date" className="input input-bordered" value={r.expenseDate}
                    onChange={(e) => setRow(r.key, { expenseDate: e.target.value })} />
                </label>
                <label className="form-control">
                  <span className="label-text">{t('amount')} *</span>
                  <input type="number" className="input input-bordered" value={r.amount}
                    onChange={(e) => setRow(r.key, { amount: Number(e.target.value) || 0 })} />
                </label>
                {/* F-B — a non-VAT company can never credit input VAT (no ภ.พ.30): the VAT rate
                    select and the recoverable checkbox are meaningless there, so hide both. */}
                {companyVatRegistered && (
                  <>
                    <label className="form-control">
                      <span className="label-text">VAT</span>
                      <select className="select select-bordered" value={r.vatRate}
                        onChange={(e) => setRow(r.key, { vatRate: Number(e.target.value) })}>
                        <option value={0}>0%</option>
                        <option value={0.07}>7%</option>
                      </select>
                    </label>
                    <div className="form-control md:col-span-2">
                      <label className="label cursor-pointer justify-start gap-2">
                        <input type="checkbox" className="checkbox" checked={r.isRecoverableVat}
                          onChange={(e) => setRow(r.key, { isRecoverableVat: e.target.checked })} />
                        <span className="label-text">{t('recoverableVat')}</span>
                      </label>
                      {/* CLARITY (Ham "อ่านไม่เข้าใจ") — the bare "VAT เครดิตได้" checkbox was
                          cryptic; explain what unchecking it means (wording family reused from
                          ExpenseCategorySelector.tsx's ม.82/5 warning). */}
                      {!r.isRecoverableVat && (
                        <span className="label-text-alt text-warning">{t('recoverableVatHint')}</span>
                      )}
                    </div>
                  </>
                )}
                <button type="button" className="btn btn-ghost btn-xs text-error md:col-span-4 md:ml-auto md:w-fit"
                  onClick={() => setRows((rs) => (rs.length > 1 ? rs.filter((x) => x.key !== r.key) : rs))}>
                  <Trash2 className="h-3 w-3" /> {tc('delete')}
                </button>
              </div>
            ))}
          </div>
          <button
            type="button"
            className="mt-3 flex w-full items-center justify-center gap-2 rounded-field border border-dashed border-ink-200 bg-base-100 py-3 text-sm font-medium text-peach-700 hover:border-peach-300 hover:bg-peach-50"
            onClick={() => setRows((rs) => [...rs, emptyRow(Date.now())])}>
            <Plus className="h-4 w-4" aria-hidden /> {t('addLine')}
          </button>

          <div className="mt-4 flex flex-col items-end gap-1 text-sm">
            <div>{t('subtotal')}: {subtotal.toLocaleString('th-TH', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}</div>
            {/* F-B — a non-VAT company never has a VAT line to show (always 0; hidden rather
                than shown as "0.00" so the summary reads as จำนวนเงิน only). */}
            {companyVatRegistered && (
              <div>{t('vat')}: {vat.toLocaleString('th-TH', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}</div>
            )}
            <div className="text-lg font-bold">
              {t('totalAmount')}: {total.toLocaleString('th-TH', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}
            </div>
          </div>
        </section>

        <div className="flex justify-end gap-2">
          <button type="button" className="btn btn-ghost" onClick={() => router.push('/expense-claims')} disabled={busy}>
            {tc('cancel')}
          </button>
          <button type="button" className="btn btn-primary" disabled={!canSave || busy} onClick={saveDraft}>
            {tc('save')}
          </button>
        </div>
      </div>
    </>
  );
}
