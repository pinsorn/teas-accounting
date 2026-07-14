import { cookies } from 'next/headers';
import { NextResponse } from 'next/server';

/**
 * WP2.1 (F16, D6 sliding re-issue) — BFF refresh route. Mirrors
 * app/api/auth/switch-company/route.ts EXACTLY (read cookie → Bearer to backend → re-set
 * cookie from the new token), just re-targeting the caller's OWN current company instead of
 * switching. The JWT never reaches client JS (CLAUDE.md §10); `expires_at` alone is returned
 * in the body so useSessionKeepAlive can schedule its next tick — it is a plain timestamp,
 * not the token, so returning it is not a §10 leak.
 */
const BACKEND = process.env.BACKEND_API_URL ?? 'http://localhost:5000';

export async function POST() {
  try {
    const store = await cookies();
    const token = store.get('access_token')?.value;
    if (!token) {
      return NextResponse.json(
        { title: 'auth.unauthenticated', detail: 'No session.' },
        { status: 401 },
      );
    }

    const upstream = await fetch(`${BACKEND}/auth/refresh`, {
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

    const res = NextResponse.json({ ok: true, expires_at: body?.expires_at });
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
    console.error('[/api/auth/refresh] handler threw:', e);
    const detail = e instanceof Error ? `${e.name}: ${e.message}` : String(e);
    return NextResponse.json({ title: 'auth.handler_error', detail }, { status: 500 });
  }
}
