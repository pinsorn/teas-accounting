'use client';

import { useState } from 'react';
import { useRouter } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { Plus, ShieldAlert, Trash2 } from 'lucide-react';
import { PageHeader } from '@/components/ui/PageHeader';
import {
  useBusinessUnits, useCompanyBuSetting, useCreateManualJournal, useGlAccounts, useMePermissions,
} from '@/lib/queries';
import { errorToToast } from '@/lib/api/errors';
import { bangkokToday } from '@/lib/utils';

// specs/manual-jv-and-coa-management.md §B8.2 — manual JV create-and-post form. Header
// fields + repeating line grid, structure copied from ExpenseClaimForm.tsx's rows/addLine
// idiom (PageHeader + a card per section + `rows` state array keyed by a stable `key`).
//
// Deliberately NOT reusing the shared <LineItemsTable> (sales/purchase documents): it always
// keeps one undeletable blank row, which is wrong here — a JV needs a hard minimum of TWO
// lines (debit + credit), not one, and its row shape (product/qty/unit-price) doesn't match a
// JV line (GL account + a pure debit XOR credit amount). Building this small dedicated grid
// inline avoids retrofitting a component designed for a different document shape, and avoids
// repeating the "undeletable blank row made a feature unreachable" bug in a new context.
const SCOPE = 'gl.journal.post';

interface Row {
  key: number; accountId: number | null; debit: string; credit: string; description: string;
  businessUnitId: number | null;
}
const emptyRow = (k: number): Row =>
  ({ key: k, accountId: null, debit: '', credit: '', description: '', businessUnitId: null });
const MIN_LINES = 2;

function lineValid(r: Row): boolean {
  const d = Number(r.debit) || 0;
  const c = Number(r.credit) || 0;
  return r.accountId !== null && (d > 0) !== (c > 0); // pure debit XOR pure credit (INV-1)
}

