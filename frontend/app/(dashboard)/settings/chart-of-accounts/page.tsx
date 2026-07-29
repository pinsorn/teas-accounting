'use client';

import { useMemo, useState } from 'react';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { Plus, Pencil } from 'lucide-react';
import type { ColumnDef } from '@tanstack/react-table';
import { PageHeader } from '@/components/ui/PageHeader';
import { DataTable } from '@/components/ui/DataTable';
import { useAccounts, useCreateAccount, useUpdateAccount } from '@/lib/queries';
import type { AccountListItem, AccountTypeStr, NormalBalanceStr } from '@/lib/types';
import { PermissionGate } from '@/components/PermissionGate';
import { errorToToast } from '@/lib/api/errors';

// specs/manual-jv-and-coa-management.md §A3 — chart-of-accounts management. Pattern copied
// from settings/business-units/page.tsx (PageHeader + DataTable + edit-modal state +
// PermissionGate + errorToToast). No delete anywhere (INV-6) — deactivate (isActive toggle
// in the edit modal) is the only retire operation; accountCode/accountType/normalBalance are
// immutable after create (UpdateAccountRequest doesn't carry them) so they render read-only
// in edit. `isHeader` is never shown/edited in the UI — carried through unchanged on save so
// editing a header account's name never silently un-headers it (the API still accepts the
// field; A1c/A1d guard the header-flip cases server-side).
const SCOPE = 'master.coa.manage';

const TYPES: AccountTypeStr[] = ['Asset', 'Liability', 'Equity', 'Revenue', 'Expense'];
// ASSET/EXPENSE are normally Dr; LIABILITY/EQUITY/REVENUE are normally Cr — user-overridable
// (1690/4100-style contra accounts are legitimate).
const DEFAULT_NORMAL: Record<AccountTypeStr, NormalBalanceStr> = {
  Asset: 'Debit', Expense: 'Debit', Liability: 'Credit', Equity: 'Credit', Revenue: 'Credit',
};

interface Editing {
  accountId: number | null;
  accountCode: string; accountNameTh: string; accountNameEn: string;
  accountType: AccountTypeStr; normalBalance: NormalBalanceStr;
  isHeader: boolean; isActive: boolean;
}
const EMPTY: Editing = {
  accountId: null, accountCode: '', accountNameTh: '', accountNameEn: '',
  accountType: 'Asset', normalBalance: 'Debit', isHeader: false, isActive: true,
};

