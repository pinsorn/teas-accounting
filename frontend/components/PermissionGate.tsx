'use client';

import type { ReactNode } from 'react';
import { useMePermissions } from '@/lib/queries';

// Sprint 13d P3 — hide (not disable) write actions the user can't perform, so
// they never fill a form that 403s on submit. Hidden, not disabled: a disabled
// button still lets a user inspect-element + re-enable; absence is the gate.

export function useHasScope() {
  const { data } = useMePermissions();
  return (scope: string): boolean => {
    if (!data) return false;            // unknown yet → treat as no access
    if (data.isSuperAdmin) return true; // super admin bypasses scope checks
    return data.permissions.includes(scope);
  };
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
