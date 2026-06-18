'use client';

import { useState } from 'react';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { Plus, KeyRound, RefreshCw, Copy } from 'lucide-react';
import { PageHeader } from '@/components/ui/PageHeader';
import {
  useApiKeys, useCreateApiKey, useRotateApiKey, useRevokeApiKey, useBusinessUnits,
} from '@/lib/queries';
import type { ApiKeyListItem, ApiKeyCreatedResult } from '@/lib/types';
import { formatDate } from '@/lib/utils';
import { useConfirm } from '@/hooks/useConfirm';
import { QueryState } from '@/components/states/QueryState';
import { PermissionGate } from '@/components/PermissionGate';

const SCOPE = 'sys.api_key.manage';

// Spec §6 — the externally-exposable scope subset (NOT admin/master-delete/tax).
const SCOPES = [
  'sales.tax_invoice.create', 'sales.tax_invoice.read', 'sales.tax_invoice.post',
  'sales.receipt.create', 'sales.receipt.read', 'sales.receipt.post',
  'sales.quotation.create', 'sales.quotation.read', 'sales.quotation.send',
  'master.customer.read', 'master.customer.manage', 'master.product.read',
  'sys.system_info.read',
] as const;

export default function ApiKeysSettingsPage() {
  const t = useTranslations('apiKey');
  const tc = useTranslations('common');
  const q = useApiKeys();
  const create = useCreateApiKey();
  const rotate = useRotateApiKey();
  const revoke = useRevokeApiKey();
  const confirm = useConfirm();
  const bus = useBusinessUnits().data ?? [];

  const [open, setOpen] = useState(false);
  const [name, setName] = useState('');
  const [scopes, setScopes] = useState<string[]>([]);
  const [buId, setBuId] = useState<number | null>(null);
  const [expires, setExpires] = useState('');
  const [secret, setSecret] = useState<ApiKeyCreatedResult | null>(null);

  const rows = q.data ?? [];

  function toggleScope(s: string) {
    setScopes((cur) => cur.includes(s) ? cur.filter((x) => x !== s) : [...cur, s]);
  }

  async function submit() {
    try {
      const res = await create.mutateAsync({
        name: name.trim(), scopes,
        expiresAt: expires ? new Date(expires + 'T00:00:00Z').toISOString() : null,
        defaultBusinessUnitId: buId,
      });
      setOpen(false);
      setName(''); setScopes([]); setBuId(null); setExpires('');
      setSecret(res);                       // show plaintext ONCE
    } catch (e) {
      toast.error((e as { detail?: string })?.detail ?? tc('error'));
    }
  }

  async function doRotate(id: number) {
    try { setSecret(await rotate.mutateAsync(id)); }
    catch (e) { toast.error((e as { detail?: string })?.detail ?? tc('error')); }
  }

  async function doRevoke(id: number) {
    if (!(await confirm({ description: t('revokeConfirm'), variant: 'destructive' }))) return;
    try { await revoke.mutateAsync(id); toast.success(t('revoked')); }
    catch (e) { toast.error((e as { detail?: string })?.detail ?? tc('error')); }
  }

  function copy(v: string) {
    navigator.clipboard?.writeText(v); toast.success(t('copied'));
  }

  return (
    <>
      <PageHeader
        title={t('title')}
        actions={
          <PermissionGate scope={SCOPE}>
            <button className="btn btn-primary btn-sm gap-1" data-testid="api-key-new"
              onClick={() => setOpen(true)}>
              <Plus className="h-4 w-4" aria-hidden /> {t('create')}
            </button>
          </PermissionGate>
        }
      />

      <QueryState query={q} isEmpty={!q.isLoading && rows.length === 0}>
      <div className="overflow-x-auto rounded-lg border border-base-300">
        <table className="table table-zebra">
          <thead><tr>
            <th>{t('name')}</th><th>{t('prefix')}</th><th>{t('scopes')}</th>
            <th>{t('defaultBu')}</th><th>{t('lastUsed')}</th><th>{t('expires')}</th>
            <th>{t('status')}</th><th className="w-28" />
          </tr></thead>
          <tbody>
            {rows.map((k: ApiKeyListItem) => (
              <tr key={k.apiKeyId} className="hover">
                <td>{k.name}</td>
                <td className="font-mono text-xs">{k.keyPrefix}…</td>
                <td className="max-w-xs"><div className="flex flex-wrap gap-1">
                  {k.scopes.map((s) => <span key={s} className="badge badge-ghost badge-xs">{s}</span>)}
                </div></td>
                <td>{k.defaultBusinessUnitCode
                  ? <span className="badge badge-info badge-sm">{k.defaultBusinessUnitCode}</span>
                  : <span className="text-base-content/40">—</span>}</td>
                <td className="tabular-nums text-xs">{k.lastUsedAt ? formatDate(k.lastUsedAt) : '—'}</td>
                <td className="tabular-nums text-xs">{k.expiresAt ? formatDate(k.expiresAt) : '—'}</td>
                <td>
                  {k.revokedAt
                    ? <span className="badge badge-error badge-sm">{t('revoked')}</span>
                    : k.isActive
                      ? <span className="badge badge-success badge-sm">{t('active')}</span>
                      : <span className="badge badge-ghost badge-sm">{t('inactive')}</span>}
                </td>
                <td className="flex gap-1">
                  {!k.revokedAt && (
                    <PermissionGate scope={SCOPE}>
                      <button className="btn btn-ghost btn-xs" aria-label={t('rotate')}
                        onClick={() => doRotate(k.apiKeyId)}>
                        <RefreshCw className="h-3 w-3" aria-hidden />
                      </button>
                      <button className="btn btn-ghost btn-xs text-error"
                        onClick={() => doRevoke(k.apiKeyId)}>{t('revoke')}</button>
                    </PermissionGate>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      </QueryState>

      {open && (
        <div className="modal modal-open" role="dialog" aria-modal="true">
          <div className="modal-box max-w-lg">
            <h3 className="text-lg font-bold">{t('create')}</h3>
            <div className="mt-4 space-y-3">
              <label className="form-control">
                <span className="label-text">{t('name')} *</span>
                <input className="input input-bordered" value={name}
                  data-testid="api-key-name"
                  onChange={(e) => setName(e.target.value)} />
              </label>
              <div className="form-control">
                <span className="label-text">{t('scopes')} *</span>
                <div className="mt-1 grid max-h-44 grid-cols-1 gap-1 overflow-y-auto rounded border border-base-300 p-2">
                  {SCOPES.map((s) => (
                    <label key={s} className="label cursor-pointer justify-start gap-2 py-0.5">
                      <input type="checkbox" className="checkbox checkbox-xs"
                        checked={scopes.includes(s)} onChange={() => toggleScope(s)} />
                      <span className="label-text font-mono text-xs">{s}</span>
                    </label>
                  ))}
                </div>
              </div>
              <label className="form-control">
                <span className="label-text">{t('defaultBu')}</span>
                <select className="select select-bordered" data-testid="api-key-bu"
                  value={buId ?? ''}
                  onChange={(e) => setBuId(e.target.value ? Number(e.target.value) : null)}>
                  <option value="">{t('noBu')}</option>
                  {bus.filter((b) => b.isActive).map((b) => (
                    <option key={b.businessUnitId} value={b.businessUnitId}>
                      {b.code} — {b.nameTh}
                    </option>
                  ))}
                </select>
              </label>
              <label className="form-control">
                <span className="label-text">{t('expires')}</span>
                <input type="date" className="input input-bordered" value={expires}
                  onChange={(e) => setExpires(e.target.value)} />
              </label>
            </div>
            <div className="modal-action">
              <button className="btn btn-ghost" onClick={() => setOpen(false)}>{tc('cancel')}</button>
              <button className="btn btn-primary" data-testid="api-key-submit"
                disabled={name.trim() === '' || scopes.length === 0 || create.isPending}
                onClick={submit}>{t('create')}</button>
            </div>
          </div>
          <button className="modal-backdrop" aria-label={tc('close')} onClick={() => setOpen(false)} />
        </div>
      )}

      {secret && (
        <div className="modal modal-open" role="dialog" aria-modal="true">
          <div className="modal-box">
            <h3 className="text-lg font-bold">{t('secretTitle')}</h3>
            <p className="mt-1 text-sm text-warning">⚠ {t('secretWarning')}</p>
            <div className="mt-3 flex items-center gap-2 rounded border border-base-300 bg-base-200 p-2">
              <code className="flex-1 break-all font-mono text-sm" data-testid="api-key-plaintext">
                {secret.plaintext}
              </code>
              <button className="btn btn-ghost btn-sm" aria-label={t('copy')}
                onClick={() => copy(secret.plaintext)}>
                <Copy className="h-4 w-4" aria-hidden />
              </button>
            </div>
            <div className="modal-action">
              <button className="btn btn-primary" onClick={() => setSecret(null)}>
                {t('savedIt')}
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  );
}
