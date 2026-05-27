'use client';

import Link from 'next/link';
import { useSearchParams } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { Plus } from 'lucide-react';
import { PageHeader } from '@/components/ui/PageHeader';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { DocumentNumberBadge } from '@/components/ui/DocumentNumberBadge';
import { ListFilters } from '@/components/ui/ListFilters';
import { EmptyState } from '@/components/ui/EmptyState';
import { applyListFilters } from '@/lib/list-filter';
import { useVendorInvoices } from '@/lib/queries';
import { formatTHB, formatDate } from '@/lib/utils';

// Sprint 13j-PURCH D5 — VI statuses for the status filter chip.
const VI_STATUSES = ['Draft', 'Posted', 'Cancelled'] as const;

export default function VendorInvoiceListPage() {
  const t = useTranslations('vi');
  const tc = useTranslations('common');
  const params = useSearchParams();
  const q = useVendorInvoices();
  const loaded = q.data?.pages.flatMap((p) => p.items) ?? [];
  const rows = applyListFilters(loaded, params, {
    status: (r) => r.status,
    docDate: (r) => r.docDate,
  });

  return (
    <>
      <PageHeader
        title={t('title')}
        actions={
          <Link href="/vendor-invoices/new" className="btn btn-primary btn-sm gap-1">
            <Plus className="h-4 w-4" aria-hidden /> {t('create')}
          </Link>
        }
      />
      <ListFilters statusOptions={VI_STATUSES} statusTestId="vi-filter-status" />
      {q.isSuccess && rows.length === 0 ? (
        <EmptyState
          title={t('title')}
          description={tc('empty')}
          cta={{ label: t('create'), href: '/vendor-invoices/new' }}
        />
      ) : (
      <div className="overflow-x-auto rounded-lg border border-base-300">
        <table className="table table-zebra">
          <thead>
            <tr>
              <th>{t('docNo')}</th><th>{t('vendorTiNo')}</th><th>{t('vendor')}</th>
              <th>{t('claimPeriod')}</th>
              <th className="text-right">{t('total')}</th>
              <th>{t('settled')}</th><th>{tc('status')}</th>
            </tr>
          </thead>
          <tbody>
            {q.isLoading && (
              <tr><td colSpan={7} className="py-8 text-center text-base-content/50">{tc('loading')}</td></tr>
            )}
            {rows.map((r) => (
              <tr key={r.vendorInvoiceId} className="hover">
                <td>
                  <Link href={`/vendor-invoices/${r.vendorInvoiceId}`}>
                    <DocumentNumberBadge value={r.docNo} />
                  </Link>
                </td>
                <td className="font-mono">{r.vendorTaxInvoiceNo}</td>
                <td>{r.vendorName}</td>
                <td className="tabular-nums">{r.vatClaimPeriod}</td>
                <td className="text-right tabular-nums">{formatTHB(r.totalAmount)}</td>
                <td><StatusBadge status={r.settlementStatus} /></td>
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
