'use client';

import { useEffect, useState } from 'react';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { Pencil, ShieldAlert } from 'lucide-react';
import { PageHeader } from '@/components/ui/PageHeader';
import { QueryState } from '@/components/states/QueryState';
import { problemToast } from '@/lib/api';
import {
  useCompanies, useMePermissions,
  useRbacUsers, useRbacRoles, useSetUserRoles,
} from '@/lib/queries';
import type { RbacUserListItem } from '@/lib/types';

export default function UsersSettingsPage() {
  const t = useTranslations('users');
  const perms = useMePermissions();
  const isSuperAdmin = perms.data?.isSuperAdmin ?? false;
  const canManage = isSuperAdmin || (perms.data?.permissions.includes('sys.user.manage') ?? false);

  const [companyId, setCompanyId] = useState<number | undefined>(undefined);

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
      <UsersBody isSuperAdmin={isSuperAdmin} companyId={companyId} />
    </>
  );
}

// useCompanies() mounts only for super-admins (the /companies endpoint enforces
// master.company.manage → 403 for company-admins).
function CompanySelector({
  value, onChange,
}: { value: number | undefined; onChange: (id: number | undefined) => void }) {
  const t = useTranslations('users');
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

function UsersBody({
  isSuperAdmin, companyId,
}: { isSuperAdmin: boolean; companyId: number | undefined }) {
  const t = useTranslations('users');
  const tc = useTranslations('common');
  const enabled = !isSuperAdmin || companyId != null;
  const q = useRbacUsers(companyId, enabled);
  const rows = enabled ? (q.data ?? []) : [];
  const [editingId, setEditingId] = useState<number | null>(null);

  if (!enabled) {
    return <div className="py-12 text-center text-base-content/50">{tc('loading')}</div>;
  }

  return (
    <>
      <QueryState query={q} isEmpty={!q.isLoading && rows.length === 0}>
        <div className="overflow-x-auto rounded-lg border border-base-300">
          <table className="table table-zebra">
            <thead>
              <tr>
                <th>{t('username')}</th>
                <th>{t('fullName')}</th>
                <th>{t('status')}</th>
                <th>{t('roles')}</th>
                <th className="w-24" />
              </tr>
            </thead>
            <tbody>
              {rows.map((u: RbacUserListItem) => (
                <tr key={u.userId} data-testid={`user-row-${u.userId}`}>
                  <td className="font-mono text-sm">{u.username}</td>
                  <td>{u.fullName}</td>
                  <td>
                    <div className="flex flex-wrap gap-1">
                      {u.isActive
                        ? <span className="badge badge-outline badge-sm">{tc('active')}</span>
                        : <span className="badge badge-error badge-sm">{tc('inactive')}</span>}
                      {u.isSuperAdmin && (
                        <span className="badge badge-warning badge-sm">{t('superAdmin')}</span>
                      )}
                    </div>
                  </td>
                  <td>
                    <div className="flex flex-wrap gap-1">
                      {u.roles.length === 0 && (
                        <span className="text-xs text-base-content/40">—</span>
                      )}
                      {u.roles.map((r) => (
                        <span key={r.roleId} className="badge badge-ghost badge-sm" title={r.roleCode}>
                          {r.nameTh}
                        </span>
                      ))}
                    </div>
                  </td>
                  <td>
                    <button className="btn btn-ghost btn-xs gap-1"
                      onClick={() => setEditingId(u.userId)}
                      data-testid={`user-edit-${u.userId}`}>
                      <Pencil className="h-3 w-3" aria-hidden /> {t('editRoles')}
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </QueryState>

      {editingId != null && (
        <EditUserRolesDialog
          user={rows.find((u) => u.userId === editingId)!}
          companyId={companyId}
          onClose={() => setEditingId(null)}
        />
      )}
    </>
  );
}

function EditUserRolesDialog({
  user, companyId, onClose,
}: { user: RbacUserListItem; companyId: number | undefined; onClose: () => void }) {
  const t = useTranslations('users');
  const tc = useTranslations('common');
  const rolesQ = useRbacRoles(companyId);
  const save = useSetUserRoles();
  const [selected, setSelected] = useState<Set<number>>(
    () => new Set(user.roles.map((r) => r.roleId)),
  );

  const roleRows = rolesQ.data ?? [];

  function toggle(id: number) {
    setSelected((cur) => {
      const next = new Set(cur);
      if (next.has(id)) next.delete(id); else next.add(id);
      return next;
    });
  }

  async function submit() {
    try {
      await save.mutateAsync({
        id: user.userId,
        req: { roleIds: [...selected], companyId },
      });
      toast.success(t('rolesSaved'));
      onClose();
    } catch (e) {
      problemToast(e, tc('error'));
    }
  }

  return (
    <div className="modal modal-open" role="dialog" aria-modal="true">
      <div className="modal-box">
        <h3 className="text-lg font-bold">{t('editRolesTitle')} — {user.fullName}</h3>
        <p className="mt-1 font-mono text-xs text-base-content/50">{user.username}</p>

        {rolesQ.isLoading && (
          <div className="py-8 text-center text-base-content/50">{tc('loading')}</div>
        )}
        {rolesQ.isError && <div className="py-8 text-center text-error">{tc('error')}</div>}

        {!rolesQ.isLoading && !rolesQ.isError && (
          <div className="mt-4 max-h-[55vh] space-y-1 overflow-y-auto pr-1">
            {roleRows.length === 0 && (
              <div className="py-6 text-center text-sm text-base-content/50">{tc('empty')}</div>
            )}
            {roleRows.map((r) => (
              <label key={r.roleId}
                className="flex cursor-pointer items-start gap-2 rounded px-1 py-1.5 hover:bg-base-200">
                <input type="checkbox" className="checkbox checkbox-sm mt-0.5"
                  checked={selected.has(r.roleId)} onChange={() => toggle(r.roleId)}
                  data-testid={`user-role-cb-${r.roleId}`} />
                <span className="flex min-w-0 flex-col">
                  <span className="text-sm">{r.nameTh}</span>
                  <span className="font-mono text-[11px] text-base-content/50">{r.roleCode}</span>
                </span>
              </label>
            ))}
          </div>
        )}

        <div className="modal-action">
          <button className="btn btn-ghost" onClick={onClose}>{tc('cancel')}</button>
          <button className="btn btn-primary" disabled={rolesQ.isLoading || save.isPending}
            onClick={submit} data-testid="user-roles-save">
            {save.isPending && <span className="loading loading-spinner loading-sm" />}
            {tc('save')}
          </button>
        </div>
      </div>
      <button className="modal-backdrop" aria-label={tc('close')} onClick={onClose} />
    </div>
  );
}
