/**
 * Thin fetch wrapper for the TEAS backend. The session JWT is in an httpOnly cookie on the
 * Next.js origin (set by app/api/auth/login). Because that cookie is first-party to the
 * frontend (not the backend origin), authenticated backend calls must go through a
 * same-origin BFF proxy route that reads the cookie and adds `Authorization: Bearer`.
 * TODO(plan.md): add a generic /api/proxy/[...path] handler; until then this wrapper is
 * only for unauthenticated/public endpoints.
 * Throws ApiError on non-2xx with the parsed ProblemDetails body when available.
 */

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    public readonly code: string,
    message: string,
    public readonly details?: unknown,
  ) {
    super(message);
  }
}

const baseUrl = process.env.NEXT_PUBLIC_API_URL ?? 'http://localhost:5000';

export async function api<T = unknown>(
  path: string,
  init: RequestInit = {},
): Promise<T> {
  const res = await fetch(`${baseUrl}${path}`, {
    credentials: 'include',
    headers: {
      'Content-Type': 'application/json',
      ...(init.headers ?? {}),
    },
    ...init,
  });

  if (res.status === 204) return undefined as T;

  const body = await res.json().catch(() => null);

  if (!res.ok) {
    const code = body?.title ?? `http_${res.status}`;
    const detail = body?.detail ?? body?.message ?? res.statusText;
    throw new ApiError(res.status, code, detail, body);
  }

  return body as T;
}
