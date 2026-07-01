import { NextRequest, NextResponse } from 'next/server';

/**
 * Public single-origin passthrough for the in-process .NET MCP server (TEAS Connect).
 *
 * WHY: in production Cloudflare fronts ONLY the Next.js app; the .NET backend has no
 * public ingress (it is reached only via the cookie-auth BFF at /api/proxy). The MCP
 * server is mounted at `${BACKEND_API_URL}/mcp` and authenticates with the `X-Api-Key`
 * scheme — NOT the session cookie. So this route forwards /mcp straight through to the
 * backend, carrying the caller's X-Api-Key, and streams the response (MCP Streamable
 * HTTP returns `text/event-stream`) back unbuffered. This is the same pattern as
 * app/api/proxy/[...path]/route.ts, minus the cookie and JWT (the X-Api-Key is the auth).
 *
 * Security: no cookie is read or forwarded; the backend's ApiKeyOnly policy + per-tool
 * [Authorize] scopes + per-key rate-limit still gate every call. A missing/invalid key
 * → backend 401, passed straight back. middleware.ts must list '/mcp' as PUBLIC so the
 * session-cookie gate does not 307 these requests to /login.
 */
export const runtime = 'nodejs';
export const dynamic = 'force-dynamic';

const BACKEND = process.env.BACKEND_API_URL ?? 'http://localhost:5080';

async function forward(req: NextRequest) {
  const apiKey = req.headers.get('x-api-key');
  if (!apiKey) {
    return NextResponse.json(
      { error: { code: 'auth.missing_api_key', message: 'X-Api-Key header is required.' } },
      { status: 401 },
    );
  }

  const headers = new Headers();
  headers.set('X-Api-Key', apiKey);
  // Streamable HTTP requires the client to accept both JSON and the SSE stream.
  headers.set('accept', req.headers.get('accept') ?? 'application/json, text/event-stream');
  const ct = req.headers.get('content-type');
  if (ct) headers.set('content-type', ct);
  // Forward the MCP session/protocol negotiation headers when present (stateless mode
  // usually omits them, but a client may still send them — pass through verbatim).
  const session = req.headers.get('mcp-session-id');
  if (session) headers.set('mcp-session-id', session);
  const proto = req.headers.get('mcp-protocol-version');
  if (proto) headers.set('mcp-protocol-version', proto);

  const hasBody = req.method !== 'GET' && req.method !== 'HEAD';

  let upstream: Response;
  try {
    upstream = await fetch(`${BACKEND}/mcp`, {
      method: req.method,
      headers,
      body: hasBody ? await req.arrayBuffer() : undefined,
      cache: 'no-store',
      redirect: 'manual',
    });
  } catch (e) {
    console.error('[/mcp] upstream fetch failed:', e);
    return NextResponse.json(
      { error: { code: 'gateway.error', message: 'Connection to MCP backend failed.' } },
      { status: 502 },
    );
  }

  // Pass status + the streaming body + the headers that keep SSE flowing unbuffered.
  const respHeaders = new Headers();
  for (const h of ['content-type', 'cache-control', 'x-accel-buffering', 'mcp-session-id', 'www-authenticate', 'location']) {
    const v = upstream.headers.get(h);
    if (v) respHeaders.set(h, v);
  }

  return new NextResponse(upstream.body, { status: upstream.status, headers: respHeaders });
}

export const GET = forward;
export const POST = forward;
export const DELETE = forward;
