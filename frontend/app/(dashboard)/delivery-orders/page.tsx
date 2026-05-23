'use client';

import Link from 'next/link';
import { useSearchParams } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { PageHeader } from '@/components/ui/PageHeader';
import { QueryStateRow } from '@/components/states/QueryState';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { ListFilters } from '@/components/ui/ListFilters';
import { applyListFilters } from '@/lib/list-filter';
import { useDeliveryOrders } from '@/lib/queries';
import { formatDate } from '@/lib/utils';

// Sprint 13h P5 / 13i C3 — DO list (4-state machine) with status + BU + customer
// + date filters, URL-persisted.
const DO_STATUSES = ['Draft', 'Issued', 'Delivered', 'Cancelled'] as const;

export default function DeliveryOrdersPage() {
  const t = useTranslations('deliveryOrder');
  const tc = useTranslations('common');
  const params = useSearchParams();
  const q = useDeliveryOrders();
  const rows = applyListFilters(q.data ?? [], params, {
    status: (r) => r.status,
    businessUnitId: (r) => r.businessUnitId,
    customerId: (r) => r.customerId,
    docDate: (r) => r.docDate,
  });

  return (
    <>
      <PageHeader title={t('listTitle')} />
      <ListFilters statusOptions={DO_STATUSES} statusTestId="do-filter-status" />
      <div className="overflow-x-auto rounded-lg border border-base-300">
        <table className="table table-zebra">
          <thead><tr>
            <th>{t('docNo')}</th><th>{tc('status')}</th><th>{t('customer')}</th>
            <th>{t('docDate')}</th><th>{t('combined')}</th><th />
          </tr></thead>
          <tbody>
            <QueryStateRow query={q} colSpan={6} isEmpty={rows.length === 0} />
            {rows.map((r) => (
              <tr key={r.deliveryOrderId}>
                <td className="font-mono">{r.docNo ?? `#${r.deliveryOrderId}`}</td>
                <td><StatusBadge status={r.status} /></td>
                <td>{r.customerName}</td>
                <td className="tabular-nums">{formatDate(r.docDate)}</td>
                <td>{r.isCombinedWithTi ? '✓' : '—'}</td>
                <td className="text-right">
                  <Link href={`/delivery-orders/${r.deliveryOrderId}`} className="btn btn-ghost btn-xs">
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
