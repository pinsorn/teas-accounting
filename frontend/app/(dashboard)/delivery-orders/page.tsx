'use client';

import Link from 'next/link';
import { useMemo } from 'react';
import { useTranslations } from 'next-intl';
import type { ColumnDef } from '@tanstack/react-table';
import { PageHeader } from '@/components/ui/PageHeader';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { DataTable, RowLink, dateRangeFilter } from '@/components/ui/DataTable';
import { useDeliveryOrders, useBusinessUnitName } from '@/lib/queries';
import type { DeliveryOrderListItem } from '@/lib/types';
import { formatDate } from '@/lib/utils';

// cont.82 — DO list (4-state machine) rebuilt on the shared <DataTable> (TanStack):
// fetch-all + client-side global search, per-column filters (status / customer),
// sortable headers, clickable docNo → detail. Same look as every other list.
export default function DeliveryOrdersPage() {
  const t = useTranslations('deliveryOrder');
  const tc = useTranslations('common');
  const q = useDeliveryOrders();
  const buName = useBusinessUnitName();

  const columns = useMemo<ColumnDef<DeliveryOrderListItem>[]>(() => [
    {
      accessorKey: 'docNo',
      header: t('docNo'),
      cell: ({ row }) => (
        <RowLink href={`/delivery-orders/${row.original.deliveryOrderId}`} mono>
          {row.original.docNo ?? `#${row.original.deliveryOrderId}`}
        </RowLink>
      ),
    },
    {
      accessorKey: 'status', header: tc('status'), meta: { filter: 'select', filterLabel: tc('status') },
      cell: ({ getValue }) => <StatusBadge status={getValue<string>()} />,
    },
    { accessorKey: 'customerName', header: t('customer'), meta: { filter: 'text', filterLabel: t('customer') } },
    {
      id: 'businessUnit',
      accessorFn: (r) => buName(r.businessUnitId),
      header: tc('businessUnit'),
      meta: { filter: 'select' },
      cell: ({ getValue }) => <span className="text-sm text-base-content/70">{getValue<string>()}</span>,
    },
    {
      accessorKey: 'docDate', header: t('docDate'),
      meta: { filter: 'dateRange' },
      filterFn: dateRangeFilter,
      cell: ({ getValue }) => <span className="tabular-nums">{formatDate(getValue<string>())}</span>,
    },
    {
      accessorKey: 'isCombinedWithTi', header: t('combined'),
      cell: ({ getValue }) => <span>{getValue<boolean>() ? '✓' : '—'}</span>,
    },
    {
      id: 'actions', header: '', enableSorting: false, meta: { align: 'right' },
      cell: ({ row }) => (
        <Link href={`/delivery-orders/${row.original.deliveryOrderId}`} className="btn btn-ghost btn-xs">{tc('view')}</Link>
      ),
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
  ], [t, tc]);

  return (
    <>
      <PageHeader title={t('listTitle')} />
      <DataTable
        data={q.data ?? []}
        columns={columns}
        isLoading={q.isLoading}
        getRowId={(r) => String(r.deliveryOrderId)}
        searchPlaceholder={t('docNo')}
        initialSorting={[{ id: 'docDate', desc: true }]}
        urlFilters={['status']}
      />
    </>
  );
}
