'use client';

import { useMemo, useState } from 'react';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { Plus, Pencil } from 'lucide-react';
import type { ColumnDef } from '@tanstack/react-table';
import { PageHeader } from '@/components/ui/PageHeader';
import { DataTable } from '@/components/ui/DataTable';
import {
  useEmployees, useEmployee, useCreateEmployee, useUpdateEmployee, useDeactivateEmployee,
} from '@/lib/queries';
import type { EmployeeListItem, CreateEmployeeRequest } from '@/lib/types';
import { useConfirm } from '@/hooks/useConfirm';
import { PermissionGate } from '@/components/PermissionGate';
import { errorToToast } from '@/lib/api/errors';
import { formatTHB } from '@/lib/utils';

const SCOPE = 'master.employee.manage';

interface Editing extends CreateEmployeeRequest {
  employeeId: number | null;
  isActive: boolean;
}

function todayIso() { return new Date().toISOString().slice(0, 10); }

const EMPTY: Editing = {
  employeeId: null, isActive: true,
  employeeCode: '', titleTh: '', firstNameTh: '', lastNameTh: '',
  titleEn: '', firstNameEn: '', lastNameEn: '',
  nationalId: '', taxId: '',
  address: { addressNo: '', moo: '', soi: '', street: '', subDistrict: '', district: '', province: '', postalCode: '' },
  hireDate: todayIso(), terminationDate: null,
  baseSalary: 0,
  bankName: '', bankAccountNo: '', bankAccountName: '',
  ssoApplicable: true, ssoNumber: '',
  maritalStatus: 'SINGLE', spouseHasIncome: false, childrenCount: 0,
};

