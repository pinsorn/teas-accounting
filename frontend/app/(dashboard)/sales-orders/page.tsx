'use client';

import Link from 'next/link';
import { useMemo } from 'react';
import { useTranslations } from 'next-intl';
import type { ColumnDef } from '@tanstack/react-table';
import { PageHeader } from '@/components/ui/PageHeader';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { DataTable, RowLink } from '@/components/ui/DataTable';
import { useSalesOrders } from '@/lib/queries';
import type { SalesOrderListItem } from '@/lib/types';
import { formatTHB, formatDate } from '@/lib/utils';

// cont.82 — SO list rebuilt on the shared <DataTable> (TanStack): fetch-all +
// client-side global search, per-column filters (status / customer), sortable
// headers, clickable docNo → detail. Same look as every other list.
export default function SalesOrdersPage() {
  const t = useTranslations('salesOrder');
  const tc = useTranslations('common');
  const q = useSalesOrders();

  const columns = useMemo<ColumnDef<SalesOrderListItem>[]>(() => [
    {
      accessorKey: 'docNo',
      header: t('docNo'),
      cell: ({ row }) => (
        <RowLink href={`/sales-orders/${row.original.salesOrderId}`} mono>
          {row.original.docNo ?? `#${row.original.salesOrderId}`}
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
      accessorKey: 'totalAmount', header: t('total'), meta: { align: 'right' },
      cell: ({ getValue }) => <span className="tabular-nums">{formatTHB(getValue<number>())}</span>,
    },
    {
      id: 'actions', header: '', enableSorting: false, meta: { align: 'right' },
      cell: ({ row }) => (
        <Link href={`/sales-orders/${row.original.salesOrderId}`} className="btn btn-ghost btn-xs">{tc('view')}</Link>
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
        getRowId={(r) => String(r.salesOrderId)}
        searchPlaceholder={t('docNo')}
        initialSorting={[{ id: 'docDate', desc: true }]}
      />
    </>
  );
}
