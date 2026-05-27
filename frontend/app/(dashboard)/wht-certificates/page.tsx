'use client';

import Link from 'next/link';
import { useSearchParams } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { PageHeader } from '@/components/ui/PageHeader';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { ListFilters } from '@/components/ui/ListFilters';
import { EmptyState } from '@/components/ui/EmptyState';
import { applyListFilters } from '@/lib/list-filter';
import { useWhtCertificates } from '@/lib/queries';
import { formatTHB, formatDate } from '@/lib/utils';

// Sprint 13j-PURCH D5 — WHT cert statuses for the status filter chip.
const WHT_STATUSES = ['Draft', 'Posted', 'Cancelled'] as const;

export default function WhtCertificateListPage() {
  const t = useTranslations('wht');
  const tc = useTranslations('common');
  const params = useSearchParams();
  const q = useWhtCertificates();
  const loaded = q.data?.pages.flatMap((p) => p.items) ?? [];
  const rows = applyListFilters(loaded, params, {
    status: (r) => r.status,
    docDate: (r) => r.certDate, // WHT uses certDate; dateFrom/dateTo filter on it
  });

  return (
    <>
      <PageHeader title={t('title')} subtitle={t('subtitle')} />
      <ListFilters statusOptions={WHT_STATUSES} statusTestId="wht-filter-status" />
      {q.isSuccess && rows.length === 0 ? (
        <EmptyState title={t('title')} description={tc('empty')} />
      ) : (
      <div className="overflow-x-auto rounded-lg border border-base-300">
        <table className="table table-zebra">
          <thead>
            <tr>
              <th>{t('docNo')}</th><th>{tc('date')}</th><th>{t('payee')}</th>
              <th>{t('formType')}</th><th>{t('incomeType')}</th>
              <th className="text-right">{t('whtAmount')}</th><th>{tc('status')}</th>
            </tr>
          </thead>
          <tbody>
            {q.isLoading && (
              <tr><td colSpan={7} className="py-8 text-center text-base-content/50">{tc('loading')}</td></tr>
            )}
            {rows.map((r) => (
              <tr key={r.whtCertificateId} className="hover">
                <td>
                  <Link href={`/wht-certificates/${r.whtCertificateId}`}
                    className="link link-primary font-mono">{r.docNo}</Link>
                </td>
                <td className="tabular-nums">{formatDate(r.certDate)}</td>
                <td>{r.payeeName}</td>
                <td>{r.formType}</td>
                <td className="font-mono">{r.incomeTypeCode}</td>
                <td className="text-right tabular-nums">{formatTHB(r.whtAmount)}</td>
                <td><StatusBadge status={r.status} /></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      )}
      {q.hasNextPage && (
        <div className="mt-4 text-center">
          <button className="btn btn-ghost btn-sm" onClick={() => q.fetchNextPage()}
            disabled={q.isFetchingNextPage}>
            {tc('loadMore')}
          </button>
        </div>
      )}
    </>
  );
}
