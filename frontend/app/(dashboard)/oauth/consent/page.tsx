'use client';

import { Suspense, useEffect, useMemo, useState } from 'react';
import { useSearchParams } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { Loader2, ShieldCheck } from 'lucide-react';

/**
 * OAuth consent screen for TEAS Connect (MCP). Session-gated: it lives under (dashboard),
 * so middleware requires the `access_token` cookie (a logged-out client is sent to /login
 * by the backend authorize 302 first). It reads the original authorize params from the
 * query string (round-tripped by the backend authorize 302 — OpenIddict re-validates them
 * on the POST), shows the requesting client + a fixed read/draft-only message + a company
 * picker (the session user's /me allowedCompanies), and on Approve/Deny POSTs to the BFF
 * /api/oauth/accept, then follows the returned redirect_uri?code back to the MCP client.
 */

type AllowedCompany = { id: number; nameTh: string; nameEn: string | null };
type Me = { companyId: number; allowedCompanies: AllowedCompany[] };

// The authorize params the backend re-validates; forwarded verbatim to the accept BFF.
const AUTHORIZE_KEYS = [
  'client_id', 'redirect_uri', 'response_type',
  'code_challenge', 'code_challenge_method',
  'scope', 'state', 'resource', 'nonce',
] as const;

function ConsentInner() {
  const t = useTranslations('oauthConsent');
  const sp = useSearchParams();

  const [me, setMe] = useState<Me | null>(null);
  const [loadingMe, setLoadingMe] = useState(true);
  const [companyId, setCompanyId] = useState<number | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const params = useMemo(() => {
    const out: Record<string, string> = {};
    for (const k of AUTHORIZE_KEYS) {
      const v = sp.get(k);
      if (v) out[k] = v;
    }
    return out;
  }, [sp]);

  const clientId = params.client_id ?? '';
  const invalid = !clientId || !params.redirect_uri || !params.response_type;

  useEffect(() => {
    let alive = true;
    fetch('/api/proxy/me', { cache: 'no-store' })
      .then((r) => (r.ok ? r.json() : null))
      .then((data: Me | null) => {
        if (!alive) return;
        setMe(data);
        const first = data?.allowedCompanies?.[0];
        if (first) {
          const inList = data!.allowedCompanies.some((c) => c.id === data!.companyId);
          setCompanyId(inList ? data!.companyId : first.id);
        }
      })
      .catch(() => {
        if (alive) setMe(null);
      })
      .finally(() => {
        if (alive) setLoadingMe(false);
      });
    return () => {
      alive = false;
    };
  }, []);

  const companies = me?.allowedCompanies ?? [];

  async function decide(approve: boolean) {
    if (submitting) return;
    if (approve && !companyId) {
      setError(t('selectCompany'));
      return;
    }
    setSubmitting(true);
    setError(null);
    try {
      const res = await fetch('/api/oauth/accept', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ ...params, company_id: companyId, approve: approve ? 'true' : 'false' }),
      });
      const data = await res.json().catch(() => null);
      if (res.ok && data?.redirect) {
        window.location.href = data.redirect as string;
        return;
      }
      setError((data as { detail?: string })?.detail ?? t('error'));
    } catch {
      setError(t('error'));
    } finally {
      setSubmitting(false);
    }
  }

  if (invalid) {
    return (
      <div className="mx-auto mt-10 max-w-md">
        <div className="rounded-lg border border-error/30 bg-error/5 p-6 text-center">
          <p className="font-medium text-error">{t('invalidRequest')}</p>
        </div>
      </div>
    );
  }

  return (
    <div className="mx-auto mt-10 max-w-md">
      <div className="rounded-lg border border-base-300 bg-base-100 p-6 shadow-sm">
        <div className="flex items-center gap-3">
          <ShieldCheck className="h-8 w-8 text-primary" aria-hidden />
          <h1 className="text-lg font-bold">{t('title')}</h1>
        </div>

        <div className="mt-4 rounded border border-base-300 bg-base-200 p-3">
          <p className="text-xs text-base-content/60">{t('clientLabel')}</p>
          <p className="break-all font-mono text-sm" data-testid="consent-client-id">{clientId}</p>
        </div>

        <p className="mt-4 text-sm text-base-content/80" data-testid="consent-description">
          {t('description')}
        </p>

        <div className="mt-5">
          <label className="form-control">
            <span className="label-text">{t('companyLabel')}</span>
            {loadingMe ? (
              <span className="mt-1 flex items-center gap-2 text-sm text-base-content/60">
                <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> {t('loading')}
              </span>
            ) : companies.length === 0 ? (
              <span className="mt-1 text-sm text-warning" data-testid="consent-no-companies">
                {t('noCompanies')}
              </span>
            ) : (
              <select
                className="select select-bordered mt-1"
                data-testid="consent-company"
                value={companyId ?? ''}
                onChange={(e) => setCompanyId(e.target.value ? Number(e.target.value) : null)}
              >
                {companies.map((c) => (
                  <option key={c.id} value={c.id}>{c.nameTh}</option>
                ))}
              </select>
            )}
          </label>
        </div>

        {error && (
          <p className="mt-3 text-sm text-error" data-testid="consent-error">{error}</p>
        )}

        <div className="mt-6 flex justify-end gap-2">
          <button
            className="btn btn-ghost"
            data-testid="consent-deny"
            disabled={submitting}
            onClick={() => decide(false)}
          >
            {t('deny')}
          </button>
          <button
            className="btn btn-primary"
            data-testid="consent-approve"
            disabled={submitting || companies.length === 0}
            onClick={() => decide(true)}
          >
            {submitting && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />}
            {t('approve')}
          </button>
        </div>
      </div>
    </div>
  );
}

export default function OAuthConsentPage() {
  return (
    <Suspense
      fallback={
        <div className="flex min-h-[40vh] items-center justify-center">
          <Loader2 className="h-6 w-6 animate-spin" aria-hidden />
        </div>
      }
    >
      <ConsentInner />
    </Suspense>
  );
}
