'use client';

import { useEffect, type ReactNode } from 'react';
import { useRouter } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { ShieldAlert, AlertCircle, Inbox } from 'lucide-react';
import { ApiError } from '@/lib/api';
import { auth } from '@/lib/auth';

// Sprint 13d P2 — distinguish a failed query from an empty list. A 403 used
// to render as "no data" so users wasted time opening a create modal that
// would also 403. Now: 403 → NoAccess, 401 → logout, other → Error+retry,
// empty 200 → Empty, else children.

export function NoAccessState() {
  const t = useTranslations('states');
  return (
    <div
      className="flex flex-col items-center gap-2 py-12 text-center"
      data-testid="state-no-access"
    >
      <ShieldAlert className="h-10 w-10 text-warning" aria-hidden />
      <div className="font-semibold">{t('noAccessTitle')}</div>
      <div className="max-w-md text-sm text-base-content/60">
        {t('noAccessBody')}
      </div>
    </div>
  );
}

export function ErrorState({ onRetry }: { onRetry?: () => void }) {
  const t = useTranslations('states');
  const tc = useTranslations('common');
  return (
    <div
      className="flex flex-col items-center gap-2 py-12 text-center"
      data-testid="state-error"
    >
      <AlertCircle className="h-10 w-10 text-error" aria-hidden />
      <div className="font-semibold">{t('errorTitle')}</div>
      {onRetry && (
        <button className="btn btn-sm btn-ghost mt-1" onClick={onRetry}>
          {tc('retry')}
        </button>
      )}
    </div>
  );
}

export function EmptyState({ message }: { message?: string }) {
  const tc = useTranslations('common');
  return (
    <div
      className="py-12 text-center text-base-content/50"
      data-testid="state-empty"
    >
      <Inbox className="mx-auto mb-2 h-8 w-8 opacity-50" aria-hidden />
      {message ?? tc('empty')}
    </div>
  );
}

interface QueryLike {
  isLoading: boolean;
  isError: boolean;
  error: unknown;
  refetch?: () => void;
}

export function QueryState({
  query,
  isEmpty = false,
  emptyMessage,
  children,
}: {
  query: QueryLike;
  isEmpty?: boolean;
  emptyMessage?: string;
  children: ReactNode;
}) {
  const tc = useTranslations('common');
  const router = useRouter();

  const err = query.error;
  const status = err instanceof ApiError ? err.status : undefined;

  // 401 = expired/invalid session → clear cookie + bounce to login.
  useEffect(() => {
    if (status !== 401) return;
    void auth.logout().catch(() => {}).finally(() => {
      router.push('/login');
      router.refresh();
    });
  }, [status, router]);

  if (query.isLoading) {
    return (
      <div className="py-12 text-center text-base-content/50" data-testid="state-loading">
        {tc('loading')}
      </div>
    );
  }

  if (query.isError) {
    if (status === 403) return <NoAccessState />;
    if (status === 401) return null; // redirecting
    return <ErrorState onRetry={query.refetch} />;
  }

  if (isEmpty) return <EmptyState message={emptyMessage} />;

  return <>{children}</>;
}

// Sprint 13i B2 — table-row variant for list pages whose <tbody> hand-rolled
// loading/empty rows and so rendered a 403 as "ไม่มีข้อมูล" (SR4). Drop this as
// the first child of <tbody>; it renders a single full-width <tr> for the
// loading / no-access / error / empty states, or null when there is data.
export function QueryStateRow({
  query,
  colSpan,
  isEmpty,
  emptyMessage,
}: {
  query: QueryLike;
  colSpan: number;
  isEmpty: boolean;
  emptyMessage?: string;
}) {
  const tc = useTranslations('common');
  const t = useTranslations('states');
  const router = useRouter();

  const err = query.error;
  const status = err instanceof ApiError ? err.status : undefined;

  useEffect(() => {
    if (status !== 401) return;
    void auth.logout().catch(() => {}).finally(() => {
      router.push('/login');
      router.refresh();
    });
  }, [status, router]);

  if (query.isLoading) {
    return (
      <tr><td colSpan={colSpan} className="py-8 text-center text-base-content/50" data-testid="state-loading">
        {tc('loading')}
      </td></tr>
    );
  }

  if (query.isError) {
    if (status === 401) return null; // redirecting
    if (status === 403) {
      return (
        <tr><td colSpan={colSpan} className="py-8" data-testid="state-no-access">
          <div className="flex flex-col items-center gap-2 text-center">
            <ShieldAlert className="h-9 w-9 text-warning" aria-hidden />
            <div className="font-semibold">{t('noAccessTitle')}</div>
            <div className="max-w-md text-sm text-base-content/60">{t('noAccessBody')}</div>
          </div>
        </td></tr>
      );
    }
    return (
      <tr><td colSpan={colSpan} className="py-8" data-testid="state-error">
        <div className="flex flex-col items-center gap-2 text-center">
          <AlertCircle className="h-9 w-9 text-error" aria-hidden />
          <div className="font-semibold">{t('errorTitle')}</div>
          {query.refetch && (
            <button className="btn btn-sm btn-ghost mt-1" onClick={query.refetch}>{tc('retry')}</button>
          )}
        </div>
      </td></tr>
    );
  }

  if (isEmpty) {
    return (
      <tr><td colSpan={colSpan} className="py-8 text-center text-base-content/50" data-testid="state-empty">
        {emptyMessage ?? tc('empty')}
      </td></tr>
    );
  }

  return null;
}
