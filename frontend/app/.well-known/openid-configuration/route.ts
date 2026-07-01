import { NextRequest, NextResponse } from 'next/server';

/**
 * Anonymous passthrough for the OpenID Connect discovery document (served by OpenIddict
 * on the backend). Some MCP clients probe openid-configuration instead of the RFC 8414
 * doc. PUBLIC — no cookie, no X-Api-Key. middleware lists '/.well-known'.
 */
export const runtime = 'nodejs';
export const dynamic = 'force-dynamic';

const BACKEND = process.env.BACKEND_API_URL ?? 'http://localhost:5080';

export async function GET(req: NextRequest) {
  let upstream: Response;
  try {
    upstream = await fetch(`${BACKEND}/.well-known/openid-configuration`, {
      method: 'GET',
      headers: { accept: req.headers.get('accept') ?? 'application/json' },
      cache: 'no-store',
      redirect: 'manual',
    });
  } catch (e) {
    console.error('[/.well-known/openid-configuration] upstream fetch failed:', e);
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
