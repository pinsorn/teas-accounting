import { describe, it, expect, vi } from 'vitest';
import { bffInternalError } from './bff-error';

// WP-D (GPT-5.6 review MEDIUM-02, 2026-09-04) — the shared 500 helper must never leak
// exception text into the response body, must always carry a traceId for server-log
// correlation, and must log server-side (console.error) for that same correlation.
const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

describe('bffInternalError', () => {
  it('returns a generic detail + uuid traceId, never the exception text', async () => {
    const spy = vi.spyOn(console, 'error').mockImplementation(() => {});
    const res = bffInternalError('x', new Error('secret host:5432'));
    expect(res.status).toBe(500);
    const body = await res.json();
    expect(body.title).toBe('auth.handler_error');
    expect(body.detail).toBe('Internal error');
    expect(body.traceId).toMatch(UUID_RE);
    expect(JSON.stringify(body)).not.toContain('secret');
    spy.mockRestore();
  });
});
