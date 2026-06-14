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
import { useBillingNotes, useBusinessUnitName } from '@/lib/queries';
import type { BillingNoteListItem } from '@/lib/types';
import { formatTHB, formatDate } from '@/lib/utils';

// cont.82 — Billing Note (ใบแจ้งหนี้/ใบวางบิล) list rebuilt on the shared <DataTable>
// (TanStack): fetch-all + client-side global search, per-column filters
// (status / customer), sortable headers, clickable docNo → detail.
export default function BillingNotesPage() {
  const t = useTranslations('billingNote');
  const tc = useTranslations('common');
  const q = useBillingNotes();
  const buName = useBusinessUnitName();

  const columns = useMemo<ColumnDef<BillingNoteListItem>[]>(() => [
    {
      accessorKey: 'docNo',
      header: t('docNo'),
      cell: ({ row }) => (
        <RowLink href={`/invoices/${row.original.billingNoteId}`} mono>
          {row.original.docNo ?? `#${row.original.billingNoteId}`}
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
      accessorKey: 'dueDate', header: t('dueDate'),
      cell: ({ getValue }) => <span className="tabular-nums">{formatDate(getValue<string>())}</span>,
    },
    {
      accessorKey: 'totalAmount', header: t('total'), meta: { align: 'right' },
      cell: ({ getValue }) => <span className="tabular-nums">{formatTHB(getValue<number>())}</span>,
    },
    {
      id: 'actions', header: '', enableSorting: false, meta: { align: 'right' },
      cell: ({ row }) => (
        <Link href={`/invoices/${row.original.billingNoteId}`} className="btn btn-ghost btn-xs">{tc('view')}</Link>
      ),
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
  ], [t, tc]);

  return (
    <>
      <PageHeader title={t('listTitle')} actions={
        <PermissionGate scope="sales.billing_note.manage">
          <Link href="/invoices/new" className="btn btn-primary btn-sm gap-1">
            <Plus className="h-4 w-4" aria-hidden /> {t('create')}
          </Link>
        </PermissionGate>
      } />
      <DataTable
        data={q.data ?? []}
        columns={columns}
        isLoading={q.isLoading}
        getRowId={(r) => String(r.billingNoteId)}
        searchPlaceholder={t('docNo')}
        initialSorting={[{ id: 'docDate', desc: true }]}
      />
    </>
  );
}
