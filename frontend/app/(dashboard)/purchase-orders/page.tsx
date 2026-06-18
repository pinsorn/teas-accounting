'use client';

import Link from 'next/link';
import { useMemo } from 'react';
import { useTranslations } from 'next-intl';
import { Plus } from 'lucide-react';
import type { ColumnDef } from '@tanstack/react-table';
import { PageHeader } from '@/components/ui/PageHeader';
import { PermissionGate } from '@/components/PermissionGate';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { DataTable, RowLink, dateRangeFilter } from '@/components/ui/DataTable';
import { AgentPendingBadge } from '@/components/ui/AgentPendingBadge';
import { usePurchaseOrders, useBusinessUnitName } from '@/lib/queries';
import type { PurchaseOrderListItem } from '@/lib/types';
import { formatTHB, formatDate } from '@/lib/utils';

// cont.82 — PO list rebuilt on the shared <DataTable> (TanStack): client-side global
// search, per-column filters (status / vendor), sortable headers, clickable docNo → detail.
export default function PurchaseOrdersPage() {
  const t = useTranslations('purchaseOrder');
  const tc = useTranslations('common');
  const q = usePurchaseOrders();
  const buName = useBusinessUnitName();

  const columns = useMemo<ColumnDef<PurchaseOrderListItem>[]>(() => [
    {
      accessorKey: 'docNo',
      header: t('docNo'),
      cell: ({ row }) => (
        <div className="flex items-center gap-2">
          <RowLink href={`/purchase-orders/${row.original.purchaseOrderId}`} mono>
            {row.original.docNo ?? `#${row.original.purchaseOrderId}`}
          </RowLink>
          {row.original.status === 'Draft' && row.original.createdViaApiKey && <AgentPendingBadge />}
        </div>
      ),
    },
    {
      accessorKey: 'status', header: tc('status'), meta: { filter: 'select', filterLabel: tc('status') },
      cell: ({ getValue }) => <StatusBadge status={getValue<string>()} />,
    },
    { accessorKey: 'vendorName', header: t('vendor'), meta: { filter: 'text', filterLabel: t('vendor') } },
    {
      id: 'businessUnit',
      accessorFn: (r) => buName(r.businessUnitId),
      header: tc('businessUnit'),
      meta: { filter: 'select' },
      cell: ({ getValue }) => <span className="text-sm text-base-content/70">{getValue<string>()}</span>,
    },
    {
      accessorKey: 'docDate', header: tc('date'),
      meta: { filter: 'dateRange' },
      filterFn: dateRangeFilter,
      cell: ({ getValue }) => <span className="tabular-nums">{formatDate(getValue<string>())}</span>,
    },
    {
      accessorKey: 'expectedDeliveryDate', header: t('expectedDelivery'),
      cell: ({ getValue }) => <span className="tabular-nums">{getValue<string | null>() ? formatDate(getValue<string>()) : '—'}</span>,
    },
    {
      accessorKey: 'totalAmount', header: t('total'), meta: { align: 'right' },
      cell: ({ getValue }) => <span className="tabular-nums">{formatTHB(getValue<number>())}</span>,
    },
    {
      id: 'actions', header: '', enableSorting: false, meta: { align: 'right' },
      cell: ({ row }) => (
        <Link href={`/purchase-orders/${row.original.purchaseOrderId}`} className="btn btn-ghost btn-xs">
          {tc('view')}
        </Link>
      ),
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
  ], [t, tc]);

  return (
    <>
      <PageHeader title={t('listTitle')} actions={
        <PermissionGate scope="purchase.purchase_order.create">
          <Link href="/purchase-orders/new" data-testid="purchase-order-create" className="btn btn-primary btn-sm gap-1">
            <Plus className="h-4 w-4" aria-hidden /> {t('create')}
          </Link>
        </PermissionGate>
      } />
      <DataTable
        data={q.data ?? []}
        columns={columns}
        isLoading={q.isLoading}
        getRowId={(r) => String(r.purchaseOrderId)}
        searchPlaceholder={t('docNo')}
        initialSorting={[{ id: 'docDate', desc: true }]}
      />
    </>
  );
}