export default function ChartOfAccountsSettingsPage() {
  const t = useTranslations('coa');
  const tc = useTranslations('common');
  const q = useAccounts(false); // always both active + inactive (activeOnly is required on the BE)
  const create = useCreateAccount();
  const update = useUpdateAccount();
  const [edit, setEdit] = useState<Editing | null>(null);

  const rows = q.data ?? [];

  const columns = useMemo<ColumnDef<AccountListItem>[]>(() => [
    {
      accessorKey: 'accountCode', header: t('code'),
      cell: ({ getValue }) => <span className="font-mono">{getValue<string>()}</span>,
    },
    { accessorKey: 'accountNameTh', header: t('nameTh') },
    {
      accessorKey: 'accountNameEn', header: t('nameEn'),
      cell: ({ getValue }) => <span>{getValue<string | null>() ?? '—'}</span>,
    },
    {
      // No column filter here (unlike isActive below): the raw enum value ("Asset") would
      // show untranslated in the filter dropdown — DataTable's ColumnFilter only Thai-translates
      // facet values for the 'status'/'paymentStatus' column ids.
      accessorKey: 'accountType', header: t('type'),
      cell: ({ getValue }) => t(`typeLabel.${getValue<AccountTypeStr>()}`),
    },
    {
      accessorKey: 'normalBalance', header: t('normalBalance'),
      cell: ({ getValue }) => (
        <span className="badge badge-ghost">{getValue<NormalBalanceStr>() === 'Debit' ? 'DR' : 'CR'}</span>
      ),
    },
    {
      accessorKey: 'isHeader', header: t('isHeader'),
      cell: ({ getValue }) => <span>{getValue<boolean>() ? '✓' : '—'}</span>,
    },
    {
      accessorFn: (a) => (a.isActive ? tc('active') : tc('inactive')),
      id: 'isActive', header: tc('status'), meta: { filter: 'select', filterLabel: tc('status') },
      cell: ({ row }) => <span>{row.original.isActive ? '✓' : '—'}</span>,
    },
    {
      id: 'actions', header: '', enableSorting: false,
      cell: ({ row }) => {
        const a = row.original;
        return (
          <PermissionGate scope={SCOPE}>
            <button className="btn btn-ghost btn-xs"
              onClick={() => setEdit({
                accountId: a.accountId, accountCode: a.accountCode, accountNameTh: a.accountNameTh,
                accountNameEn: a.accountNameEn ?? '', accountType: a.accountType,
                normalBalance: a.normalBalance, isHeader: a.isHeader, isActive: a.isActive,
              })}>
              <Pencil className="h-3 w-3" aria-hidden />
            </button>
          </PermissionGate>
        );
      },
    },
  ], [t, tc]);

  async function save() {
    if (!edit) return;
    try {
      if (edit.accountId === null) {
        await create.mutateAsync({
          accountCode: edit.accountCode, accountNameTh: edit.accountNameTh,
          accountNameEn: edit.accountNameEn || null, accountType: edit.accountType,
          parentId: null, isHeader: false, normalBalance: edit.normalBalance,
        });
      } else {
        await update.mutateAsync({
          id: edit.accountId,
          req: {
            accountNameTh: edit.accountNameTh, accountNameEn: edit.accountNameEn || null,
            // isHeader is not user-editable — pass the account's OWN current value through
            // unchanged (never hardcode false: this account may legitimately be a header).
            isHeader: edit.isHeader, isActive: edit.isActive,
          },
        });
      }
      toast.success(tc('save'));
      setEdit(null);
    } catch (e) {
      toast.error(errorToToast(e));
    }
  }

  return (
    <>
      <PageHeader
        title={t('settingsTitle')}
        actions={
          <PermissionGate scope={SCOPE}>
            <button className="btn btn-primary btn-sm gap-1"
              onClick={() => setEdit({ ...EMPTY })}>
              <Plus className="h-4 w-4" aria-hidden /> {t('create')}
            </button>
          </PermissionGate>
        }
      />

      <DataTable
        data={rows}
        columns={columns}
        isLoading={q.isLoading}
        getRowId={(r) => String(r.accountId)}
        searchPlaceholder={t('nameTh')}
      />

      {edit && (
        <div className="modal modal-open" role="dialog" aria-modal="true">
          <div className="modal-box">
            <h3 className="text-lg font-bold">
              {edit.accountId === null ? t('create') : t('edit')}
            </h3>
            <div className="mt-4 space-y-3">
              <label className="form-control">
                <span className="label-text">{t('code')} *</span>
                {edit.accountId === null ? (
                  <>
                    <input className="input input-bordered" value={edit.accountCode}
                      pattern="\d{2,10}"
                      onChange={(e) => setEdit({ ...edit, accountCode: e.target.value })} />
                    <span className="label-text-alt mt-1 text-base-content/50">{t('codeHint')}</span>
                  </>
                ) : (
                  <>
                    <input className="input input-bordered" value={edit.accountCode} disabled readOnly />
                    <span className="label-text-alt mt-1 text-base-content/50">{t('codeImmutableHint')}</span>
                  </>
                )}
              </label>
              <label className="form-control">
                <span className="label-text">{t('nameTh')} *</span>
                <input className="input input-bordered" value={edit.accountNameTh}
                  onChange={(e) => setEdit({ ...edit, accountNameTh: e.target.value })} />
              </label>
              <label className="form-control">
                <span className="label-text">{t('nameEn')}</span>
                <input className="input input-bordered" value={edit.accountNameEn}
                  onChange={(e) => setEdit({ ...edit, accountNameEn: e.target.value })} />
              </label>
              <label className="form-control">
                <span className="label-text">{t('type')}</span>
                {edit.accountId === null ? (
                  <select className="select select-bordered" value={edit.accountType}
                    onChange={(e) => {
                      const next = e.target.value as AccountTypeStr;
                      setEdit({ ...edit, accountType: next, normalBalance: DEFAULT_NORMAL[next] });
                    }}>
                    {TYPES.map((ty) => <option key={ty} value={ty}>{t(`typeLabel.${ty}`)}</option>)}
                  </select>
                ) : (
                  <>
                    <input className="input input-bordered" value={t(`typeLabel.${edit.accountType}`)} disabled readOnly />
                    <span className="label-text-alt mt-1 text-base-content/50">{t('typeImmutableHint')}</span>
                  </>
                )}
              </label>
              <label className="form-control">
                <span className="label-text">{t('normalBalance')}</span>
                {edit.accountId === null ? (
                  <select className="select select-bordered" value={edit.normalBalance}
                    onChange={(e) => setEdit({ ...edit, normalBalance: e.target.value as NormalBalanceStr })}>
                    <option value="Debit">{t('debit')}</option>
                    <option value="Credit">{t('credit')}</option>
                  </select>
                ) : (
                  <>
                    <input className="input input-bordered"
                      value={edit.normalBalance === 'Debit' ? t('debit') : t('credit')} disabled readOnly />
                    <span className="label-text-alt mt-1 text-base-content/50">{t('typeImmutableHint')}</span>
                  </>
                )}
              </label>
              {edit.accountId !== null && (
                <label className="label cursor-pointer justify-start gap-3">
                  <input type="checkbox" className="checkbox checkbox-sm" checked={edit.isActive}
                    onChange={(e) => setEdit({ ...edit, isActive: e.target.checked })} />
                  <span className="label-text">{t('isActive')}</span>
                </label>
              )}
              {edit.accountId !== null && (
                <span className="block text-xs text-base-content/50">{t('deactivateHint')}</span>
              )}
            </div>
            <div className="modal-action">
              <button className="btn btn-ghost" onClick={() => setEdit(null)}>{tc('cancel')}</button>
              <button className="btn btn-primary"
                disabled={edit.accountCode.trim() === '' || edit.accountNameTh.trim() === '' ||
                          create.isPending || update.isPending}
                onClick={save}>
                {tc('save')}
              </button>
            </div>
          </div>
          <button className="modal-backdrop" aria-label={tc('close')} onClick={() => setEdit(null)} />
        </div>
      )}
    </>
  );
}