export function ManualJournalForm() {
  const t = useTranslations('jv');
  const tc = useTranslations('common');
  const tBu = useTranslations('businessUnit');
  const router = useRouter();
  const create = useCreateManualJournal();
  const glAccounts = useGlAccounts(); // already filters IsActive && !IsHeader (spec §0.5, INV-5)
  const perms = useMePermissions();
  // Tier-2 F1b — Company.RequiresBusinessUnit gates a per-line BU picker. On a company that
  // does not require one the form renders exactly as it did before this fix (no picker at
  // all) — same gating hook/pattern every other document form already uses (e.g.
  // PurchaseOrderForm.tsx:91). The server (JournalService) enforces the finer rule that only
  // Revenue/Expense lines actually need one; this picker is offered on every line so the user
  // can set it wherever it applies, and a forgotten P&L line surfaces via `bu.required`.
  const buRequired = useCompanyBuSetting().data?.requiresBusinessUnit ?? false;
  const businessUnits = useBusinessUnits();

  const [docDate, setDocDate] = useState(bangkokToday());
  const [description, setDescription] = useState('');
  const [reference, setReference] = useState('');
  const [rows, setRows] = useState<Row[]>([emptyRow(1), emptyRow(2)]);
  const [busy, setBusy] = useState(false);

  function setRow(key: number, patch: Partial<Row>) {
    setRows((rs) => rs.map((r) => (r.key === key ? { ...r, ...patch } : r)));
  }

  const totalDebit = rows.reduce((s, r) => s + (Number(r.debit) || 0), 0);
  const totalCredit = rows.reduce((s, r) => s + (Number(r.credit) || 0), 0);
  const diff = Math.round((totalDebit - totalCredit) * 100) / 100;

  // INV-1 — this is UI convenience ONLY, never the guarantee: the server re-validates
  // ΣDr==ΣCr (both create-time FluentValidation and post-time MarkPosted). Disabling submit
  // here just spares the user a round-trip for the common mistake; a server rejection still
  // surfaces via errorToToast below for anything this check doesn't catch (e.g. 3-decimal
  // amounts, which the server rejects but this client check does not inspect).
  // Tier-2 F2 — compare rounded satang integers, not raw JS doubles: summing e.g. 33.33 +
  // 33.33 + 33.34 accumulates to 100.00000000000001, which `===` against 100.00 never
  // matches even though the displayed (rounded) totals agree — locking Save on an entry that
  // is visibly balanced with no way out of the form.
  const totalDebitSatang = Math.round(totalDebit * 100);
  const totalCreditSatang = Math.round(totalCredit * 100);
  const canSubmit =
    docDate !== '' && description.trim() !== '' &&
    rows.length >= MIN_LINES && rows.every(lineValid) &&
    totalDebitSatang === totalCreditSatang && totalDebitSatang > 0;

  async function submit() {
    setBusy(true);
    try {
      const res = await create.mutateAsync({
        docDate, description: description.trim(), reference: reference.trim() || null,
        lines: rows.map((r) => ({
          accountId: r.accountId!,
          debitAmount: Number(r.debit) || 0,
          creditAmount: Number(r.credit) || 0,
          description: r.description.trim() || null,
          businessUnitId: r.businessUnitId,
        })),
      });
      toast.success(tc('save'));
      // F1 (specs/fix-pnd36-payment-detection.md §3.7) — non-blocking; the post already
      // succeeded above. A warning toast alongside the success toast, never a blocking modal.
      if (res.advisoryCode === 'pnd36.ap_cleared_outside_pv') {
        toast.warning(t('pnd36AdvisoryToast'));
      }
      router.push(`/journals/${res.journalId}`);
    } catch (e) {
      // Surface the server's (Thai-resolved) rejection as-is — the guarantee lives there,
      // not in canSubmit above.
      toast.error(errorToToast(e));
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
      <PageHeader title={t('createTitle')} />
      <div className="max-w-4xl space-y-5">
        <section className="rounded-card border border-ink-100 bg-base-100 p-5 shadow-warm-sm">
          <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
            <label className="form-control">
              <span className="label-text">{t('docDate')} *</span>
              <input type="date" className="input input-bordered" value={docDate}
                max={bangkokToday()}
                onChange={(e) => setDocDate(e.target.value)} aria-label={t('docDate')} />
            </label>
            <label className="form-control">
              <span className="label-text">{t('reference')}</span>
              <input className="input input-bordered" value={reference}
                onChange={(e) => setReference(e.target.value)} aria-label={t('reference')} />
            </label>
            <label className="form-control md:col-span-2">
              <span className="label-text">{t('description')} *</span>
              <input className="input input-bordered" value={description}
                onChange={(e) => setDescription(e.target.value)} aria-label={t('description')} />
            </label>
          </div>
        </section>

        <section className="rounded-card border border-ink-100 bg-base-100 p-5 shadow-warm-sm">
          <h2 className="mb-3 font-semibold">{t('lines')}</h2>
          <div className="space-y-3">
            {rows.map((r, i) => (
              <div key={r.key} className="grid grid-cols-1 gap-3 rounded-lg border border-base-300 p-3 md:grid-cols-6">
                <div className="md:col-span-2">
                  <label className="form-control">
                    <span className="label-text">{t('account')} *</span>
                    <select className="select select-bordered" value={r.accountId ?? ''}
                      aria-label={`${t('account')} ${i + 1}`}
                      onChange={(e) => setRow(r.key, { accountId: e.target.value ? Number(e.target.value) : null })}>
                      <option value="">{t('selectAccount')}</option>
                      {(glAccounts.data ?? []).map((a) => (
                        <option key={a.accountId} value={a.accountId}>{a.accountCode} — {a.accountNameTh}</option>
                      ))}
                    </select>
                  </label>
                </div>
                <label className="form-control">
                  <span className="label-text">{t('lineDescription')}</span>
                  <input className="input input-bordered" value={r.description}
                    aria-label={`${t('lineDescription')} ${i + 1}`}
                    onChange={(e) => setRow(r.key, { description: e.target.value })} />
                </label>
                <label className="form-control">
                  <span className="label-text">{t('debit')}</span>
                  <input type="number" step="0.01" min="0" className="input input-bordered" value={r.debit}
                    aria-label={`${t('debit')} ${i + 1}`}
                    // A line is pure Dr XOR pure Cr (ck_journal_lines_amount_sign) — typing a
                    // debit clears any credit already entered on the same row, and vice versa
                    // below, so the UI can never build a both-nonzero line.
                    onChange={(e) => setRow(r.key, { debit: e.target.value, credit: e.target.value ? '' : r.credit })} />
                </label>
                <label className="form-control">
                  <span className="label-text">{t('credit')}</span>
                  <input type="number" step="0.01" min="0" className="input input-bordered" value={r.credit}
                    aria-label={`${t('credit')} ${i + 1}`}
                    onChange={(e) => setRow(r.key, { credit: e.target.value, debit: e.target.value ? '' : r.debit })} />
                </label>
                {buRequired && (
                  <label className="form-control">
                    <span className="label-text">{t('businessUnit')}</span>
                    <select className="select select-bordered" value={r.businessUnitId ?? ''}
                      aria-label={`${t('businessUnit')} ${i + 1}`}
                      onChange={(e) => setRow(r.key, { businessUnitId: e.target.value ? Number(e.target.value) : null })}>
                      <option value="">{tBu('none')}</option>
                      {(businessUnits.data ?? []).map((u) => (
                        <option key={u.businessUnitId} value={u.businessUnitId}>{u.code} — {u.nameTh}</option>
                      ))}
                    </select>
                  </label>
                )}
                <button type="button" className="btn btn-ghost btn-xs text-error md:col-span-6 md:ml-auto md:w-fit"
                  disabled={rows.length <= MIN_LINES}
                  onClick={() => setRows((rs) => (rs.length > MIN_LINES ? rs.filter((x) => x.key !== r.key) : rs))}>
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
            <div>{t('totalDebit')}: {totalDebit.toLocaleString('th-TH', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}</div>
            <div>{t('totalCredit')}: {totalCredit.toLocaleString('th-TH', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}</div>
            <div className={`text-lg font-bold ${diff === 0 ? 'text-success' : 'text-error'}`}>
              {t('difference')}: {diff.toLocaleString('th-TH', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}
              {diff === 0 && totalDebit > 0 ? ` (${t('balanced')})` : ''}
            </div>
          </div>
        </section>

        <div className="flex justify-end gap-2">
          <button type="button" className="btn btn-ghost" onClick={() => router.push('/journals')} disabled={busy}>
            {tc('cancel')}
          </button>
          <button type="button" className="btn btn-primary" disabled={!canSubmit || busy || create.isPending} onClick={submit}>
            {tc('save')}
          </button>
        </div>
        {!canSubmit && (
          <p className="text-right text-xs text-base-content/50">{t('unbalancedHint')}</p>
        )}
      </div>
    </>
  );
}
