'use client';

import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { Plus } from 'lucide-react';
import { PageHeader } from '@/components/ui/PageHeader';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { DocumentNumberBadge } from '@/components/ui/DocumentNumberBadge';
import { useVendorInvoices } from '@/lib/queries';
import { formatTHB, formatDate } from '@/lib/utils';

export default function VendorInvoiceListPage() {
  const t = useTranslations('vi');
  const tc = useTranslations('common');
  const q = useVendorInvoices();
  const rows = q.data?.pages.flatMap((p) => p.items) ?? [];

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
      <div className="overflow-x-auto rounded-lg border border-base-300">
        <table className="table table-zebra">
          <thead>
            <tr>
              <th>No.</th><th>{t('vendorTiNo')}</th><th>{t('vendor')}</th>
              <th>{t('claimPeriod')}</th>
              <th className="text-right">{t('total')}</th>
              <th>{t('settled')}</th><th>Status</th>
            </tr>
          </thead>
          <tbody>
            {q.isLoading && (
              <tr><td colSpan={7} className="py-8 text-center text-base-content/50">{tc('loading')}</td></tr>
            )}
            {!q.isLoading && rows.length === 0 && (
              <tr><td colSpan={7} className="py-8 text-center text-base-content/50">{tc('empty')}</td></tr>
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
