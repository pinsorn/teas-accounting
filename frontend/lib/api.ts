import { ApiError } from './api-client';

/**
 * Authenticated client. Every call goes through the same-origin BFF proxy
 * (/api/proxy/<backend-path>), which injects the Bearer token from the httpOnly
 * cookie server-side. No JWT ever touches client JS.
 */
const PROXY = '/api/proxy';

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const res = await fetch(`${PROXY}/${path}`, {
    ...init,
    headers: { 'Content-Type': 'application/json', ...(init.headers ?? {}) },
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

export const apiGet = <T>(path: string) => request<T>(path, { method: 'GET' });
export const apiPost = <T>(path: string, payload?: unknown) =>
  request<T>(path, { method: 'POST', body: payload ? JSON.stringify(payload) : undefined });
export const apiPut = <T>(path: string, payload?: unknown) =>
  request<T>(path, { method: 'PUT', body: payload ? JSON.stringify(payload) : undefined });
export const apiDelete = <T>(path: string) => request<T>(path, { method: 'DELETE' });

/** Build a query string from a filter object, skipping null/undefined/''. */
export function qs(params: Record<string, string | number | boolean | null | undefined>): string {
  const sp = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) {
    if (v !== null && v !== undefined && v !== '') sp.set(k, String(v));
  }
  const s = sp.toString();
  return s ? `?${s}` : '';
}

/** A binary download (PDF/XML) via the proxy — returns a blob URL the caller revokes. */
export async function downloadFile(path: string, filename: string) {
  const res = await fetch(`${PROXY}/${path}`);
  if (!res.ok) throw new ApiError(res.status, 'download_failed', res.statusText);
  const blob = await res.blob();
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(url);
}
