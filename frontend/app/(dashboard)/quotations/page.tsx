'use client';

import Link from 'next/link';
import { useMemo } from 'react';
import { useTranslations } from 'next-intl';
import { Plus } from 'lucide-react';
import type { ColumnDef } from '@tanstack/react-table';
import { PageHeader } from '@/components/ui/PageHeader';
import { PermissionGate } from '@/components/PermissionGate';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { AgentPendingBadge } from '@/components/ui/AgentPendingBadge';
import { DataTable, RowLink, dateRangeFilter } from '@/components/ui/DataTable';
import { useQuotations, useBusinessUnitName } from '@/lib/queries';
import type { QuotationListItem } from '@/lib/types';
import { formatTHB, formatDate } from '@/lib/utils';

// cont.82 — Q list rebuilt on the shared <DataTable> (TanStack): fetch-all +
// client-side global search, per-column filters (status / customer), sortable
// headers, clickable docNo → detail. Same look as every other list.
export default function QuotationsPage() {
  const t = useTranslations('quotation');
  const tc = useTranslations('common');
  const q = useQuotations();
  const buName = useBusinessUnitName();

  const columns = useMemo<ColumnDef<QuotationListItem>[]>(() => [
    {
      accessorKey: 'docNo',
      header: t('docNo'),
      cell: ({ row }) => (
        <RowLink href={`/quotations/${row.original.quotationId}`} mono>
          {row.original.docNo ?? `#${row.original.quotationId}`}
        </RowLink>
      ),
    },
    {
      accessorKey: 'status', header: tc('status'), meta: { filter: 'select', filterLabel: tc('status') },
      cell: ({ row }) => (
        <span className="inline-flex flex-wrap gap-1">
          <StatusBadge status={row.original.status} />
          {row.original.status === 'Draft' && row.original.createdViaApiKey && <AgentPendingBadge />}
        </span>
      ),
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
      accessorKey: 'totalAmount', header: t('total'), meta: { align: 'right' },
      cell: ({ getValue }) => <span className="tabular-nums">{formatTHB(getValue<number>())}</span>,
    },
    {
      id: 'actions', header: '', enableSorting: false, meta: { align: 'right' },
      cell: ({ row }) => (
        row.original.status === 'Draft' ? (
          <span className="inline-flex gap-1">
            <Link href={`/quotations/${row.original.quotationId}`} className="btn btn-ghost btn-xs">{tc('view')}</Link>
            <Link href={`/quotations/${row.original.quotationId}/edit`} className="btn btn-ghost btn-xs">{tc('edit')}</Link>
          </span>
        ) : (
          <Link href={`/quotations/${row.original.quotationId}`} className="btn btn-ghost btn-xs">{tc('view')}</Link>
        )
      ),
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
  ], [t, tc]);

  return (
    <>
      <PageHeader title={t('listTitle')} actions={
        <PermissionGate scope="sales.quotation.manage">
          <Link href="/quotations/new" data-testid="quotation-create" className="btn btn-primary btn-sm gap-1">
            <Plus className="h-4 w-4" aria-hidden /> {t('create')}
          </Link>
        </PermissionGate>
      } />
      <DataTable
        data={q.data ?? []}
        columns={columns}
        isLoading={q.isLoading}
        getRowId={(r) => String(r.quotationId)}
        searchPlaceholder={t('docNo')}
        initialSorting={[{ id: 'docDate', desc: true }]}
      />
    </>
  );
}
