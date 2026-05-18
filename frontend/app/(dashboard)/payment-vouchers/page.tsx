'use client';

import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { Plus } from 'lucide-react';
import { PageHeader } from '@/components/ui/PageHeader';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { DocumentNumberBadge } from '@/components/ui/DocumentNumberBadge';
import { usePaymentVouchers } from '@/lib/queries';
import { formatTHB, formatDate } from '@/lib/utils';

export default function PaymentVoucherListPage() {
  const t = useTranslations('pv');
  const tc = useTranslations('common');
  const q = usePaymentVouchers();
  const rows = q.data?.pages.flatMap((p) => p.items) ?? [];

  return (
    <>
      <PageHeader
        title={t('title')}
        actions={
          <Link href="/payment-vouchers/new" className="btn btn-primary btn-sm gap-1">
            <Plus className="h-4 w-4" aria-hidden /> {t('create')}
          </Link>
        }
      />
      <div className="overflow-x-auto rounded-lg border border-base-300">
        <table className="table table-zebra">
          <thead>
            <tr>
              <th>No.</th><th>Date</th><th>{t('vendor')}</th>
              <th className="text-right">{t('wht')}</th>
              <th className="text-right">{t('netPaid')}</th><th>Status</th>
            </tr>
          </thead>
          <tbody>
            {q.isLoading && (
              <tr><td colSpan={6} className="py-8 text-center text-base-content/50">{tc('loading')}</td></tr>
            )}
            {!q.isLoading && rows.length === 0 && (
              <tr><td colSpan={6} className="py-8 text-center text-base-content/50">{tc('empty')}</td></tr>
            )}
            {rows.map((r) => (
              <tr key={r.paymentVoucherId} className="hover">
                <td>
                  <Link href={`/payment-vouchers/${r.paymentVoucherId}`}>
                    <DocumentNumberBadge value={r.docNo} />
                  </Link>
                </td>
                <td className="tabular-nums">{formatDate(r.docDate)}</td>
                <td>{r.vendorName}</td>
                <td className="text-right tabular-nums">{formatTHB(r.whtAmount)}</td>
                <td className="text-right tabular-nums">{formatTHB(r.totalPaid)}</td>
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
