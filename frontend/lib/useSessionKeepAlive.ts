'use client';

import { useEffect, useRef } from 'react';

// WP2.1 (F16, D6 sliding re-issue) — keeps an ACTIVE dashboard session alive by calling
// POST /api/auth/refresh at ~60% of the token's remaining TTL, so a user who never stops
// working never hits the hard 401 a fixed expiry used to produce mid-form. An EXPIRED token
// can never be refreshed (the backend's JWT bearer handler rejects it before the /auth/refresh
// handler ever runs) and the backend re-checks the absolute session cap + user active/locked
// status on every call — so this can only ever EXTEND a genuinely active, still-valid,
// still-permitted session, never resurrect or widen one.
const IDLE_TIMEOUT_MS = 15 * 60_000; // stop sliding an abandoned tab after 15 min of no input
const MIN_TICK_MS = 30_000; // floor so a very short TTL (dev/test) can't busy-loop

async function callRefresh(): Promise<number | null> {
  try {
    const res = await fetch('/api/auth/refresh', { method: 'POST', cache: 'no-store' });
    if (!res.ok) return null;
    const body = await res.json().catch(() => null);
    return body?.expires_at ? new Date(body.expires_at).getTime() : null;
  } catch {
    return null;
  }
}

/** Mount once (dashboard layout). No visible output — side-effect only. */
export function useSessionKeepAlive(): void {
  const lastActivityRef = useRef(Date.now());
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const cancelledRef = useRef(false);

  useEffect(() => {
    cancelledRef.current = false;

    const clearTimer = () => {
      if (timerRef.current) {
        clearTimeout(timerRef.current);
        timerRef.current = null;
      }
    };

    const tick = async () => {
      timerRef.current = null;
      if (cancelledRef.current) return;
      if (document.visibilityState !== 'visible') return; // resumes on visibilitychange
      if (Date.now() - lastActivityRef.current > IDLE_TIMEOUT_MS) return; // resumes on next activity

      const expiresAt = await callRefresh();
      // Failure (401/403/network) — stop silently. The token will eventually really expire
      // and lib/api.ts's global 401 interceptor (WP2.2) takes over with the re-login modal.
      if (cancelledRef.current || expiresAt == null) return;

      const delay = Math.max(MIN_TICK_MS, Math.round((expiresAt - Date.now()) * 0.6));
      timerRef.current = setTimeout(tick, delay);
    };

    const resumeIfNoTimerScheduled = () => {
      if (!timerRef.current && document.visibilityState === 'visible') void tick();
    };

    const onVisibilityChange = () => {
      if (document.visibilityState === 'visible') resumeIfNoTimerScheduled();
      else clearTimer();
    };

    // Naturally throttled: while a tick is already scheduled (the common case), activity is a
    // no-op — it only matters right after an idle/hidden pause, to resume sliding promptly.
    const onActivity = () => {
      lastActivityRef.current = Date.now();
      resumeIfNoTimerScheduled();
    };

    const activityEvents = ['mousedown', 'keydown', 'touchstart'] as const;
    activityEvents.forEach((e) => window.addEventListener(e, onActivity, { passive: true }));
    document.addEventListener('visibilitychange', onVisibilityChange);

    void tick(); // prime — learns the current expires_at and schedules the real cadence

    return () => {
      cancelledRef.current = true;
      clearTimer();
      activityEvents.forEach((e) => window.removeEventListener(e, onActivity));
      document.removeEventListener('visibilitychange', onVisibilityChange);
    };
  }, []);
}
