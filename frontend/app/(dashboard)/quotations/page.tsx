'use client';

import Link from 'next/link';
import { useMemo } from 'react';
import { useTranslations } from 'next-intl';
import { Plus } from 'lucide-react';
import type { ColumnDef } from '@tanstack/react-table';
import { PageHeader } from '@/components/ui/PageHeader';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { DataTable, RowLink } from '@/components/ui/DataTable';
import { useQuotations } from '@/lib/queries';
import type { QuotationListItem } from '@/lib/types';
import { formatTHB, formatDate } from '@/lib/utils';

// cont.82 — Q list rebuilt on the shared <DataTable> (TanStack): fetch-all +
// client-side global search, per-column filters (status / customer), sortable
// headers, clickable docNo → detail. Same look as every other list.
export default function QuotationsPage() {
  const t = useTranslations('quotation');
  const tc = useTranslations('common');
  const q = useQuotations();

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
  ], [t, tc]);

  return (
    <>
      <PageHeader title={t('listTitle')} actions={
        <Link href="/quotations/new" className="btn btn-primary btn-sm gap-1">
          <Plus className="h-4 w-4" aria-hidden /> {t('create')}
        </Link>
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
