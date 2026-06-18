import { ApiError } from './api';

/**
 * Auth talks to same-origin BFF routes (app/api/auth/*), NOT the backend directly.
 * The JWT lives only in an httpOnly cookie set by the route handler — never in JS.
 */
export type LoginResponse =
  | { mfa_required: true }
  | { ok: true };

async function postSameOrigin<T>(path: string, payload: unknown): Promise<T> {
  const res = await fetch(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  });
  const body = await res.json().catch(() => null);
  if (!res.ok) {
    const code = body?.title ?? `http_${res.status}`;
    const detail = body?.detail ?? body?.message ?? res.statusText;
    throw new ApiError(res.status, code, detail, body);
  }
  return body as T;
}

export const auth = {
  login: (username: string, password: string, mfaCode?: string) =>
    postSameOrigin<LoginResponse>('/api/auth/login', { username, password, mfaCode }),

  logout: () => postSameOrigin<{ ok: true }>('/api/auth/logout', {}),
};
