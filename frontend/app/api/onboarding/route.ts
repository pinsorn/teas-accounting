import { cookies } from 'next/headers';
import { NextResponse } from 'next/server';

/**
 * BFF onboarding route (first-company setup, onboarding-switcher spec 2026-06-16).
 *
 * Why a dedicated route and not the generic /api/proxy: POST /companies returns 201 with an
 * EMPTY body — the new id is only in the `Location` header, which the generic proxy strips.
 * We need that id to immediately switch the super-admin's JWT to the freshly-created company.
 *
 * Flow (all server-side, single client hop): read access_token cookie → POST /companies with
 * Bearer → read new id from `Location: /companies/{id}` → POST /auth/switch-company/{id} →
 * overwrite the httpOnly access_token cookie with the company-scoped token (cookie options mirror
 * app/api/auth/login/route.ts EXACTLY). Backend enforces Master.CompanyManage + super-admin.
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

    const company = await request.json().catch(() => null);
    if (!company || typeof company !== 'object') {
      return NextResponse.json(
        { title: 'auth.bad_request', detail: 'company body is required' },
        { status: 400 },
      );
    }

    // 1) Create the company.
    const created = await fetch(`${BACKEND}/companies`, {
      method: 'POST',
      headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
      body: JSON.stringify(company),
      cache: 'no-store',
    });

    if (!created.ok) {
      const body = await created.json().catch(() => null);
      return NextResponse.json(body ?? { title: `http_${created.status}` }, {
        status: created.status,
      });
    }

    // POST /companies → 201 Created, empty body, id only in the Location header.
    const location = created.headers.get('location') ?? '';
    const newId = Number(location.split('/').filter(Boolean).pop());
    if (!Number.isInteger(newId) || newId <= 0) {
      return NextResponse.json(
        { title: 'onboarding.no_id', detail: 'Company created but its id could not be resolved.' },
        { status: 502 },
      );
    }

    // 2) Switch the super-admin's JWT to the new company so the dashboard is scoped to it.
    const switched = await fetch(`${BACKEND}/auth/switch-company/${newId}`, {
      method: 'POST',
      headers: { Authorization: `Bearer ${token}` },
      cache: 'no-store',
    });

    const sBody = await switched.json().catch(() => null);
    if (!switched.ok) {
      return NextResponse.json(sBody ?? { title: `http_${switched.status}` }, {
        status: switched.status,
      });
    }

    const newToken: string | undefined = sBody?.access_token;
    if (!newToken) {
      return NextResponse.json(
        { title: 'auth.no_token', detail: 'Switch did not return an access_token' },
        { status: 502 },
      );
    }

    const res = NextResponse.json({ ok: true, companyId: newId });
    const expires = sBody?.expires_at ? new Date(sBody.expires_at) : undefined;
    res.cookies.set('access_token', newToken, {
      httpOnly: true,
      secure: process.env.NODE_ENV === 'production',
      sameSite: 'lax',
      path: '/',
      ...(expires && !Number.isNaN(expires.getTime()) ? { expires } : {}),
    });
    return res;
  } catch (e) {
    console.error('[/api/onboarding] handler threw:', e);
    const detail = e instanceof Error ? `${e.name}: ${e.message}` : String(e);
    return NextResponse.json({ title: 'auth.handler_error', detail }, { status: 500 });
  }
}
