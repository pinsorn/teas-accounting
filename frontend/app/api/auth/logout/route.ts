import { NextResponse } from 'next/server';

/** Clears the httpOnly session cookie. middleware.ts will then bounce to /login. */
export async function POST() {
  const res = NextResponse.json({ ok: true });
  res.cookies.set('access_token', '', {
    httpOnly: true,
    secure: process.env.NODE_ENV === 'production',
    sameSite: 'lax',
    path: '/',
    maxAge: 0,
  });
  return res;
}
