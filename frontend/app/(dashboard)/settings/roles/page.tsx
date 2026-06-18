'use client';

import { useEffect, useMemo, useState } from 'react';
import { useTranslations, useLocale } from 'next-intl';
import { toast } from 'sonner';
import { Plus, Pencil, Trash2, ShieldCheck, ShieldAlert, Info } from 'lucide-react';
import { PageHeader } from '@/components/ui/PageHeader';
import { QueryState } from '@/components/states/QueryState';
import { problemToast } from '@/lib/api';
import {
  useCompanies, useMePermissions,
  useRbacRoles, useRbacRole, usePermissionCatalog,
  useCreateRbacRole, useUpdateRbacRole, useDeleteRbacRole, useSetRolePermissions,
} from '@/lib/queries';
import type { RoleListItem, PermissionCatalogItem } from '@/lib/types';

// Module display order for the permission grid (CLAUDE.md grouping).
const MODULE_ORDER = ['master', 'sys', 'gl', 'sales', 'purchase', 'tax', 'payroll', 'report'];

export default function RolesSettingsPage() {
  const t = useTranslations('roles');
  const perms = useMePermissions();
  const isSuperAdmin = perms.data?.isSuperAdmin ?? false;
  const canManage = isSuperAdmin || (perms.data?.permissions.includes('sys.role.manage') ?? false);

  // Super-admin scopes by a company selector; company-admins omit companyId entirely.
  const [companyId, setCompanyId] = useState<number | undefined>(undefined);

  // Page gate — same hidden-not-disabled rule as the companies page.
  if (perms.data && !canManage) {
    return (
      <>
        <PageHeader title={t('title')} />
        <div className="flex flex-col items-center gap-2 py-12 text-center" data-testid="state-no-access">
          <ShieldAlert className="h-10 w-10 text-warning" aria-hidden />
          <div className="font-semibold">{t('noAccessTitle')}</div>
          <div className="max-w-md text-sm text-base-content/60">{t('noAccessBody')}</div>
        </div>
      </>
    );
  }

  return (
    <>
      <PageHeader title={t('title')} subtitle={t('subtitle')} />
      {isSuperAdmin && <CompanySelector value={companyId} onChange={setCompanyId} />}
      <RolesBody isSuperAdmin={isSuperAdmin} companyId={companyId} />
    </>
  );
}

// Mounts useCompanies() ONLY for super-admins (the /companies endpoint enforces
// master.company.manage → would 403 for company-admins). Defaults to the first company.
function CompanySelector({
  value, onChange,
}: { value: number | undefined; onChange: (id: number | undefined) => void }) {
  const t = useTranslations('roles');
  const companies = useCompanies();
  const rows = companies.data ?? [];

  useEffect(() => {
    const first = rows[0];
    if (value == null && first) onChange(first.companyId);
  }, [rows, value, onChange]);

  return (
    <div className="mb-4 flex items-center gap-2">
      <span className="text-sm text-base-content/60">{t('company')}</span>
      <select
        className="select select-bordered select-sm min-w-64"
        value={value ?? ''}
        onChange={(e) => onChange(e.target.value ? Number(e.target.value) : undefined)}
        data-testid="rbac-company-select"
      >
        {rows.map((c) => (
          <option key={c.companyId} value={c.companyId}>{c.nameTh}</option>
        ))}
      </select>
    </div>
  );
}

