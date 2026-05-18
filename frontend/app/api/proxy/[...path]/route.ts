import { cookies } from 'next/headers';
import { NextRequest, NextResponse } from 'next/server';

/**
 * Authenticated BFF proxy. The browser calls /api/proxy/<backend-path>; this handler
 * reads the httpOnly access_token cookie and forwards the request to the .NET backend
 * with `Authorization: Bearer`. The JWT never reaches client JS (CLAUDE.md §10).
 * Binary responses (PDF/XML) are passed through untouched for downloads.
 */
const BACKEND = process.env.BACKEND_API_URL ?? 'http://localhost:5000';

async function forward(req: NextRequest, pathParts: string[]) {
  const store = await cookies();
  const token = store.get('access_token')?.value;
  if (!token) {
    return NextResponse.json(
      { title: 'auth.unauthenticated', detail: 'No session.' },
      { status: 401 },
    );
  }

  const search = req.nextUrl.search; // includes leading "?" or ""
  const target = `${BACKEND}/${pathParts.map(encodeURIComponent).join('/')}${search}`;

  const headers = new Headers();
  headers.set('Authorization', `Bearer ${token}`);
  const ct = req.headers.get('content-type');
  if (ct) headers.set('content-type', ct);
  headers.set('accept', req.headers.get('accept') ?? 'application/json');

  const hasBody = req.method !== 'GET' && req.method !== 'HEAD';
  const upstream = await fetch(target, {
    method: req.method,
    headers,
    body: hasBody ? await req.arrayBuffer() : undefined,
    cache: 'no-store',
    redirect: 'manual',
  });

  // Pass through status + content-type + body (works for JSON and binary downloads).
  const respHeaders = new Headers();
  const upCt = upstream.headers.get('content-type');
  if (upCt) respHeaders.set('content-type', upCt);
  const cd = upstream.headers.get('content-disposition');
  if (cd) respHeaders.set('content-disposition', cd);

  return new NextResponse(upstream.body, {
    status: upstream.status,
    headers: respHeaders,
  });
}

type Ctx = { params: Promise<{ path: string[] }> };

export async function GET(req: NextRequest, { params }: Ctx) {
  return forward(req, (await params).path);
}
export async function POST(req: NextRequest, { params }: Ctx) {
  return forward(req, (await params).path);
}
export async function PUT(req: NextRequest, { params }: Ctx) {
  return forward(req, (await params).path);
}
export async function DELETE(req: NextRequest, { params }: Ctx) {
  return forward(req, (await params).path);
}
