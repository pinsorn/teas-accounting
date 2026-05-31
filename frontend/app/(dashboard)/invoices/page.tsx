'use client';

import Link from 'next/link';
import { useMemo } from 'react';
import { useTranslations } from 'next-intl';
import { Plus } from 'lucide-react';
import type { ColumnDef } from '@tanstack/react-table';
import { PageHeader } from '@/components/ui/PageHeader';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { DataTable, RowLink } from '@/components/ui/DataTable';
import { useBillingNotes } from '@/lib/queries';
import type { BillingNoteListItem } from '@/lib/types';
import { formatTHB, formatDate } from '@/lib/utils';

// cont.82 — Billing Note (ใบแจ้งหนี้/ใบวางบิล) list rebuilt on the shared <DataTable>
// (TanStack): fetch-all + client-side global search, per-column filters
// (status / customer), sortable headers, clickable docNo → detail.
export default function BillingNotesPage() {
  const t = useTranslations('billingNote');
  const tc = useTranslations('common');
  const q = useBillingNotes();

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
      accessorKey: 'docDate', header: t('docDate'),
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
  ], [t, tc]);

  return (
    <>
      <PageHeader title={t('listTitle')} actions={
        <Link href="/invoices/new" className="btn btn-primary btn-sm gap-1">
          <Plus className="h-4 w-4" aria-hidden /> {t('create')}
        </Link>
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