function RolesBody({
  isSuperAdmin, companyId,
}: { isSuperAdmin: boolean; companyId: number | undefined }) {
  const t = useTranslations('roles');
  const tc = useTranslations('common');
  // Super-admin must have picked a company before we fire the list (else a
  // companyId-less call would briefly show their own company's roles).
  const enabled = !isSuperAdmin || companyId != null;
  const q = useRbacRoles(companyId, enabled);
  const rows = enabled ? (q.data ?? []) : [];

  const del = useDeleteRbacRole();
  const [creating, setCreating] = useState(false);
  const [editingPermsId, setEditingPermsId] = useState<number | null>(null);
  const [renamingId, setRenamingId] = useState<number | null>(null);

  async function remove(r: RoleListItem) {
    if (!confirm(t('deleteConfirm', { name: r.nameTh }))) return;
    try {
      await del.mutateAsync(r.roleId);
      toast.success(t('deleted'));
    } catch (e) {
      problemToast(e, tc('error'));
    }
  }

  if (!enabled) {
    return <div className="py-12 text-center text-base-content/50">{tc('loading')}</div>;
  }

  return (
    <>
      <div className="mb-3 flex justify-end">
        <button className="btn btn-primary btn-sm gap-1" onClick={() => setCreating(true)}
          data-testid="role-create-btn">
          <Plus className="h-4 w-4" aria-hidden /> {t('create')}
        </button>
      </div>

      <QueryState query={q} isEmpty={!q.isLoading && rows.length === 0}>
        <div className="overflow-x-auto rounded-lg border border-base-300">
          <table className="table table-zebra">
            <thead>
              <tr>
                <th>{t('roleCode')}</th>
                <th>{t('nameTh')}</th>
                <th>{t('type')}</th>
                <th className="text-right">{t('userCount')}</th>
                <th className="text-right">{t('permissionCount')}</th>
                <th className="w-40" />
              </tr>
            </thead>
            <tbody>
              {rows.map((r: RoleListItem) => (
                <tr key={r.roleId} data-testid={`role-row-${r.roleId}`}>
                  <td className="font-mono text-sm">{r.roleCode}</td>
                  <td>{r.nameTh}</td>
                  <td>
                    {r.isSystem
                      ? <span className="badge badge-neutral badge-sm">{t('system')}</span>
                      : <span className="badge badge-ghost badge-sm">{t('custom')}</span>}
                  </td>
                  <td className="text-right tabular-nums">{r.userCount}</td>
                  <td className="text-right tabular-nums">{r.permissionCount}</td>
                  <td>
                    <div className="flex justify-end gap-1">
                      <button className="btn btn-ghost btn-xs gap-1"
                        onClick={() => setEditingPermsId(r.roleId)}
                        data-testid={`role-perms-${r.roleId}`}>
                        <ShieldCheck className="h-3 w-3" aria-hidden /> {t('editPermissions')}
                      </button>
                      <button className="btn btn-ghost btn-xs gap-1"
                        onClick={() => setRenamingId(r.roleId)}
                        data-testid={`role-edit-${r.roleId}`}>
                        <Pencil className="h-3 w-3" aria-hidden /> {tc('edit')}
                      </button>
                      <button className="btn btn-ghost btn-xs gap-1 text-error"
                        disabled={r.isSystem || r.userCount > 0 || del.isPending}
                        onClick={() => remove(r)}
                        data-testid={`role-delete-${r.roleId}`}>
                        <Trash2 className="h-3 w-3" aria-hidden /> {tc('delete')}
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </QueryState>

      {creating && (
        <CreateRoleDialog companyId={companyId} onClose={() => setCreating(false)} />
      )}
      {editingPermsId != null && (
        <EditPermissionsDialog roleId={editingPermsId} onClose={() => setEditingPermsId(null)} />
      )}
      {renamingId != null && (
        <RenameRoleDialog roleId={renamingId} onClose={() => setRenamingId(null)} />
      )}
    </>
  );
}

// ───────────────────────────── Create ─────────────────────────────

function CreateRoleDialog({
  companyId, onClose,
}: { companyId: number | undefined; onClose: () => void }) {
  const t = useTranslations('roles');
  const tc = useTranslations('common');
  const create = useCreateRbacRole();
  const [roleCode, setRoleCode] = useState('');
  const [nameTh, setNameTh] = useState('');
  const [description, setDescription] = useState('');

  const canSave = roleCode.trim() !== '' && nameTh.trim() !== '';

  async function submit() {
    try {
      await create.mutateAsync({
        roleCode: roleCode.trim(),
        nameTh: nameTh.trim(),
        description: description.trim() ? description.trim() : null,
        companyId, // undefined for company-admins → BE uses own company
      });
      toast.success(t('created'));
      onClose();
    } catch (e) {
      problemToast(e, tc('error'));
    }
  }

  return (
    <div className="modal modal-open" role="dialog" aria-modal="true">
      <div className="modal-box">
        <h3 className="text-lg font-bold">{t('createTitle')}</h3>
        <div className="mt-4 flex flex-col gap-3">
          <label className="form-control">
            <span className="label-text">{t('roleCode')} <span className="text-error">*</span></span>
            <input className="input input-bordered font-mono" value={roleCode}
              onChange={(e) => setRoleCode(e.target.value)} data-testid="role-new-code" />
          </label>
          <label className="form-control">
            <span className="label-text">{t('nameTh')} <span className="text-error">*</span></span>
            <input className="input input-bordered" value={nameTh}
              onChange={(e) => setNameTh(e.target.value)} data-testid="role-new-nameth" />
          </label>
          <label className="form-control">
            <span className="label-text">{t('description')}</span>
            <input className="input input-bordered" value={description}
              onChange={(e) => setDescription(e.target.value)} data-testid="role-new-desc" />
          </label>
        </div>
        <div className="modal-action">
          <button className="btn btn-ghost" onClick={onClose}>{tc('cancel')}</button>
          <button className="btn btn-primary" disabled={!canSave || create.isPending}
            onClick={submit} data-testid="role-new-save">
            {create.isPending && <span className="loading loading-spinner loading-sm" />}
            {tc('save')}
          </button>
        </div>
      </div>
      <button className="modal-backdrop" aria-label={tc('close')} onClick={onClose} />
    </div>
  );
}

// ───────────────────────────── Rename ─────────────────────────────

function RenameRoleDialog({ roleId, onClose }: { roleId: number; onClose: () => void }) {
  const t = useTranslations('roles');
  const tc = useTranslations('common');
  const q = useRbacRole(roleId);
  const save = useUpdateRbacRole();
  const [nameTh, setNameTh] = useState('');
  const [description, setDescription] = useState('');

  const d = q.data;
  useEffect(() => {
    if (!d) return;
    setNameTh(d.nameTh);
    setDescription(d.description ?? '');
  }, [d]);

  const canSave = !!d && nameTh.trim() !== '';

  async function submit() {
    try {
      await save.mutateAsync({
        id: roleId,
        req: { nameTh: nameTh.trim(), description: description.trim() ? description.trim() : null },
      });
      toast.success(t('saved'));
      onClose();
    } catch (e) {
      problemToast(e, tc('error'));
    }
  }

  return (
    <div className="modal modal-open" role="dialog" aria-modal="true">
      <div className="modal-box">
        <h3 className="text-lg font-bold">
          {t('editTitle')}{d ? ` — ${d.roleCode}` : ''}
        </h3>
        {(q.isLoading || !d) && !q.isError && (
          <div className="py-8 text-center text-base-content/50">{tc('loading')}</div>
        )}
        {q.isError && <div className="py-8 text-center text-error">{tc('error')}</div>}
        {d && (
          <>
            {d.isSystem && (
              <div role="alert" className="alert alert-info mt-3 py-2 text-xs">
                <Info className="h-4 w-4 shrink-0" aria-hidden />
                {t('systemNote')}
              </div>
            )}
            <div className="mt-4 flex flex-col gap-3">
              <label className="form-control">
                <span className="label-text">{t('nameTh')} <span className="text-error">*</span></span>
                <input className="input input-bordered" value={nameTh}
                  onChange={(e) => setNameTh(e.target.value)} data-testid="role-rename-nameth" />
              </label>
              <label className="form-control">
                <span className="label-text">{t('description')}</span>
                <input className="input input-bordered" value={description}
                  onChange={(e) => setDescription(e.target.value)} data-testid="role-rename-desc" />
              </label>
            </div>
          </>
        )}
        <div className="modal-action">
          <button className="btn btn-ghost" onClick={onClose}>{tc('cancel')}</button>
          <button className="btn btn-primary" disabled={!canSave || save.isPending}
            onClick={submit} data-testid="role-rename-save">
            {save.isPending && <span className="loading loading-spinner loading-sm" />}
            {tc('save')}
          </button>
        </div>
      </div>
      <button className="modal-backdrop" aria-label={tc('close')} onClick={onClose} />
    </div>
  );
}

// ───────────────────────── Edit permissions ─────────────────────────

function EditPermissionsDialog({ roleId, onClose }: { roleId: number; onClose: () => void }) {
  const t = useTranslations('roles');
  const tc = useTranslations('common');
  const locale = useLocale();
  const role = useRbacRole(roleId);
  const catalog = usePermissionCatalog();
  const save = useSetRolePermissions();
  const [checked, setChecked] = useState<Set<string> | null>(null);

  const d = role.data;
  useEffect(() => {
    if (!d) return;
    setChecked(new Set(d.permissionCodes));
  }, [d]);

  // Group catalog by module in the canonical order; unknown modules tail-appended.
  const groups = useMemo(() => {
    const items = catalog.data ?? [];
    const byModule = new Map<string, PermissionCatalogItem[]>();
    for (const p of items) {
      const arr = byModule.get(p.module) ?? [];
      arr.push(p);
      byModule.set(p.module, arr);
    }
    const ordered = [
      ...MODULE_ORDER.filter((m) => byModule.has(m)),
      ...[...byModule.keys()].filter((m) => !MODULE_ORDER.includes(m)),
    ];
    return ordered.map((m) => ({ module: m, perms: byModule.get(m)! }));
  }, [catalog.data]);

  function toggle(code: string) {
    setChecked((cur) => {
      const next = new Set(cur ?? []);
      if (next.has(code)) next.delete(code); else next.add(code);
      return next;
    });
  }

  function toggleGroup(perms: PermissionCatalogItem[], on: boolean) {
    setChecked((cur) => {
      const next = new Set(cur ?? []);
      for (const p of perms) { if (on) next.add(p.code); else next.delete(p.code); }
      return next;
    });
  }

  async function submit() {
    if (!checked) return;
    try {
      await save.mutateAsync({ id: roleId, req: { permissionCodes: [...checked] } });
      toast.success(t('permissionsSaved'));
      onClose();
    } catch (e) {
      problemToast(e, tc('error'));
    }
  }

  const loading = role.isLoading || catalog.isLoading || checked == null;
  const isError = role.isError || catalog.isError;
  const label = (p: PermissionCatalogItem) => (locale === 'th' ? p.labelTh : p.labelEn);

  return (
    <div className="modal modal-open" role="dialog" aria-modal="true">
      <div className="modal-box max-w-3xl">
        <h3 className="text-lg font-bold">
          {t('permissionsTitle')}{d ? ` — ${d.nameTh}` : ''}
        </h3>

        {d?.isSystem && (
          <div role="alert" className="alert alert-info mt-3 py-2 text-xs">
            <Info className="h-4 w-4 shrink-0" aria-hidden />
            {t('systemNote')}
          </div>
        )}

        {loading && !isError && (
          <div className="py-8 text-center text-base-content/50">{tc('loading')}</div>
        )}
        {isError && <div className="py-8 text-center text-error">{tc('error')}</div>}

        {!loading && !isError && checked && (
          <div className="mt-4 max-h-[60vh] space-y-4 overflow-y-auto pr-1">
            {groups.map(({ module, perms }) => {
              const allOn = perms.every((p) => checked.has(p.code));
              return (
                <div key={module} className="rounded-lg border border-base-300 p-3"
                  data-testid={`perm-group-${module}`}>
                  <div className="mb-2 flex items-center justify-between">
                    <span className="text-sm font-semibold uppercase tracking-wide text-base-content/70">
                      {t(`module.${module}`)}
                    </span>
                    <label className="label cursor-pointer gap-2 py-0">
                      <span className="label-text text-xs">{t('selectAll')}</span>
                      <input type="checkbox" className="checkbox checkbox-xs" checked={allOn}
                        onChange={(e) => toggleGroup(perms, e.target.checked)}
                        data-testid={`perm-group-all-${module}`} />
                    </label>
                  </div>
                  <div className="grid grid-cols-1 gap-1.5 sm:grid-cols-2">
                    {perms.map((p) => (
                      <label key={p.code} className="flex cursor-pointer items-start gap-2 rounded px-1 py-1 hover:bg-base-200">
                        <input type="checkbox" className="checkbox checkbox-sm mt-0.5"
                          checked={checked.has(p.code)} onChange={() => toggle(p.code)}
                          data-testid={`perm-cb-${p.code}`} />
                        <span className="flex min-w-0 flex-col">
                          <span className="text-sm">{label(p)}</span>
                          <span className="font-mono text-[11px] text-base-content/50">{p.code}</span>
                        </span>
                      </label>
                    ))}
                  </div>
                </div>
              );
            })}
          </div>
        )}

        <div className="modal-action">
          <button className="btn btn-ghost" onClick={onClose}>{tc('cancel')}</button>
          <button className="btn btn-primary" disabled={loading || save.isPending}
            onClick={submit} data-testid="perm-save">
            {save.isPending && <span className="loading loading-spinner loading-sm" />}
            {tc('save')}
          </button>
        </div>
      </div>
      <button className="modal-backdrop" aria-label={tc('close')} onClick={onClose} />
    </div>
  );
}
