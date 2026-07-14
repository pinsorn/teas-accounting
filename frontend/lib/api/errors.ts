// Sprint 13d P5 — single parser for the unified error envelope.
//
// After P5 the backend emits ONE shape for both business-rule and validation
// failures (DomainExceptionMiddleware + ValidationErrorEnvelopeMiddleware):
//   { type, title, detail, status, fieldErrors?: [ { field, messages[] } ] }
// Business errors omit fieldErrors; validation errors include them with
// i18n-key messages. This module normalizes either into one structure so
// callers stop branching on shape.

import { ApiError, problemToast } from '@/lib/api';
import { resolveValidationKey, currentLocale } from '@/lib/i18n/validation';
import { resolveProblemKey } from '@/lib/i18n/problems';

export interface FieldError {
  field: string;
  messages: string[]; // i18n keys (or legacy literals during migration)
}

export interface ParsedApiError {
  status: number;
  code: string;
  detail: string;
  fieldErrors: FieldError[];
}

export function parseApiError(err: unknown): ParsedApiError {
  if (!(err instanceof ApiError)) {
    return { status: 0, code: 'unknown', detail: 'Unexpected error', fieldErrors: [] };
  }
  const body = (err.details ?? {}) as Record<string, unknown>;
  const rawFE = Array.isArray(body.fieldErrors) ? body.fieldErrors : [];
  const fieldErrors: FieldError[] = rawFE.map((f) => {
    const o = f as { field?: string; messages?: unknown };
    return {
      field: String(o.field ?? ''),
      messages: Array.isArray(o.messages) ? o.messages.map(String) : [],
    };
  });
  return {
    status: err.status,
    code: (body.title as string) ?? err.code,
    detail: (body.detail as string) ?? err.message,
    fieldErrors,
  };
}

/** Localized one-line summary suitable for a toast. WP2.4 (F19) — for a plain domain error
 * (no fieldErrors), prefer the Thai translation keyed by the stable error CODE; unknown codes
 * (or an `en` locale) fall back to the backend's own detail string, unchanged. */
export function errorToToast(err: unknown): string {
  const p = parseApiError(err);
  if (p.fieldErrors.length > 0) {
    const first = p.fieldErrors[0]!;
    const key = first.messages[0] ?? 'validation.unknown';
    return resolveValidationKey(key, currentLocale());
  }
  return resolveProblemKey(p.code) ?? p.detail;
}

/** WP2.4 (F19) — one-stop sink for the `toast.error(errorToToast(e))` call-site pattern: same
 * Thai-by-code resolution as errorToToast, PLUS the longer/sticky duration and the EN detail
 * kept as a secondary line (via problemToast, lib/api.ts). No fallback text needed — errorToToast
 * always resolves to something displayable. */
export function apiErrorToast(err: unknown): void {
  problemToast(err, errorToToast(err));
}

/** field → localized messages, for inline rendering under inputs. */
export function fieldErrorMap(err: unknown): Record<string, string[]> {
  const out: Record<string, string[]> = {};
  for (const fe of parseApiError(err).fieldErrors) {
    out[fe.field] = fe.messages.map((m) => resolveValidationKey(m));
  }
  return out;
}
