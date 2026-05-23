'use client';

import Link from 'next/link';
import { useSearchParams } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { PageHeader } from '@/components/ui/PageHeader';
import { QueryStateRow } from '@/components/states/QueryState';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { ListFilters } from '@/components/ui/ListFilters';
import { applyListFilters } from '@/lib/list-filter';
import { useSalesOrders } from '@/lib/queries';
import { formatTHB, formatDate } from '@/lib/utils';

// Sprint 13h P5 / 13i C3 — SO list with status + BU + customer + date filters,
// URL-persisted (refresh-safe + shareable).
const SO_STATUSES = ['Draft', 'Posted', 'Closed', 'Cancelled'] as const;

export default function SalesOrdersPage() {
  const t = useTranslations('salesOrder');
  const tc = useTranslations('common');
  const params = useSearchParams();
  const q = useSalesOrders();
  const rows = applyListFilters(q.data ?? [], params, {
    status: (r) => r.status,
    businessUnitId: (r) => r.businessUnitId,
    customerId: (r) => r.customerId,
    docDate: (r) => r.docDate,
  });

  return (
    <>
      <PageHeader title={t('listTitle')} />
      <ListFilters statusOptions={SO_STATUSES} statusTestId="so-filter-status" />
      <div className="overflow-x-auto rounded-lg border border-base-300">
        <table className="table table-zebra">
          <thead><tr>
            <th>{t('docNo')}</th><th>{tc('status')}</th><th>{t('customer')}</th>
            <th>{t('docDate')}</th><th className="text-right">{t('total')}</th><th />
          </tr></thead>
          <tbody>
            <QueryStateRow query={q} colSpan={6} isEmpty={rows.length === 0} />
            {rows.map((r) => (
              <tr key={r.salesOrderId}>
                <td className="font-mono">{r.docNo ?? `#${r.salesOrderId}`}</td>
                <td><StatusBadge status={r.status} /></td>
                <td>{r.customerName}</td>
                <td className="tabular-nums">{formatDate(r.docDate)}</td>
                <td className="text-right tabular-nums">{formatTHB(r.totalAmount)}</td>
                <td className="text-right">
                  <Link href={`/sales-orders/${r.salesOrderId}`} className="btn btn-ghost btn-xs">
                    {tc('view')}
                  </Link>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </>
  );
}