export default function EmployeesSettingsPage() {
  const t = useTranslations('employee');
  const tc = useTranslations('common');
  const q = useEmployees(true);
  const create = useCreateEmployee();
  const update = useUpdateEmployee();
  const deactivate = useDeactivateEmployee();
  const confirm = useConfirm();
  const [edit, setEdit] = useState<Editing | null>(null);
  const [editId, setEditId] = useState<number>(0);
  const detail = useEmployee(editId);

  const rows = q.data ?? [];

  // Open the edit modal: seed from the loaded detail (hydrated by the effect below).
  function openEdit(row: EmployeeListItem) { setEditId(row.employeeId); }
  // When detail arrives for the row being edited, populate the form once.
  if (detail.data && edit === null && editId === detail.data.employeeId) {
    const d = detail.data;
    setEdit({
      employeeId: d.employeeId, isActive: d.isActive,
      employeeCode: d.employeeCode,
      titleTh: d.titleTh ?? '', firstNameTh: d.firstNameTh, lastNameTh: d.lastNameTh,
      titleEn: d.titleEn ?? '', firstNameEn: d.firstNameEn ?? '', lastNameEn: d.lastNameEn ?? '',
      nationalId: d.nationalId, taxId: d.taxId ?? '',
      address: d.address,
      hireDate: d.hireDate.slice(0, 10),
      terminationDate: d.terminationDate ? d.terminationDate.slice(0, 10) : null,
      baseSalary: d.baseSalary,
      bankName: d.bankName ?? '', bankAccountNo: d.bankAccountNo ?? '', bankAccountName: d.bankAccountName ?? '',
      ssoApplicable: d.ssoApplicable, ssoNumber: d.ssoNumber ?? '',
      maritalStatus: d.maritalStatus, spouseHasIncome: d.spouseHasIncome, childrenCount: d.childrenCount,
    });
  }

  function close() { setEdit(null); setEditId(0); }

  const columns = useMemo<ColumnDef<EmployeeListItem>[]>(() => [
    {
      accessorKey: 'employeeCode', header: t('code'),
      cell: ({ getValue }) => <span className="font-mono">{getValue<string>()}</span>,
    },
    { accessorKey: 'fullNameTh', header: t('name') },
    {
      accessorKey: 'nationalId', header: t('nationalId'),
      cell: ({ getValue }) => <span className="font-mono">{getValue<string>()}</span>,
    },
    {
      accessorKey: 'baseSalary', header: t('baseSalary'), meta: { align: 'right' },
      cell: ({ getValue }) => <span className="tabular-nums">{formatTHB(getValue<number>())}</span>,
    },
    {
      accessorFn: (e) => (e.ssoApplicable ? tc('yes') : tc('no')),
      id: 'sso', header: t('sso'), meta: { filter: 'select' },
    },
    {
      accessorFn: (e) => (e.isActive ? tc('active') : tc('inactive')),
      id: 'isActive', header: tc('status'), meta: { filter: 'select', filterLabel: tc('status') },
      cell: ({ row }) => <span>{row.original.isActive ? '✓' : '—'}</span>,
    },
    {
      id: 'actions', header: '', enableSorting: false,
      cell: ({ row }) => (
        <PermissionGate scope={SCOPE}>
          <span className="flex gap-1">
            <button className="btn btn-ghost btn-xs" onClick={() => openEdit(row.original)}>
              <Pencil className="h-3 w-3" aria-hidden />
            </button>
            {row.original.isActive && (
              <button className="btn btn-ghost btn-xs text-error"
                onClick={async () => {
                  if (!(await confirm({ description: t('deactivateConfirm'), variant: 'destructive' }))) return;
                  try { await deactivate.mutateAsync(row.original.employeeId); toast.success(t('deactivated')); }
                  catch (e) { toast.error(errorToToast(e)); }
                }}>
                {t('deactivate')}
              </button>
            )}
          </span>
        </PermissionGate>
      ),
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
  ], [t, tc, confirm, deactivate]);

  async function save() {
    if (!edit) return;
    const req: CreateEmployeeRequest = {
      employeeCode: edit.employeeCode,
      titleTh: edit.titleTh || null, firstNameTh: edit.firstNameTh, lastNameTh: edit.lastNameTh,
      titleEn: edit.titleEn || null, firstNameEn: edit.firstNameEn || null, lastNameEn: edit.lastNameEn || null,
      nationalId: edit.nationalId, taxId: edit.taxId || null,
      address: edit.address,
      hireDate: edit.hireDate, terminationDate: edit.terminationDate || null,
      baseSalary: edit.baseSalary,
      bankName: edit.bankName || null, bankAccountNo: edit.bankAccountNo || null, bankAccountName: edit.bankAccountName || null,
      ssoApplicable: edit.ssoApplicable, ssoNumber: edit.ssoNumber || null,
      maritalStatus: edit.maritalStatus, spouseHasIncome: edit.spouseHasIncome, childrenCount: edit.childrenCount,
    };
    try {
      if (edit.employeeId === null) await create.mutateAsync(req);
      else await update.mutateAsync({ id: edit.employeeId, req: { ...req, isActive: edit.isActive } });
      toast.success(tc('save'));
      close();
    } catch (e) { toast.error(errorToToast(e)); }
  }

  const set = (patch: Partial<Editing>) => setEdit((p) => (p ? { ...p, ...patch } : p));
  const setAddr = (patch: Partial<NonNullable<Editing['address']>>) =>
    setEdit((p) => (p ? { ...p, address: { ...(p.address ?? EMPTY.address)!, ...patch } } : p));

  const nidOk = edit ? edit.nationalId.replace(/\D/g, '').length === 13 : false;
  const canSave = !!edit && edit.employeeCode.trim() !== '' && edit.firstNameTh.trim() !== ''
    && edit.lastNameTh.trim() !== '' && nidOk && !create.isPending && !update.isPending;

  return (
    <>
      <PageHeader
        title={t('settingsTitle')} subtitle={t('settingsSubtitle')}
        actions={
          <PermissionGate scope={SCOPE}>
            <button className="btn btn-primary btn-sm gap-1" onClick={() => { setEditId(0); setEdit({ ...EMPTY }); }}>
              <Plus className="h-4 w-4" aria-hidden /> {t('create')}
            </button>
          </PermissionGate>
        }
      />

      <DataTable
        data={rows}
        columns={columns}
        isLoading={q.isLoading}
        getRowId={(r) => String(r.employeeId)}
        searchPlaceholder={t('name')}
      />

      {edit && (
        <div className="modal modal-open" role="dialog" aria-modal="true">
          <div className="modal-box max-w-2xl">
            <h3 className="text-lg font-bold">{edit.employeeId === null ? t('create') : t('edit')}</h3>

            <div className="mt-4 grid grid-cols-1 gap-3 sm:grid-cols-2">
              <label className="form-control">
                <span className="label-text">{t('code')} *</span>
                <input className="input input-bordered" value={edit.employeeCode}
                  disabled={edit.employeeId !== null}
                  onChange={(e) => set({ employeeCode: e.target.value.toUpperCase() })} />
              </label>
              <label className="form-control">
                <span className="label-text">{t('nationalId')} *</span>
                <input className="input input-bordered font-mono" value={edit.nationalId} maxLength={13}
                  onChange={(e) => set({ nationalId: e.target.value.replace(/\D/g, '') })} />
                {!nidOk && edit.nationalId !== '' && (
                  <span className="label-text-alt text-error">{t('nationalIdInvalid')}</span>
                )}
              </label>

              <label className="form-control">
                <span className="label-text">{t('titleTh')}</span>
                <input className="input input-bordered" value={edit.titleTh ?? ''}
                  onChange={(e) => set({ titleTh: e.target.value })} />
              </label>
              <div className="grid grid-cols-2 gap-2">
                <label className="form-control">
                  <span className="label-text">{t('firstNameTh')} *</span>
                  <input className="input input-bordered" value={edit.firstNameTh}
                    onChange={(e) => set({ firstNameTh: e.target.value })} />
                </label>
                <label className="form-control">
                  <span className="label-text">{t('lastNameTh')} *</span>
                  <input className="input input-bordered" value={edit.lastNameTh}
                    onChange={(e) => set({ lastNameTh: e.target.value })} />
                </label>
              </div>

              <label className="form-control">
                <span className="label-text">{t('baseSalary')} *</span>
                <input type="number" min={0} step="0.01" className="input input-bordered tabular-nums"
                  value={edit.baseSalary}
                  onChange={(e) => set({ baseSalary: Number(e.target.value) || 0 })} />
              </label>
              <label className="form-control">
                <span className="label-text">{t('hireDate')} *</span>
                <input type="date" className="input input-bordered" value={edit.hireDate}
                  onChange={(e) => set({ hireDate: e.target.value })} />
              </label>

              {/* Allowance inputs (minimal v1) */}
              <label className="form-control">
                <span className="label-text">{t('maritalStatus')}</span>
                <select className="select select-bordered" value={edit.maritalStatus}
                  onChange={(e) => set({ maritalStatus: e.target.value })}>
                  <option value="SINGLE">{t('single')}</option>
                  <option value="MARRIED">{t('married')}</option>
                </select>
              </label>
              <label className="form-control">
                <span className="label-text">{t('childrenCount')}</span>
                <input type="number" min={0} className="input input-bordered" value={edit.childrenCount}
                  onChange={(e) => set({ childrenCount: Math.max(0, Number(e.target.value) || 0) })} />
              </label>

              <label className="label cursor-pointer justify-start gap-3">
                <input type="checkbox" className="checkbox checkbox-sm" checked={edit.spouseHasIncome}
                  disabled={edit.maritalStatus !== 'MARRIED'}
                  onChange={(e) => set({ spouseHasIncome: e.target.checked })} />
                <span className="label-text">{t('spouseHasIncome')}</span>
              </label>
              <label className="label cursor-pointer justify-start gap-3">
                <input type="checkbox" className="checkbox checkbox-sm" checked={edit.ssoApplicable}
                  onChange={(e) => set({ ssoApplicable: e.target.checked })} />
                <span className="label-text">{t('ssoApplicable')}</span>
              </label>

              {/* Bank (payment evidence) */}
              <label className="form-control">
                <span className="label-text">{t('bankName')}</span>
                <input className="input input-bordered" value={edit.bankName ?? ''}
                  onChange={(e) => set({ bankName: e.target.value })} />
              </label>
              <label className="form-control">
                <span className="label-text">{t('bankAccountNo')}</span>
                <input className="input input-bordered font-mono" value={edit.bankAccountNo ?? ''}
                  onChange={(e) => set({ bankAccountNo: e.target.value })} />
              </label>

              {/* Address (ภ.ง.ด.1ก) */}
              <label className="form-control sm:col-span-2">
                <span className="label-text">{t('addressNo')} / {t('street')}</span>
                <div className="grid grid-cols-2 gap-2">
                  <input className="input input-bordered" placeholder={t('addressNo')} value={edit.address?.addressNo ?? ''}
                    onChange={(e) => setAddr({ addressNo: e.target.value })} />
                  <input className="input input-bordered" placeholder={t('street')} value={edit.address?.street ?? ''}
                    onChange={(e) => setAddr({ street: e.target.value })} />
                </div>
              </label>
              <label className="form-control">
                <span className="label-text">{t('subDistrict')}</span>
                <input className="input input-bordered" value={edit.address?.subDistrict ?? ''}
                  onChange={(e) => setAddr({ subDistrict: e.target.value })} />
              </label>
              <label className="form-control">
                <span className="label-text">{t('district')}</span>
                <input className="input input-bordered" value={edit.address?.district ?? ''}
                  onChange={(e) => setAddr({ district: e.target.value })} />
              </label>
              <label className="form-control">
                <span className="label-text">{t('province')}</span>
                <input className="input input-bordered" value={edit.address?.province ?? ''}
                  onChange={(e) => setAddr({ province: e.target.value })} />
              </label>
              <label className="form-control">
                <span className="label-text">{t('postalCode')}</span>
                <input className="input input-bordered font-mono" maxLength={5} value={edit.address?.postalCode ?? ''}
                  onChange={(e) => setAddr({ postalCode: e.target.value.replace(/\D/g, '') })} />
              </label>

              {edit.employeeId !== null && (
                <label className="label cursor-pointer justify-start gap-3 sm:col-span-2">
                  <input type="checkbox" className="checkbox checkbox-sm" checked={edit.isActive}
                    onChange={(e) => set({ isActive: e.target.checked })} />
                  <span className="label-text">{tc('active')}</span>
                </label>
              )}
            </div>

            <div className="modal-action">
              <button className="btn btn-ghost" onClick={close}>{tc('cancel')}</button>
              <button className="btn btn-primary" disabled={!canSave} onClick={save}>{tc('save')}</button>
            </div>
          </div>
          <button className="modal-backdrop" aria-label="close" onClick={close} />
        </div>
      )}
    </>
  );
}
