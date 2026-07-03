import { NextRequest, NextResponse } from 'next/server';

/**
 * Anonymous passthrough for the OpenIddict JWKS document. Discovery advertises
 * `jwks_uri = {publicOrigin}/.well-known/jwks` (see the oauth-authorization-server route's
 * host rewrite), so the key set must be publicly reachable at that path too. Public keys
 * only — safe to expose; middleware already lists '/.well-known' as public.
 */
export const runtime = 'nodejs';
export const dynamic = 'force-dynamic';

const BACKEND = process.env.BACKEND_API_URL ?? 'http://localhost:5080';

export async function GET(req: NextRequest) {
  let upstream: Response;
  try {
    upstream = await fetch(`${BACKEND}/.well-known/jwks`, {
      method: 'GET',
      headers: { accept: req.headers.get('accept') ?? 'application/json' },
      cache: 'no-store',
      redirect: 'manual',
    });
  } catch (e) {
    console.error('[/.well-known/jwks] upstream fetch failed:', e);
    return NextResponse.json(
      { error: { code: 'gateway.error', message: 'Connection to OAuth backend failed.' } },
      { status: 502 },
    );
  }

  const respHeaders = new Headers();
  for (const h of ['content-type', 'cache-control']) {
    const v = upstream.headers.get(h);
    if (v) respHeaders.set(h, v);
  }
  return new NextResponse(upstream.body, { status: upstream.status, headers: respHeaders });
}
