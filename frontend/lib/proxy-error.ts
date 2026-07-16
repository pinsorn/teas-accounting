// S13a — classifies an upstream fetch failure from the BFF proxy
// (app/api/proxy/[...path]/route.ts) into the JSON envelope + status the FE toast
// layer shows. A stuck backend now aborts via AbortSignal.timeout(30s), which throws
// a TimeoutError (not a plain AbortError/network error) — distinguished here so a
// timeout surfaces as a clear "not confirmed, retry" 504 instead of the generic
// "connection failed" 502. Pure so it's unit-testable without mocking Next.js
// request/cookie machinery.
export interface ProxyFailureEnvelope {
  body: { title: string; detail: string };
  status: number;
}

export function classifyUpstreamFailure(e: unknown): ProxyFailureEnvelope {
  const timedOut = e instanceof Error && e.name === 'TimeoutError';
  return timedOut
    ? { body: { title: 'gateway.timeout', detail: 'ยังไม่ยืนยันผล — ลองใหม่' }, status: 504 }
    : { body: { title: 'gateway.error', detail: 'Connection to backend failed.' }, status: 502 };
}
