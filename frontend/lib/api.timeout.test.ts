import { describe, it, expect, vi, afterEach } from 'vitest';

// WP2.3 (F21) layer 2 — a genuinely stuck request must REJECT (never hang forever) so a
// mutation settles and its Save button re-enables. Verifies the WIRING (request() passes an
// AbortSignal into fetch, and honours it) rather than waiting out the real 30s bound —
// AbortSignal.timeout()'s own countdown is a JS-platform guarantee, not ours to re-prove.
describe('lib/api request() timeout wiring', () => {
  const realFetch = global.fetch;
  const realTimeout = AbortSignal.timeout.bind(AbortSignal);

  afterEach(() => {
    global.fetch = realFetch;
    vi.restoreAllMocks();
  });

  it('rejects a request that never settles once its abort signal fires', async () => {
    // Shrink the bound from 30s to 5ms for the test — proves the plumbing, not the platform.
    vi.spyOn(AbortSignal, 'timeout').mockImplementation(() => realTimeout(5));

    global.fetch = vi.fn((_url: RequestInfo | URL, init?: RequestInit) =>
      new Promise((_resolve, reject) => {
        init?.signal?.addEventListener('abort', () =>
          reject(new DOMException('The operation was aborted.', 'TimeoutError')),
        );
      }),
    ) as typeof fetch;

    const { apiGet } = await import('./api');
    await expect(apiGet('stuck-endpoint')).rejects.toThrow();

    expect(global.fetch).toHaveBeenCalledTimes(1);
    const init = (global.fetch as ReturnType<typeof vi.fn>).mock.calls[0]?.[1] as RequestInit;
    expect(init.signal).toBeInstanceOf(AbortSignal);
  });
});
