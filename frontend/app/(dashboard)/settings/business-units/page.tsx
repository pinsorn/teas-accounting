'use client';

import { useMemo, useState } from 'react';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { Plus, Pencil } from 'lucide-react';
import type { ColumnDef } from '@tanstack/react-table';
import { PageHeader } from '@/components/ui/PageHeader';
import { DataTable } from '@/components/ui/DataTable';
import {
  useBusinessUnits, useCreateBusinessUnit, useUpdateBusinessUnit,
  useDeactivateBusinessUnit, useCompanyBuSetting, useSetCompanyBuSetting,
} from '@/lib/queries';
import type { BusinessUnitListItem } from '@/lib/types';
import { useConfirm } from '@/hooks/useConfirm';
import { PermissionGate } from '@/components/PermissionGate';
import { errorToToast } from '@/lib/api/errors';

const SCOPE = 'master.business_unit.manage';

interface Editing {
  businessUnitId: number | null;
  code: string; nameTh: string; nameEn: string; isActive: boolean;
}
const EMPTY: Editing = { businessUnitId: null, code: '', nameTh: '', nameEn: '', isActive: true };

export default function BusinessUnitsSettingsPage() {
  const t = useTranslations('businessUnit');
  const tc = useTranslations('common');
  const q = useBusinessUnits(true);
  const setting = useCompanyBuSetting();
  const setSetting = useSetCompanyBuSetting();
  const create = useCreateBusinessUnit();
  const update = useUpdateBusinessUnit();
  const deactivate = useDeactivateBusinessUnit();
  const confirm = useConfirm();
  const [edit, setEdit] = useState<Editing | null>(null);

  const rows = q.data ?? [];

  const columns = useMemo<ColumnDef<BusinessUnitListItem>[]>(() => [
    {
      accessorKey: 'code', header: t('code'),
      cell: ({ getValue }) => <span className="font-mono">{getValue<string>()}</span>,
    },
    { accessorKey: 'nameTh', header: t('nameTh') },
    {
      accessorKey: 'nameEn', header: t('nameEn'),
      cell: ({ getValue }) => <span>{getValue<string | null>() ?? '—'}</span>,
    },
    {
      accessorFn: (u) => (u.isActive ? tc('active') : tc('inactive')),
      id: 'isActive', header: t('isActive'), meta: { filter: 'select', filterLabel: tc('status') },
      cell: ({ row }) => <span>{row.original.isActive ? '✓' : '—'}</span>,
    },
    {
      id: 'actions', header: '', enableSorting: false,
      cell: ({ row }) => {
        const u = row.original;
        return (
          <span className="flex gap-1">
            <PermissionGate scope={SCOPE}>
              <button className="btn btn-ghost btn-xs"
                onClick={() => setEdit({
                  businessUnitId: u.businessUnitId, code: u.code, nameTh: u.nameTh,
                  nameEn: u.nameEn ?? '', isActive: u.isActive,
                })}>
                <Pencil className="h-3 w-3" aria-hidden />
              </button>
              {u.isActive ? (
                <button className="btn btn-ghost btn-xs text-error"
                  onClick={async () => {
                    if (!(await confirm({ description: t('deactivateConfirm'), variant: 'destructive' }))) return;
                    try { await deactivate.mutateAsync(u.businessUnitId); toast.success(t('deactivate')); }
                    catch { toast.error(tc('error')); }
                  }}>
                  {t('deactivate')}
                </button>
              ) : (
                <button className="btn btn-ghost btn-xs text-success"
                  data-testid="row-restore"
                  onClick={async () => {
                    try {
                      await update.mutateAsync({
                        id: u.businessUnitId,
                        req: { nameTh: u.nameTh, nameEn: u.nameEn ?? null,
                               defaultRevenueAccountId: null, isActive: true },
                      });
                      toast.success(tc('restore'));
                    } catch { toast.error(tc('error')); }
                  }}>
                  ↺ {tc('restore')}
                </button>
              )}
            </PermissionGate>
          </span>
        );
      },
    },
  ], [t, tc, confirm, deactivate, update]);

  async function save() {
    if (!edit) return;
    try {
      if (edit.businessUnitId === null) {
        await create.mutateAsync({
          code: edit.code, nameTh: edit.nameTh,
          nameEn: edit.nameEn || null, defaultRevenueAccountId: null,
        });
      } else {
        await update.mutateAsync({
          id: edit.businessUnitId,
          req: {
            nameTh: edit.nameTh, nameEn: edit.nameEn || null,
            defaultRevenueAccountId: null, isActive: edit.isActive,
          },
        });
      }
      toast.success(t('save'));
      setEdit(null);
    } catch (e) {
      toast.error(errorToToast(e)); // P5 — unified envelope, i18n-resolved
    }
  }

  return (
    <>
      <PageHeader
        title={t('settingsTitle')}
        actions={
          <PermissionGate scope={SCOPE}>
            <button className="btn btn-primary btn-sm gap-1" onClick={() => setEdit({ ...EMPTY })}>
              <Plus className="h-4 w-4" aria-hidden /> {t('create')}
            </button>
          </PermissionGate>
        }
      />

      <div className="card mb-4 bg-base-100 shadow-sm">
        <div className="card-body">
          <label className="label cursor-pointer justify-start gap-3">
            <input
              type="checkbox"
              className="toggle toggle-primary"
              checked={setting.data?.requiresBusinessUnit ?? false}
              disabled={setting.isLoading || setSetting.isPending}
              onChange={async (e) => {
                try { await setSetting.mutateAsync(e.target.checked); toast.success(tc('save')); }
                catch { toast.error(tc('error')); }
              }}
            />
            <span>
              <b>{t('companyToggle')}</b>
              <span className="block text-xs text-base-content/60">{t('companyToggleHint')}</span>
            </span>
          </label>
        </div>
      </div>

      <DataTable
        data={rows}
        columns={columns}
        isLoading={q.isLoading}
        getRowId={(r) => String(r.businessUnitId)}
        searchPlaceholder={t('nameTh')}
      />

      {edit && (
        <div className="modal modal-open" role="dialog" aria-modal="true">
          <div className="modal-box">
            <h3 className="text-lg font-bold">
              {edit.businessUnitId === null ? t('create') : t('edit')}
            </h3>
            <div className="mt-4 space-y-3">
              <label className="form-control">
                <span className="label-text">{t('code')} *</span>
                <input className="input input-bordered" value={edit.code}
                  disabled={edit.businessUnitId !== null}
                  onChange={(e) => setEdit({ ...edit, code: e.target.value.toUpperCase() })} />
              </label>
              <label className="form-control">
                <span className="label-text">{t('nameTh')} *</span>
                <input className="input input-bordered" value={edit.nameTh}
                  onChange={(e) => setEdit({ ...edit, nameTh: e.target.value })} />
              </label>
              <label className="form-control">
                <span className="label-text">{t('nameEn')}</span>
                <input className="input input-bordered" value={edit.nameEn}
                  onChange={(e) => setEdit({ ...edit, nameEn: e.target.value })} />
              </label>
              {edit.businessUnitId !== null && (
                <label className="label cursor-pointer justify-start gap-3">
                  <input type="checkbox" className="checkbox checkbox-sm" checked={edit.isActive}
                    onChange={(e) => setEdit({ ...edit, isActive: e.target.checked })} />
                  <span className="label-text">{t('isActive')}</span>
                </label>
              )}
            </div>
            <div className="modal-action">
              <button className="btn btn-ghost" onClick={() => setEdit(null)}>{t('cancel')}</button>
              <button className="btn btn-primary"
                disabled={edit.code.trim() === '' || edit.nameTh.trim() === '' ||
                          create.isPending || update.isPending}
                onClick={save}>
                {t('save')}
              </button>
            </div>
          </div>
          <button className="modal-backdrop" aria-label="close" onClick={() => setEdit(null)} />
        </div>
      )}
    </>
  );
}
