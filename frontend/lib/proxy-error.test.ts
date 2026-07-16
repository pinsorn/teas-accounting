import { describe, it, expect } from 'vitest';
import { classifyUpstreamFailure } from './proxy-error';

// S13a — the BFF proxy's fetch to the backend previously had no timeout and could
// hang forever on a stuck upstream. Verifies the two failure modes map to distinct,
// FE-toastable envelopes: a real timeout (504, "not confirmed, retry") vs any other
// connection failure (502, generic).
describe('classifyUpstreamFailure', () => {
  it('maps a TimeoutError (AbortSignal.timeout) to a 504 retry envelope', () => {
    const err = new DOMException('The operation was aborted.', 'TimeoutError');
    const { body, status } = classifyUpstreamFailure(err);
    expect(status).toBe(504);
    expect(body.title).toBe('gateway.timeout');
    expect(body.detail).toContain('ลองใหม่');
  });

  it('maps a generic connection failure to the existing 502 envelope', () => {
    const err = new TypeError('fetch failed');
    const { body, status } = classifyUpstreamFailure(err);
    expect(status).toBe(502);
    expect(body.title).toBe('gateway.error');
  });

  it('maps a non-Error throw to the 502 envelope (defensive)', () => {
    const { body, status } = classifyUpstreamFailure('not an error');
    expect(status).toBe(502);
    expect(body.title).toBe('gateway.error');
  });
});
