import { cookies } from 'next/headers';
import { NextRequest, NextResponse } from 'next/server';

/**
 * BFF for the consent "accept" (POST /oauth/authorize on the backend).
 *
 * The consent page (app/(dashboard)/oauth/consent) POSTs JSON here: the original authorize
 * params + the chosen company_id + approve('true'|'false'). This route:
 *   1. ANTIFORGERY — verifies the Origin (or Referer) host matches THIS app's Host header;
 *      rejects (403) otherwise. A forged cross-site POST also lacks the httpOnly cookie, so
 *      it fails twice over. (Header-to-header check; req.nextUrl.origin is unreliable behind
 *      Cloudflare.)
 *   2. AUTH SEAM — reads the httpOnly `access_token` cookie and forwards it as
 *      `Authorization: Bearer` (the backend authenticates the consenting user via the JWT,
 *      NOT the cookie), with the params re-encoded as application/x-www-form-urlencoded.
 *   3. RELAY — captures the backend's 302 `location` (redirect_uri?code&state) and returns
 *      it as JSON { redirect } so the client-side consent page can window.location = redirect.
 *
 * Note: granted SCOPES are authoritative server-side (McpScopes.Normalize) — the browser can
 * post scope but the backend caps it; company_id is validated against the user's memberships.
 */
export const runtime = 'nodejs';
export const dynamic = 'force-dynamic';

const BACKEND = process.env.BACKEND_API_URL ?? 'http://localhost:5080';

// The OpenIddict authorize params the backend re-validates. Anything else is ignored.
const AUTHORIZE_PARAMS = [
  'client_id', 'redirect_uri', 'response_type',
  'code_challenge', 'code_challenge_method',
  'scope', 'state', 'resource', 'nonce',
];

function hostOf(u: string | null): string | null {
  if (!u) return null;
  try {
    return new URL(u).host;
  } catch {
    return null;
  }
}

export async function POST(req: NextRequest) {
  // 1. Antiforgery: Origin/Referer host must equal our own Host header.
  const host = req.headers.get('host');
  const sourceHost = hostOf(req.headers.get('origin')) ?? hostOf(req.headers.get('referer'));
  if (!host || !sourceHost || sourceHost !== host) {
    return NextResponse.json(
      { title: 'auth.forbidden', detail: 'Cross-origin request rejected.' },
      { status: 403 },
    );
  }

  // 2. Session cookie → Bearer for the backend.
  const store = await cookies();
  const token = store.get('access_token')?.value;
  if (!token) {
    return NextResponse.json(
      { title: 'auth.unauthenticated', detail: 'No session.' },
      { status: 401 },
    );
  }

  const payload = await req.json().catch(() => null);
  if (!payload || typeof payload !== 'object') {
    return NextResponse.json(
      { title: 'auth.bad_request', detail: 'Invalid request body.' },
      { status: 400 },
    );
  }

  const form = new URLSearchParams();
  for (const k of AUTHORIZE_PARAMS) {
    const v = (payload as Record<string, unknown>)[k];
    if (typeof v === 'string' && v.length > 0) form.set(k, v);
  }
  const companyId = (payload as Record<string, unknown>).company_id;
  if (companyId != null && companyId !== '') form.set('company_id', String(companyId));
  const approve = (payload as Record<string, unknown>).approve;
  form.set('approve', approve === 'true' || approve === true ? 'true' : 'false');

  let upstream: Response;
  try {
    upstream = await fetch(`${BACKEND}/oauth/authorize`, {
      method: 'POST',
      headers: {
        'content-type': 'application/x-www-form-urlencoded',
        accept: 'application/json',
        Authorization: `Bearer ${token}`,
      },
      body: form.toString(),
      cache: 'no-store',
      redirect: 'manual', // capture the 302 Location (redirect_uri?code), do NOT follow
    });
  } catch (e) {
    console.error('[/api/oauth/accept] upstream fetch failed:', e);
    return NextResponse.json(
      { title: 'gateway.error', detail: 'Connection to OAuth backend failed.' },
      { status: 502 },
    );
  }

  // On success (approve, or a deny that redirects with error=access_denied) the backend
  // 3xx-redirects to the client's redirect_uri. Hand that URL back to the browser.
  const location = upstream.headers.get('location');
  if (upstream.status >= 300 && upstream.status < 400 && location) {
    return NextResponse.json({ redirect: location }, { status: 200 });
  }

  // No redirect → surface the backend's status/body so the consent page can show the error.
  const body = await upstream.text().catch(() => '');
  return new NextResponse(body || null, {
    status: upstream.status,
    headers: { 'content-type': upstream.headers.get('content-type') ?? 'application/json' },
  });
}
