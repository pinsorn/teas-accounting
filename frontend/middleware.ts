import { NextResponse, type NextRequest } from 'next/server';

// /onboarding is public so a FRESH install (zero users — nobody can log in yet) can reach the
// create-super-admin step. It is safe: the super-admin create calls the zero-users-gated
// POST /system/setup/bootstrap-admin (refuses 409 once any user exists), and the create-company
// step's BFF route still 401s without a session cookie. The page self-routes authenticated users.
const PUBLIC_PATHS = ['/login', '/onboarding', '/api', '/_next', '/favicon.ico'];

export function middleware(request: NextRequest) {
  const { pathname } = request.nextUrl;
  if (PUBLIC_PATHS.some((p) => pathname.startsWith(p))) {
    return NextResponse.next();
  }

  // Backend sets the access_token cookie on /auth/login success. Absence ⇒ redirect to /login.
  const token = request.cookies.get('access_token')?.value;
  if (!token) {
    const url = request.nextUrl.clone();
    url.pathname = '/login';
    return NextResponse.redirect(url);
  }

  return NextResponse.next();
}

export const config = {
  // Skip Next internals + any /public static asset (logo, mascot, images) so the
  // auth gate doesn't 307-redirect them to /login (which broke next/image).
  matcher: ['/((?!_next/static|_next/image|favicon.ico|.*\\.(?:png|jpg|jpeg|svg|gif|webp|ico|woff2?)$).*)'],
};
