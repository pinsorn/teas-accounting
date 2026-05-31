'use client';

import Link from 'next/link';
import { useMemo } from 'react';
import { useTranslations } from 'next-intl';
import type { ColumnDef } from '@tanstack/react-table';
import { PageHeader } from '@/components/ui/PageHeader';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { DataTable, RowLink } from '@/components/ui/DataTable';
import { useDeliveryOrders } from '@/lib/queries';
import type { DeliveryOrderListItem } from '@/lib/types';
import { formatDate } from '@/lib/utils';

// cont.82 — DO list (4-state machine) rebuilt on the shared <DataTable> (TanStack):
// fetch-all + client-side global search, per-column filters (status / customer),
// sortable headers, clickable docNo → detail. Same look as every other list.
export default function DeliveryOrdersPage() {
  const t = useTranslations('deliveryOrder');
  const tc = useTranslations('common');
  const q = useDeliveryOrders();

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
      accessorKey: 'docDate', header: t('docDate'),
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
      />
    </>
  );
}
