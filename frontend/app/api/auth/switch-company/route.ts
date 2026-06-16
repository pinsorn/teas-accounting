import { cookies } from 'next/headers';
import { NextResponse } from 'next/server';

/**
 * BFF company-switch route (super-admin company switcher, onboarding-switcher spec 2026-06-16).
 *
 * The generic /api/proxy route forwards the JWT but does NOT set cookies, so switching company
 * (which re-issues a JWT carrying the new company_id — RLS is bound to the token, not a header)
 * needs a dedicated route that overwrites the httpOnly access_token cookie with the new token.
 *
 * Flow: read current access_token cookie → call backend POST /auth/switch-company/{id} with
 * Bearer → on success overwrite the cookie with the returned token (cookie options mirror
 * app/api/auth/login/route.ts EXACTLY: httpOnly, secure-in-prod, sameSite=lax, path=/, expires).
 * The token never reaches client JS (CLAUDE.md §10). Backend enforces super-admin (403 otherwise).
 */
const BACKEND = process.env.BACKEND_API_URL ?? 'http://localhost:5000';

export async function POST(request: Request) {
  try {
    const store = await cookies();
    const token = store.get('access_token')?.value;
    if (!token) {
      return NextResponse.json(
        { title: 'auth.unauthenticated', detail: 'No session.' },
        { status: 401 },
      );
    }

    const payload = await request.json().catch(() => null);
    const companyId = Number(payload?.companyId);
    if (!Number.isInteger(companyId) || companyId <= 0) {
      return NextResponse.json(
        { title: 'auth.bad_request', detail: 'companyId (positive integer) is required' },
        { status: 400 },
      );
    }

    const upstream = await fetch(`${BACKEND}/auth/switch-company/${companyId}`, {
      method: 'POST',
      headers: { Authorization: `Bearer ${token}` },
      cache: 'no-store',
    });

    const body = await upstream.json().catch(() => null);
    if (!upstream.ok) {
      return NextResponse.json(body ?? { title: `http_${upstream.status}` }, {
        status: upstream.status,
      });
    }

    const newToken: string | undefined = body?.access_token;
    if (!newToken) {
      return NextResponse.json(
        { title: 'auth.no_token', detail: 'Backend did not return an access_token' },
        { status: 502 },
      );
    }

    const res = NextResponse.json({ ok: true });
    const expires = body?.expires_at ? new Date(body.expires_at) : undefined;
    res.cookies.set('access_token', newToken, {
      httpOnly: true,
      secure: process.env.NODE_ENV === 'production',
      sameSite: 'lax',
      path: '/',
      ...(expires && !Number.isNaN(expires.getTime()) ? { expires } : {}),
    });
    return res;
  } catch (e) {
    console.error('[/api/auth/switch-company] handler threw:', e);
    const detail = e instanceof Error ? `${e.name}: ${e.message}` : String(e);
    return NextResponse.json({ title: 'auth.handler_error', detail }, { status: 500 });
  }
}
