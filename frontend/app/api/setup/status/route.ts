import { NextResponse } from 'next/server';

/**
 * BFF first-run probe. ANONYMOUS: forwards to the backend's zero-users-gated
 * GET /system/setup/status so the login page can decide whether a visitor with no session
 * should be routed to the /onboarding wizard (fresh install, no user yet) or to /login.
 * Returns only `{ needs_setup: boolean }` — no data, no user info.
 */
const BACKEND = process.env.BACKEND_API_URL ?? 'http://localhost:5000';

export async function GET() {
  try {
    const res = await fetch(`${BACKEND}/system/setup/status`, { cache: 'no-store' });
    const body = await res.json().catch(() => null);
    if (!res.ok) {
      // On a backend error, fail SAFE: assume the system is already set up so we never trap a
      // returning user in the onboarding wizard. Worst case they see /login (recoverable).
      return NextResponse.json({ needs_setup: false }, { status: 200 });
    }
    return NextResponse.json({ needs_setup: body?.needs_setup === true });
  } catch {
    return NextResponse.json({ needs_setup: false }, { status: 200 });
  }
}
