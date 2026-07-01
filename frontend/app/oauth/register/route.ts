import { NextRequest, NextResponse } from 'next/server';

/**
 * Anonymous passthrough for OAuth Dynamic Client Registration (RFC 7591).
 *
 * DCR is deferred on the backend — the endpoint may 404 for now; that is fine, this route
 * just forwards so clients that probe it get the backend's real answer. CLIENT/public call,
 * NOT session-authenticated → NO cookie is read or forwarded; body streamed as-is. Modeled
 * on app/mcp/route.ts. middleware.ts lists '/oauth/register' as PUBLIC.
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
    upstream = await fetch(`${BACKEND}/oauth/register`, {
      method: 'POST',
      headers,
      body: await req.arrayBuffer(),
      cache: 'no-store',
      redirect: 'manual',
    });
  } catch (e) {
    console.error('[/oauth/register] upstream fetch failed:', e);
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
