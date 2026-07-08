import { NextRequest, NextResponse } from 'next/server';

/**
 * Anonymous passthrough for the token-authenticated public PDF endpoint (spec
 * mcp-expansion.md §A). Prod topology: nginx-proxy-manager forwards the ENTIRE
 * teas.kazaki-rio.com domain to this Next.js app; the .NET backend has no public
 * ingress of its own. MCP tools mint browser-openable links of the shape
 * `{BaseUrl}/public/pdf?t=<token>` (see TeasMcpTools.PublicPdfUrl), so this route
 * must exist at the same path or those links 307 to /login via the session-cookie
 * gate before ever reaching the backend. Same pattern as app/mcp/route.ts and
 * app/.well-known/jwks/route.ts, minus any credential header: the `t` token IS
 * the auth (PublicPdfTenantMiddleware validates it backend-side; a bad token
 * yields 404, never 401/403 — see PublicPdfEndpoints.cs). middleware.ts must list
 * '/public/pdf' as PUBLIC so this passthrough is not itself redirected to /login.
 */
export const runtime = 'nodejs';
export const dynamic = 'force-dynamic';

const BACKEND = process.env.BACKEND_API_URL ?? 'http://localhost:5080';

export async function GET(req: NextRequest) {
  // Forward the `t` token verbatim; no other query params are meaningful to the
  // backend route and no auth header is sent (anonymous — the token is the auth).
  const t = req.nextUrl.searchParams.get('t');
  const qs = t ? `?t=${encodeURIComponent(t)}` : '';

  let upstream: Response;
  try {
    upstream = await fetch(`${BACKEND}/public/pdf${qs}`, {
      method: 'GET',
      headers: { accept: 'application/pdf' },
      cache: 'no-store',
      redirect: 'manual',
    });
  } catch (e) {
    console.error('[/public/pdf] upstream fetch failed:', e);
    return NextResponse.json(
      { error: { code: 'gateway.error', message: 'Connection to PDF backend failed.' } },
      { status: 502 },
    );
  }

  // Pass status + the streaming body through untouched. content-disposition carries
  // the inline-render filename the endpoint sets; content-type is application/pdf on
  // success. No cache-control is forwarded/added — these links are token-scoped and
  // must never be cached by an intermediate.
  const respHeaders = new Headers();
  for (const h of ['content-type', 'content-disposition']) {
    const v = upstream.headers.get(h);
    if (v) respHeaders.set(h, v);
  }
  respHeaders.set('cache-control', 'no-store');

  return new NextResponse(upstream.body, { status: upstream.status, headers: respHeaders });
}
