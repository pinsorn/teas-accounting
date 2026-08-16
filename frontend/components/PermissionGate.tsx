'use client';

import type { ReactNode } from 'react';
import { useMePermissions } from '@/lib/queries';

// Sprint 13d P3 — hide (not disable) write actions the user can't perform, so
// they never fill a form that 403s on submit. Hidden, not disabled: a disabled
// button still lets a user inspect-element + re-enable; absence is the gate.
// F6 exception (2026-08-16): the five doc-chain "convert" buttons (quotation→SO,
// SO→invoice, DO→TI, DO→invoice, invoice→TI) render DISABLED with a tooltip
// instead of hiding — see useScopeState below. Ham's call: since 91e5147 the
// backend hard-403s on the missing target permission, so hiding taught the user
// nothing while a silent 403 on click was worse; a visible reason beats both.
// Every OTHER call site keeps the hide behaviour above unchanged.

export function useHasScope() {
  const { data } = useMePermissions();
  return (scope: string): boolean => {
    if (!data) return false;            // unknown yet → treat as no access
    if (data.isSuperAdmin) return true; // super admin bypasses scope checks
    return data.permissions.includes(scope);
  };
}

// F6 — companion to useHasScope for callers that must tell "still loading" apart
// from "checked and denied" (useHasScope collapses both to false). Loading must
// never render a "you lack permission" tooltip.
export function useScopeState(scope: string): { allowed: boolean; pending: boolean } {
  const { data } = useMePermissions();
  if (!data) return { allowed: false, pending: true }; // unknown yet, not denied
  return { allowed: data.isSuperAdmin || data.permissions.includes(scope), pending: false };
}

export function PermissionGate({
  scope,
  children,
  fallback = null,
}: {
  scope: string;
  children: ReactNode;
  fallback?: ReactNode;
}) {
  const has = useHasScope();
  return has(scope) ? <>{children}</> : <>{fallback}</>;
}
