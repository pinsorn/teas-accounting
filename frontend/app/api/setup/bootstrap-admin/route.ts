import { NextResponse } from 'next/server';

/**
 * BFF first-run bootstrap (first-run-bootstrap spec 2026-06-17). ANONYMOUS by necessity: on a fresh
 * install there is no user to authenticate as, so this forwards to the backend's zero-users-gated
 * POST /system/setup/bootstrap-admin (which 409s the instant any user exists), then — on success —
 * immediately logs the new super-admin in and stores the JWT in the httpOnly access_token cookie
 * (mirrors app/api/auth/login/route.ts EXACTLY so the JWT never reaches client JS — CLAUDE.md §10).
 *
 * The resulting session is a companyId=0 super-admin (no role yet), so the onboarding company step runs.
 */
const BACKEND = process.env.BACKEND_API_URL ?? 'http://localhost:5000';

export async function POST(request: Request) {
  try {
    const body = await request.json().catch(() => null);
    const username = typeof body?.username === 'string' ? body.username.trim() : '';
    const password = typeof body?.password === 'string' ? body.password : '';
    if (!username || !password) {
      return NextResponse.json(
        { title: 'auth.bad_request', detail: 'username and password are required' },
        { status: 400 },
      );
    }

    // 1) Create the first super-admin (anonymous; gated on zero users server-side).
    const created = await fetch(`${BACKEND}/system/setup/bootstrap-admin`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        username,
        password,
        email: body?.email?.trim?.() || null,
        fullName: body?.fullName?.trim?.() || null,
      }),
      cache: 'no-store',
    });
    if (!created.ok) {
      const cb = await created.json().catch(() => null);
      return NextResponse.json(cb ?? { title: `http_${created.status}` }, { status: created.status });
    }

    // 2) Log the new admin in so the company step runs as the companyId=0 super-admin.
    const login = await fetch(`${BACKEND}/auth/login`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password, mfaCode: null }),
      cache: 'no-store',
    });
    const lb = await login.json().catch(() => null);
    if (!login.ok) {
      return NextResponse.json(lb ?? { title: `http_${login.status}` }, { status: login.status });
    }
    const token: string | undefined = lb?.access_token;
    if (!token) {
      return NextResponse.json(
        { title: 'auth.no_token', detail: 'Login after bootstrap did not return an access_token' },
        { status: 502 },
      );
    }

    const res = NextResponse.json({ ok: true });
    const expires = lb?.expires_at ? new Date(lb.expires_at) : undefined;
    res.cookies.set('access_token', token, {
      httpOnly: true,
      secure: process.env.NODE_ENV === 'production',
      sameSite: 'lax',
      path: '/',
      ...(expires && !Number.isNaN(expires.getTime()) ? { expires } : {}),
    });
    return res;
  } catch (e) {
    console.error('[/api/setup/bootstrap-admin] handler threw:', e);
    const detail = e instanceof Error ? `${e.name}: ${e.message}` : String(e);
    return NextResponse.json({ title: 'auth.handler_error', detail }, { status: 500 });
  }
}
