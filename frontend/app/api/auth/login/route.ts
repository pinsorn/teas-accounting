import { NextResponse } from 'next/server';

/**
 * BFF login proxy. The browser never sees the JWT: this server route forwards
 * credentials to the .NET backend and, on success, stores the access_token in an
 * httpOnly cookie on the Next.js origin (so middleware.ts can gate routes and the
 * token is never exposed to client JS / localStorage — CLAUDE.md §10).
 */
const BACKEND = process.env.BACKEND_API_URL ?? 'http://localhost:5000';

export async function POST(request: Request) {
  try {
  const creds = await request.json().catch(() => null);
  if (!creds?.username || !creds?.password) {
    return NextResponse.json(
      { title: 'auth.bad_request', detail: 'username and password are required' },
      { status: 400 },
    );
  }

  const upstream = await fetch(`${BACKEND}/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      username: creds.username,
      password: creds.password,
      mfaCode: creds.mfaCode ?? null,
    }),
    cache: 'no-store',
  });

  const body = await upstream.json().catch(() => null);

  if (!upstream.ok) {
    return NextResponse.json(body ?? { title: `http_${upstream.status}` }, {
      status: upstream.status,
    });
  }

  // Step 1 of a 2-step MFA login — no token yet, just relay the flag.
  if (body?.mfa_required) {
    return NextResponse.json({ mfa_required: true });
  }

  const token: string | undefined = body?.access_token;
  if (!token) {
    return NextResponse.json(
      { title: 'auth.no_token', detail: 'Backend did not return an access_token' },
      { status: 502 },
    );
  }

  const res = NextResponse.json({ ok: true });
  const expires = body?.expires_at ? new Date(body.expires_at) : undefined;
  res.cookies.set('access_token', token, {
    httpOnly: true,
    secure: process.env.NODE_ENV === 'production',
    sameSite: 'lax',
    path: '/',
    ...(expires && !Number.isNaN(expires.getTime()) ? { expires } : {}),
  });
  return res;
  } catch (e) {
    // Sprint 13h ckpt4 — surface server-side errors so the FE 500 isn't an
    // opaque "Internal Server Error" body. Logged to fe3.log too.
    console.error('[/api/auth/login] handler threw:', e);
    // Never leak the stack/message to the browser — log server-side, return a generic detail.
    return NextResponse.json(
      { title: 'auth.handler_error', detail: 'Internal error' },
      { status: 500 },
    );
  }
}
