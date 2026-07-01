import { NextRequest, NextResponse } from 'next/server';

/**
 * Anonymous passthrough for the OAuth token endpoint (OpenIddict on the backend).
 *
 * The token exchange is CLIENT-authenticated (form-encoded: code + PKCE verifier, or a
 * refresh token) — NOT session-authenticated. So NO cookie is read or forwarded; the body
 * is streamed through as-is and the content-type preserved. Modeled on app/mcp/route.ts.
 * middleware.ts lists '/oauth/token' as PUBLIC.
 */
export const runtime = 'nodejs';
export const dynamic = 'force-dynamic';

const BACKEND = process.env.BACKEND_API_URL ?? 'http://localhost:5080';

export async function POST(req: NextRequest) {
  const headers = new Headers();
  const ct = req.headers.get('content-type');
  if (ct) headers.set('content-type', ct);
  headers.set('accept', req.headers.get('accept') ?? 'application/json');

  let upstream: Response;
  try {
    upstream = await fetch(`${BACKEND}/oauth/token`, {
      method: 'POST',
      headers,
      body: await req.arrayBuffer(),
      cache: 'no-store',
      redirect: 'manual',
    });
  } catch (e) {
    console.error('[/oauth/token] upstream fetch failed:', e);
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
