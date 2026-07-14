'use client';

import { useSessionKeepAlive } from '@/lib/useSessionKeepAlive';

/** WP2.1 — thin client mount point for the keep-alive hook (the dashboard layout is a Server
 * Component and cannot call hooks directly). No visible output. */
export function SessionKeepAlive() {
  useSessionKeepAlive();
  return null;
}
