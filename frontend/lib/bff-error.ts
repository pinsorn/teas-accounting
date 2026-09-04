import { NextResponse } from 'next/server';

// WP-D (GPT-5.6 review MEDIUM-02, 2026-09-04) — the 4 BFF auth/onboarding routes
// (refresh, switch-company, onboarding, bootstrap-admin) leaked `${e.name}: ${e.message}`
// into the 500 response body. Mirrors app/api/auth/login/route.ts's safe pattern (log
// server-side, generic detail to the client) plus a traceId so support can correlate a
// reported error with the server log without re-exposing the exception text.
export function bffInternalError(tag: string, e: unknown) {
  const traceId = crypto.randomUUID();
  console.error(`[${tag}] ${traceId}`, e);           // server-side only; never log request bodies/tokens
  return NextResponse.json({ title: 'auth.handler_error', detail: 'Internal error', traceId }, { status: 500 });
}
