'use client';

import Link from 'next/link';
import { useSearchParams } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { Plus } from 'lucide-react';
import { PageHeader } from '@/components/ui/PageHeader';
import { QueryStateRow } from '@/components/states/QueryState';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { DocumentNumberBadge } from '@/components/ui/DocumentNumberBadge';
import { ListFilters } from '@/components/ui/ListFilters';
import { applyListFilters } from '@/lib/list-filter';
import { useReceipts } from '@/lib/queries';
import { formatTHB, formatDate } from '@/lib/utils';

// Sprint 13i C3 — RC list: BU filter stays server-side (paginated); status +
// customer + date filter the loaded rows client-side. All URL-persisted.
const RC_STATUSES = ['Draft', 'Posted', 'Voided'] as const;

export default function ReceiptListPage() {
  const t = useTranslations('rc');
  const tc = useTranslations('common');
  const params = useSearchParams();
  const buId = params.get('bu') ? Number(params.get('bu')) : undefined;
  const q = useReceipts(buId);
  const rows = applyListFilters(q.data?.pages.flatMap((p) => p.items) ?? [], params, {
    status: (r) => r.status,
    customerId: (r) => r.customerId,
    docDate: (r) => r.docDate,
  });

  return (
    <>
      <PageHeader
        title={t('title')}
        actions={
          <Link href="/receipts/new" className="btn btn-primary btn-sm gap-1">
            <Plus className="h-4 w-4" aria-hidden /> {t('create')}
          </Link>
        }
      />
      <ListFilters statusOptions={RC_STATUSES} statusTestId="rc-filter-status" />
      <div className="overflow-x-auto rounded-lg border border-base-300">
        <table className="table table-zebra">
          <thead>
            <tr><th>{t('docNo')}</th><th>{t('date')}</th><th>{t('customer')}</th><th className="text-right">{t('amount')}</th><th className="text-right">{t('wht.column')}</th><th>{tc('status')}</th><th className="text-right" /></tr>
          </thead>
          <tbody>
            <QueryStateRow query={q} colSpan={7} isEmpty={rows.length === 0} />
            {rows.map((r) => (
              <tr key={r.receiptId} className="hover">
                <td><Link href={`/receipts/${r.receiptId}`}><DocumentNumberBadge value={r.docNo} /></Link></td>
                <td className="tabular-nums">{formatDate(r.docDate)}</td>
                <td>{r.customerName}</td>
                <td className="text-right tabular-nums">{formatTHB(r.amount)}</td>
                <td className="text-right tabular-nums">{r.whtAmount > 0 ? formatTHB(r.whtAmount) : '—'}</td>
                <td><StatusBadge status={r.status} /></td>
                <td className="text-right">
                  <Link href={`/receipts/${r.receiptId}`} className="link link-primary text-sm">
                    {tc('view')}
                  </Link>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {q.hasNextPage && (
        <div className="mt-4 text-center">
          <button className="btn btn-ghost btn-sm" onClick={() => q.fetchNextPage()} disabled={q.isFetchingNextPage}>
            {tc('loadMore')}
          </button>
        </div>
      )}
    </>
  );
}
