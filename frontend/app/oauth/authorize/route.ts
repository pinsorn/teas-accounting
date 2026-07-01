import { cookies } from 'next/headers';
import { NextRequest, NextResponse } from 'next/server';

/**
 * Passthrough for the OAuth authorize endpoint (OpenIddict on the backend), with the
 * critical auth seam: the TEAS session is the httpOnly `access_token` cookie on THIS origin,
 * but the backend authenticates the browser via `Authorization: Bearer` (it does NOT read
 * the cookie). So this route reads the cookie and forwards it as a Bearer token, then RELAYS
 * the backend's 302 (redirect:'manual' — copy the `location`, do NOT follow it):
 *   - not authenticated → backend 302 → {App:BaseUrl}/login?returnTo=<enc /oauth/consent?...>
 *   - authenticated     → backend 302 → {App:BaseUrl}/oauth/consent?<authorize query>
 * OpenIddict has already validated client_id + exact redirect_uri + PKCE S256 + resource.
 * middleware.ts lists '/oauth/authorize' as PUBLIC (a logged-out client must reach it; the
 * missing cookie is exactly what makes the backend send the browser to /login).
 */
export const runtime = 'nodejs';
export const dynamic = 'force-dynamic';

const BACKEND = process.env.BACKEND_API_URL ?? 'http://localhost:5080';

export async function GET(req: NextRequest) {
  const store = await cookies();
  const token = store.get('access_token')?.value;

  const headers = new Headers();
  headers.set('accept', req.headers.get('accept') ?? 'text/html, application/json');
  // Cookie → Bearer: without this the backend always sees the caller as unauthenticated.
  if (token) headers.set('Authorization', `Bearer ${token}`);

  const search = req.nextUrl.search; // includes leading "?" (the authorize query) or ""

  let upstream: Response;
  try {
    upstream = await fetch(`${BACKEND}/oauth/authorize${search}`, {
      method: 'GET',
      headers,
      cache: 'no-store',
      redirect: 'manual', // relay the backend 302 to /login or /oauth/consent, do NOT follow
    });
  } catch (e) {
    console.error('[/oauth/authorize] upstream fetch failed:', e);
    return NextResponse.json(
      { error: { code: 'gateway.error', message: 'Connection to OAuth backend failed.' } },
      { status: 502 },
    );
  }

  const respHeaders = new Headers();
  for (const h of ['content-type', 'cache-control', 'www-authenticate', 'location']) {
    const v = upstream.headers.get(h);
    if (v) respHeaders.set(h, v);
  }
  return new NextResponse(upstream.body, { status: upstream.status, headers: respHeaders });
}
