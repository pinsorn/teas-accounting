'use client';

import { useState } from 'react';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { Plus, RefreshCw, Copy } from 'lucide-react';
import { PageHeader } from '@/components/ui/PageHeader';
import {
  useApiKeys, useCreateApiKey, useRotateApiKey, useRevokeApiKey, useBusinessUnits,
} from '@/lib/queries';
import type { ApiKeyListItem, ApiKeyCreatedResult, ApiKeyKind } from '@/lib/types';
import { formatDate } from '@/lib/utils';
import { useConfirm } from '@/hooks/useConfirm';
import { QueryState } from '@/components/states/QueryState';
import { PermissionGate } from '@/components/PermissionGate';

const SCOPE = 'sys.api_key.manage';

// Spec §6 / E6 — the externally-exposable scope subset (NOT admin/master-delete/tax).
const ALL_SCOPES = [
  'sales.tax_invoice.create', 'sales.tax_invoice.read', 'sales.tax_invoice.post',
  'sales.receipt.create', 'sales.receipt.read', 'sales.receipt.post',
  'sales.quotation.create', 'sales.quotation.read', 'sales.quotation.send',
  'master.customer.read', 'master.customer.manage',
  'master.product.read', 'master.product.manage',
  'master.vendor.manage',
  'purchase.purchase_order.read', 'purchase.purchase_order.create',
  'purchase.vendor_invoice.read', 'purchase.vendor_invoice.create',
  'purchase.payment_voucher.read', 'purchase.payment_voucher.create',
  'sys.system_info.read',
] as const;

// MCP keys cannot hold .post scopes (M1 backend guard). Mirror that constraint
// in the UI so users don't hit a confusing 400.
const POST_SCOPES = new Set([
  'sales.tax_invoice.post', 'sales.receipt.post',
]);

