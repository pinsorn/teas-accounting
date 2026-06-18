import { toast } from 'sonner';

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

/**
 * Authenticated client. Every call goes through the same-origin BFF proxy
 * (/api/proxy/<backend-path>), which injects the Bearer token from the httpOnly
 * cookie server-side. No JWT ever touches client JS.
 */
const PROXY = '/api/proxy';

/**
 * Sprint 13j-PURCH (BP-05, #SR9 class) — surface an RFC7807 ProblemDetails error
 * as a Thai toast. `ApiError` sets `.message` to the body's `detail` (see api-client.ts),
 * so the user-facing reason lives in `.message`, NOT `.detail` (which doesn't exist on
 * ApiError — the old `e.detail` reads always fell through to the generic fallback).
 * Resolution order: ApiError.message (= ProblemDetails.detail) → body.title → body.detail
 * → caller fallback → "เกิดข้อผิดพลาด". Shared so PO/PV approve/post/mark-sent all map
 * the BE Problem to a meaningful toast with one call.
 */
export function problemToast(err: unknown, fallback: string): void {
  let msg: string | undefined;
  if (err instanceof ApiError) {
    // ApiError.message is the ProblemDetails `detail` (api-client.ts ctor 3rd arg).
    msg = err.message;
    if (!msg || !msg.trim()) {
      const body = err.details as { detail?: string; title?: string } | undefined;
      msg = body?.detail ?? body?.title;
    }
  } else if (err && typeof err === 'object') {
    const e = err as { detail?: string; title?: string; message?: string };
    msg = e.detail ?? e.title ?? e.message;
  }
  toast.error(msg && msg.trim() ? msg : fallback);
}

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

/**
 * cont.81 (DataTable) — fetch EVERY page of a cursor-paginated endpoint and return
 * the flattened items, so the unified client-side TanStack table filters/sorts the
 * whole dataset (not just the first page). Loops `cursor` until the server reports
 * no more. SME-scale lists; safe cap to avoid an accidental infinite loop.
 */
export async function fetchAllPages<T>(
  path: string,
  params: Record<string, string | number | boolean | null | undefined> = {},
  pageSize = 100,
): Promise<T[]> {
  const out: T[] = [];
  let cursor: number | undefined;
  for (let guard = 0; guard < 1000; guard++) {
    const page = await request<{ items: T[]; nextCursor: number | null; hasMore: boolean }>(
      `${path}${qs({ ...params, cursor, limit: pageSize })}`,
      { method: 'GET' },
    );
    out.push(...page.items);
    if (!page.hasMore || page.nextCursor == null) break;
    cursor = page.nextCursor;
  }
  return out;
}

/** Sprint 13h P10 — multipart upload helper. Lets the browser set the
 * multipart boundary itself (no fixed Content-Type override). */
export async function apiUploadFile<T>(path: string, file: File): Promise<T> {
  const fd = new FormData();
  fd.append('file', file);
  const res = await fetch(`${PROXY}/${path}`, { method: 'POST', body: fd });
  if (res.status === 204) return undefined as T;
  const body = await res.json().catch(() => null);
  if (!res.ok) {
    const code = body?.title ?? `http_${res.status}`;
    const detail = body?.detail ?? body?.message ?? res.statusText;
    throw new ApiError(res.status, code, detail, body);
  }
  return body as T;
}

/**
 * Sprint 13i B6 (SR8) — print the legal PDF, not the HTML screen.
 * Fetches the document's PDF via the proxy, opens it in a new tab and triggers
 * the browser print dialog on that PDF. `window.print()` on the detail page
 * printed the on-screen HTML, which is not the compliant document.
 */
export async function printPdf(path: string): Promise<void> {
  const res = await fetch(`${PROXY}/${path}`);
  if (!res.ok) throw new ApiError(res.status, 'print_failed', res.statusText);
  const blob = await res.blob();
  const url = URL.createObjectURL(blob);
  const w = window.open(url, '_blank');
  if (w) {
    w.addEventListener('load', () => {
      w.focus();
      w.print();
    });
  }
  // Revoke after a delay so the new tab has time to load + print.
  setTimeout(() => URL.revokeObjectURL(url), 60_000);
}

/** Open a PDF in a new tab WITHOUT auto-triggering the print dialog (user prints from the viewer). */
export async function openPdf(path: string): Promise<void> {
  const res = await fetch(`${PROXY}/${path}`);
  if (!res.ok) throw new ApiError(res.status, 'open_failed', res.statusText);
  const blob = await res.blob();
  const url = URL.createObjectURL(blob);
  window.open(url, '_blank');
  setTimeout(() => URL.revokeObjectURL(url), 60_000);
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
