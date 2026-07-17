import { toast } from 'sonner';
import { emitSessionExpired } from '@/lib/session-events';
import { resolveProblemKey } from '@/lib/i18n/problems';

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

// WP2.3 (F21) layer 2 — a genuinely stuck request (hung upstream, dropped connection, the
// latent 3xx-hang the proxy hardening below also guards) must still REJECT so the caller's
// mutation settles (`isPending` clears, buttons re-enable) instead of hanging forever. This is
// the general guarantee — independent of WP2.3's root cause (the trailing-slash 308).
const REQUEST_TIMEOUT_MS = 30_000;

/**
 * Sprint 13j-PURCH (BP-05, #SR9 class) — surface an RFC7807 ProblemDetails error
 * as a Thai toast. `ApiError` sets `.message` to the body's `detail` (see api-client.ts),
 * so the user-facing reason lives in `.message`, NOT `.detail` (which doesn't exist on
 * ApiError — the old `e.detail` reads always fell through to the generic fallback).
 * Resolution order: ApiError.message (= ProblemDetails.detail) → body.title → body.detail
 * → caller fallback → "เกิดข้อผิดพลาด". Shared so PO/PV approve/post/mark-sent all map
 * the BE Problem to a meaningful toast with one call.
 *
 * WP2.4 (F19) — additionally resolves a Thai message by the stable error CODE first (the
 * `problems.*` i18n dict), and renders the original detail as a secondary sonner
 * `description` line (kept available, not primary) with a longer/sticky duration so a domain
 * error is not gone before the user finishes reading it.
 */
export function problemToast(err: unknown, fallback: string): void {
  let msg: string | undefined;
  let code: string | undefined;
  if (err instanceof ApiError) {
    // ApiError.message is the ProblemDetails `detail` (api-client.ts ctor 3rd arg).
    msg = err.message;
    code = err.code;
    if (!msg || !msg.trim()) {
      const body = err.details as { detail?: string; title?: string } | undefined;
      msg = body?.detail ?? body?.title;
      code = code ?? body?.title;
    }
  } else if (err && typeof err === 'object') {
    const e = err as { detail?: string; title?: string; message?: string };
    msg = e.detail ?? e.title ?? e.message;
    code = e.title;
  }
  const detail = msg && msg.trim() ? msg : fallback;
  const th = code ? resolveProblemKey(code) : null;
  toast.error(th ?? detail, { duration: 8000, description: th && th !== detail ? detail : undefined });
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const res = await fetch(`${PROXY}/${path}`, {
    ...init,
    headers: { 'Content-Type': 'application/json', ...(init.headers ?? {}) },
    // WP2.3 layer 2 — caller-supplied signal (none today) wins if ever added; otherwise the
    // request is bounded so it can never hang a mutation forever.
    signal: init.signal ?? AbortSignal.timeout(REQUEST_TIMEOUT_MS),
  });
  if (res.status === 204) return undefined as T;
  const body = await res.json().catch(() => null);
  if (!res.ok) {
    // WP2.2 (F16/F1) — every call through this proxy is auth-gated (login itself never goes
    // through /api/proxy), so a 401 here always means "the session is gone" — expired token,
    // missing cookie, or (rare) a revoked/locked user. Open the global re-login modal; the
    // ApiError below still throws so the caller's own catch/mutation state behaves exactly as
    // before (isPending clears, its own toast may also fire — the modal is the authoritative
    // recovery path either way). 403 (permission-denied, still authenticated) does NOT trigger it.
    if (res.status === 401) emitSessionExpired();
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
  if (!res.ok) await throwFileResponseError(res, 'open_failed');
  const blob = await res.blob();
  const url = URL.createObjectURL(blob);
  window.open(url, '_blank');
  setTimeout(() => URL.revokeObjectURL(url), 60_000);
}

/** A binary download (PDF/XML) via the proxy — returns a blob URL the caller revokes. */
export async function downloadFile(path: string, filename: string) {
  const res = await fetch(`${PROXY}/${path}`);
  if (!res.ok) await throwFileResponseError(res, 'download_failed');
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

async function throwFileResponseError(res: Response, fallbackCode: string): Promise<never> {
  const body = await res.json().catch(() => null) as { title?: string; detail?: string } | null;
  const fallback = res.status === 422
    ? 'ไม่มีข้อมูลสำหรับเงื่อนไขที่เลือก'
    : res.status >= 500
      ? 'เชื่อมต่อไม่สำเร็จ — ลองใหม่อีกครั้ง'
      : res.status === 404
        ? 'ไม่พบไฟล์ที่ต้องการ'
        : 'ดาวน์โหลดไฟล์ไม่สำเร็จ';
  throw new ApiError(
    res.status,
    body?.title ?? fallbackCode,
    body?.detail ?? body?.title ?? fallback,
    body,
  );
}