// Default scopes pre-selected when user picks mcp kind.
// E6: expanded to include purchase read+create and master manage scopes.
const MCP_DEFAULT_SCOPES = [
  'sales.tax_invoice.create', 'sales.tax_invoice.read',
  'sales.receipt.create', 'sales.receipt.read',
  'sales.quotation.create', 'sales.quotation.read',
  'master.customer.read', 'master.customer.manage',
  'master.product.read', 'master.product.manage',
  'master.vendor.manage',
  'purchase.purchase_order.read', 'purchase.purchase_order.create',
  'purchase.vendor_invoice.read', 'purchase.vendor_invoice.create',
  'purchase.payment_voucher.read', 'purchase.payment_voucher.create',
  'sys.system_info.read',
];

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
  const [kind, setKind] = useState<ApiKeyKind>('integration');
  const [scopes, setScopes] = useState<string[]>([]);
  const [buId, setBuId] = useState<number | null>(null);
  const [expires, setExpires] = useState('');
  const [secret, setSecret] = useState<ApiKeyCreatedResult | null>(null);
  // Track what kind was used when creating — drives MCP panel display.
  const [secretKind, setSecretKind] = useState<ApiKeyKind>('integration');

  const rows = q.data ?? [];

  // Scopes available depend on kind — mcp cannot hold .post.
  const availableScopes = ALL_SCOPES.filter((s) => kind !== 'mcp' || !POST_SCOPES.has(s));

  function handleKindChange(next: ApiKeyKind) {
    setKind(next);
    // Re-seed scopes to avoid stale .post entries on mcp switch.
    if (next === 'mcp') {
      setScopes(MCP_DEFAULT_SCOPES);
    } else {
      setScopes([]);
    }
  }

  function toggleScope(s: string) {
    setScopes((cur) => cur.includes(s) ? cur.filter((x) => x !== s) : [...cur, s]);
  }

  function resetForm() {
    setName(''); setKind('integration'); setScopes([]); setBuId(null); setExpires('');
  }

  async function submit() {
    try {
      const res = await create.mutateAsync({
        name: name.trim(), scopes, kind,
        expiresAt: expires ? new Date(expires + 'T00:00:00Z').toISOString() : null,
        defaultBusinessUnitId: buId,
      });
      setOpen(false);
      setSecretKind(kind);
      resetForm();
      setSecret(res); // show plaintext ONCE
    } catch (e) {
      toast.error((e as { detail?: string })?.detail ?? tc('error'));
    }
  }

  async function doRotate(id: number) {
    try { setSecret(await rotate.mutateAsync(id)); setSecretKind('integration'); }
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

  // Claude Code — remote HTTP MCP with a static header (.mcp.json shape; or `claude mcp add
  // --transport http teas <url> --header "X-Api-Key: <key>"`).
  function mcpConfigSnippet(key: string): string {
    const origin = typeof window !== 'undefined' ? window.location.origin : '';
    return JSON.stringify(
      { mcpServers: { teas: { type: 'http', url: `${origin}/mcp`, headers: { 'X-Api-Key': key } } } },
      null, 2,
    );
  }

  // Claude Desktop has no static-header field in its config — bridge via mcp-remote (stdio).
  // The key goes in env; the header uses NO space after the colon to dodge a known Windows
  // arg-mangling bug in npx. (Mobile / the in-app Custom Connector can't do this — OAuth only.)
  function mcpDesktopSnippet(key: string): string {
    const origin = typeof window !== 'undefined' ? window.location.origin : '';
    return JSON.stringify(
      {
        mcpServers: {
          teas: {
            command: 'npx',
            args: ['mcp-remote', `${origin}/mcp`, '--header', 'X-Api-Key:${TEAS_API_KEY}'],
            env: { TEAS_API_KEY: key },
          },
        },
      },
      null, 2,
    );
  }

  function mcpOrigin(): string {
    return typeof window !== 'undefined' ? window.location.origin : '';
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
              <th>{t('name')}</th><th>{t('kind')}</th><th>{t('prefix')}</th><th>{t('scopes')}</th>
              <th>{t('defaultBu')}</th><th>{t('lastUsed')}</th><th>{t('expires')}</th>
              <th>{t('status')}</th><th className="w-28" />
            </tr></thead>
            <tbody>
              {rows.map((k: ApiKeyListItem) => (
                <tr key={k.apiKeyId} className="hover">
                  <td>{k.name}</td>
                  <td>
                    {k.kind === 'mcp'
                      ? <span className="badge badge-secondary badge-sm">MCP</span>
                      : <span className="badge badge-ghost badge-sm">integration</span>}
                  </td>
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

      {/* Create dialog */}
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
              {/* Kind selector */}
              <div className="form-control">
                <span className="label-text">{t('kind')} *</span>
                <div className="mt-1 flex gap-4">
                  <label className="label cursor-pointer gap-2">
                    <input type="radio" className="radio radio-sm"
                      data-testid="api-key-kind-integration"
                      checked={kind === 'integration'}
                      onChange={() => handleKindChange('integration')} />
                    <span className="label-text">{t('kindIntegration')}</span>
                  </label>
                  <label className="label cursor-pointer gap-2">
                    <input type="radio" className="radio radio-sm radio-secondary"
                      data-testid="api-key-kind-mcp"
                      checked={kind === 'mcp'}
                      onChange={() => handleKindChange('mcp')} />
                    <span className="label-text">{t('kindMcp')}</span>
                  </label>
                </div>
              </div>
              <div className="form-control">
                <span className="label-text">{t('scopes')} *</span>
                {kind === 'mcp' && (
                  <p className="mt-1 text-xs text-base-content/60">
                    {t('mcpScopeNote')}
                  </p>
                )}
                <div className="mt-1 grid max-h-44 grid-cols-1 gap-1 overflow-y-auto rounded border border-base-300 p-2">
                  {availableScopes.map((s) => (
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
              <button className="btn btn-ghost" onClick={() => { setOpen(false); resetForm(); }}>{tc('cancel')}</button>
              <button className="btn btn-primary" data-testid="api-key-submit"
                disabled={name.trim() === '' || scopes.length === 0 || create.isPending}
                onClick={submit}>{t('create')}</button>
            </div>
          </div>
          <button className="modal-backdrop" aria-label={tc('close')} onClick={() => { setOpen(false); resetForm(); }} />
        </div>
      )}

      {/* Secret reveal — MCP kind gets setup panel, integration gets plain reveal */}
      {secret && (
        <div className="modal modal-open" role="dialog" aria-modal="true">
          <div className="modal-box max-w-2xl">
            <h3 className="text-lg font-bold">{t('secretTitle')}</h3>
            <p className="mt-1 text-sm text-warning">⚠ {t('secretWarning')}</p>

            {/* One-time key reveal (always shown) */}
            <div className="mt-3 flex items-center gap-2 rounded border border-base-300 bg-base-200 p-2">
              <code className="flex-1 break-all font-mono text-sm" data-testid="api-key-plaintext">
                {secret.plaintext}
              </code>
              <button className="btn btn-ghost btn-sm" aria-label={t('copy')}
                onClick={() => copy(secret.plaintext)}>
                <Copy className="h-4 w-4" aria-hidden />
              </button>
            </div>

            {/* MCP setup instructions panel */}
            {secretKind === 'mcp' && (
              <div className="mt-5 space-y-4 rounded-lg border border-secondary/30 bg-secondary/5 p-4">
                <h4 className="font-semibold text-secondary">{t('mcpSetupTitle')}</h4>

                {/* Endpoint URL */}
                <div>
                  <p className="mb-1 text-xs font-medium text-base-content/70">{t('mcpEndpointLabel')}</p>
                  <div className="flex items-center gap-2 rounded border border-base-300 bg-base-200 p-2">
                    <code className="flex-1 font-mono text-sm" data-testid="mcp-endpoint-url">
                      {mcpOrigin()}/mcp
                    </code>
                    <button className="btn btn-ghost btn-sm" aria-label={t('copy')}
                      onClick={() => copy(`${mcpOrigin()}/mcp`)}>
                      <Copy className="h-4 w-4" aria-hidden />
                    </button>
                  </div>
                </div>

                {/* Auth header */}
                <div>
                  <p className="mb-1 text-xs font-medium text-base-content/70">{t('mcpHeaderLabel')}</p>
                  <div className="flex items-center gap-2 rounded border border-base-300 bg-base-200 p-2">
                    <code className="flex-1 break-all font-mono text-sm" data-testid="mcp-auth-header">
                      X-Api-Key: {secret.plaintext}
                    </code>
                    <button className="btn btn-ghost btn-sm" aria-label={t('copy')}
                      onClick={() => copy(`X-Api-Key: ${secret.plaintext}`)}>
                      <Copy className="h-4 w-4" aria-hidden />
                    </button>
                  </div>
                </div>

                {/* Claude Code config snippet (static X-Api-Key, remote HTTP) */}
                <div>
                  <p className="mb-1 text-xs font-medium text-base-content/70">{t('mcpConfigLabel')}</p>
                  <div className="relative rounded border border-base-300 bg-base-200">
                    <button className="btn btn-ghost btn-sm absolute right-1 top-1" aria-label={t('copy')}
                      onClick={() => copy(mcpConfigSnippet(secret.plaintext))}>
                      <Copy className="h-4 w-4" aria-hidden />
                    </button>
                    <pre className="overflow-x-auto p-3 pr-12 font-mono text-xs" data-testid="mcp-config-snippet">
                      {mcpConfigSnippet(secret.plaintext)}
                    </pre>
                  </div>
                </div>

                {/* Claude Desktop config snippet (via mcp-remote stdio bridge) */}
                <div>
                  <p className="mb-1 text-xs font-medium text-base-content/70">{t('mcpDesktopLabel')}</p>
                  <div className="relative rounded border border-base-300 bg-base-200">
                    <button className="btn btn-ghost btn-sm absolute right-1 top-1" aria-label={t('copy')}
                      onClick={() => copy(mcpDesktopSnippet(secret.plaintext))}>
                      <Copy className="h-4 w-4" aria-hidden />
                    </button>
                    <pre className="overflow-x-auto p-3 pr-12 font-mono text-xs" data-testid="mcp-desktop-snippet">
                      {mcpDesktopSnippet(secret.plaintext)}
                    </pre>
                  </div>
                </div>

                {/* Mobile / in-app connector — OAuth only, not yet supported with an API key */}
                <p className="text-xs text-warning/90" data-testid="mcp-mobile-note">{t('mcpMobileNote')}</p>

                {/* Scope note */}
                <p className="text-xs text-base-content/60">{t('mcpScopeNote')}</p>
              </div>
            )}

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
