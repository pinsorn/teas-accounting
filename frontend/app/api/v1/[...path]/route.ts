import { NextRequest, NextResponse } from 'next/server';

/**
 * Public passthrough for MCP PDF downloads, authenticated by X-Api-Key (NOT the session
 * cookie). The .NET backend has no public ingress (Cloudflare fronts the Next.js app), yet
 * the MCP get_*_pdf_url tools hand the agent a `${BaseUrl}/api/v1/<doc>/<id>/pdf` link to
 * fetch. This forwards GET to `${BACKEND_API_URL}/api/v1/...` carrying X-Api-Key and streams
 * the PDF body back unbuffered.
 *
 * TIGHTLY SCOPED: GET only, and ONLY `.../pdf` routes (see the scope lock below) — NOT the
 * rest of the external read API (which includes PII list endpoints). The backend's
 * ApiKeyOnly policy + per-tool scopes + per-key rate-limit + RLS still gate every call.
 * Opening the full external /api/v1 surface (reads or writes) is a separate human decision.
 */
export const runtime = 'nodejs';
export const dynamic = 'force-dynamic';

const BACKEND = process.env.BACKEND_API_URL ?? 'http://localhost:5080';

export async function GET(req: NextRequest, { params }: { params: Promise<{ path: string[] }> }) {
  const apiKey = req.headers.get('x-api-key');
  if (!apiKey) {
    return NextResponse.json(
      { error: { code: 'auth.missing_api_key', message: 'X-Api-Key header is required.' } },
      { status: 401 },
    );
  }

  const { path } = await params;
  // Reject dot segments — encodeURIComponent leaves '.'/'..' intact, so guard against
  // path traversal above /api/v1 on the backend (defense-in-depth; the backend authorizes too).
  if (path.some((seg) => seg === '.' || seg === '..')) {
    return NextResponse.json(
      { error: { code: 'bad_request', message: 'Invalid path.' } },
      { status: 400 },
    );
  }
  // SCOPE LOCK: only PDF-download routes (`/api/v1/<doc>/<id>/pdf`) are exposed — the exact
  // surface the MCP get_*_pdf_url tools need. This deliberately does NOT open the rest of the
  // external read API (e.g. GET /api/v1/tax-invoices returns customer names + tax IDs = PII);
  // publicly exposing the full external surface is a separate decision for a human to make.
  if (path[path.length - 1] !== 'pdf') {
    return NextResponse.json(
      { error: { code: 'not_found', message: 'Not found.' } },
      { status: 404 },
    );
  }
  const target = `${BACKEND}/api/v1/${path.map(encodeURIComponent).join('/')}${req.nextUrl.search}`;

  const headers = new Headers();
  headers.set('X-Api-Key', apiKey);
  headers.set('accept', req.headers.get('accept') ?? 'application/json');

  let upstream: Response;
  try {
    upstream = await fetch(target, { method: 'GET', headers, cache: 'no-store', redirect: 'manual' });
  } catch (e) {
    console.error('[/api/v1] upstream fetch failed:', e);
    return NextResponse.json(
      { error: { code: 'gateway.error', message: 'Connection to backend failed.' } },
      { status: 502 },
    );
  }

  // Pass status + the streamed body + the headers that matter for JSON and PDF downloads.
  const respHeaders = new Headers();
  for (const h of ['content-type', 'content-disposition', 'cache-control', 'location']) {
    const v = upstream.headers.get(h);
    if (v) respHeaders.set(h, v);
  }
  return new NextResponse(upstream.body, { status: upstream.status, headers: respHeaders });
}
