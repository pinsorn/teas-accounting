import { NextRequest, NextResponse } from 'next/server';

/**
 * Anonymous passthrough for RFC 9728 OAuth protected-resource metadata (TEAS Connect).
 *
 * WHY: Cloudflare fronts ONLY the Next.js app; the .NET backend has no public ingress.
 * MCP clients discover the AS via this well-known doc, so it must be reachable on the
 * app origin. Modeled on app/mcp/route.ts (runtime nodejs, dynamic, stream body, copy
 * www-authenticate/location) — minus any credential: this endpoint is PUBLIC (no cookie,
 * no X-Api-Key). middleware.ts lists '/.well-known' as PUBLIC.
 */
export const runtime = 'nodejs';
export const dynamic = 'force-dynamic';

const BACKEND = process.env.BACKEND_API_URL ?? 'http://localhost:5080';

export async function GET(req: NextRequest) {
  let upstream: Response;
  try {
    upstream = await fetch(`${BACKEND}/.well-known/oauth-protected-resource`, {
      method: 'GET',
      headers: { accept: req.headers.get('accept') ?? 'application/json' },
      cache: 'no-store',
      redirect: 'manual',
    });
  } catch (e) {
    console.error('[/.well-known/oauth-protected-resource] upstream fetch failed:', e);
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
