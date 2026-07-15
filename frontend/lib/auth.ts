import { ApiError } from './api';
import { LAST_COMPANY_KEY } from './utils';

/**
 * Auth talks to same-origin BFF routes (app/api/auth/*), NOT the backend directly.
 * The JWT lives only in an httpOnly cookie set by the route handler — never in JS.
 */
export type LoginResponse =
  | { mfa_required: true }
  | { ok: true };

async function postSameOrigin<T>(path: string, payload: unknown): Promise<T> {
  const res = await fetch(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  });
  const body = await res.json().catch(() => null);
  if (!res.ok) {
    const code = body?.title ?? `http_${res.status}`;
    const detail = body?.detail ?? body?.message ?? res.statusText;
    throw new ApiError(res.status, code, detail, body);
  }
  return body as T;
}

export const auth = {
  login: (username: string, password: string, mfaCode?: string) =>
    postSameOrigin<LoginResponse>('/api/auth/login', { username, password, mfaCode }),

  logout: () => postSameOrigin<{ ok: true }>('/api/auth/logout', {}),
};

// WP4 4.4 (F17) / F-C follow-up — a super-admin's session always came back on the
// default company after a fresh login, silently dropping whatever company (e.g. a
// test BU) they were last working in. If a different, still-accessible company was
// remembered (CompanySwitcher persists it), switch to it right after login. Fails
// silently on any error — this is a convenience, never allowed to block/trap login.
// Shared by the login page (post-credentials) and SessionExpiredModal (post
// in-place re-login), so an in-progress form holding company-X ids lands back on
// company X instead of failing to save under the wrong tenant.
export async function restoreLastCompany(): Promise<void> {
  const wanted = Number(localStorage.getItem(LAST_COMPANY_KEY));
  if (!Number.isInteger(wanted) || wanted <= 0) return;
  try {
    const me = await fetch('/api/proxy/me', { cache: 'no-store' }).then((r) => (r.ok ? r.json() : null));
    if (!me?.isSuperAdmin || me.companyId === wanted) return;
    const allowed = (me.allowedCompanies as { id: number }[] | undefined)?.some((c) => c.id === wanted);
    if (!allowed) return;
    await fetch('/api/auth/switch-company', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ companyId: wanted }),
    });
  } catch {
    // best-effort — the user just stays on the default company, same as before.
  }
}
