'use client';

import Link from 'next/link';
import { useMemo } from 'react';
import { useSearchParams } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { Plus } from 'lucide-react';
import type { ColumnDef } from '@tanstack/react-table';
import { PageHeader } from '@/components/ui/PageHeader';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { DataTable, RowLink } from '@/components/ui/DataTable';
import { useReceipts } from '@/lib/queries';
import type { ReceiptListItem } from '@/lib/types';
import { formatTHB, formatDate } from '@/lib/utils';

// cont.82 — RC list rebuilt on the shared <DataTable> (TanStack): fetch-all +
// client global search, per-column filters (status / customer), sortable headers,
// clickable docNo → detail. BU scope stays server-side via the `bu` URL param.
export default function ReceiptListPage() {
  const t = useTranslations('rc');
  const tc = useTranslations('common');
  const params = useSearchParams();
  const buId = params.get('bu') ? Number(params.get('bu')) : undefined;
  const q = useReceipts(buId);

  const columns = useMemo<ColumnDef<ReceiptListItem>[]>(() => [
    {
      accessorKey: 'docNo',
      header: t('docNo'),
      cell: ({ row }) => (
        <RowLink href={`/receipts/${row.original.receiptId}`} mono>
          {row.original.docNo ?? `#${row.original.receiptId}`}
        </RowLink>
      ),
    },
    {
      accessorKey: 'docDate',
      header: t('date'),
      cell: ({ getValue }) => <span className="tabular-nums">{formatDate(getValue<string>())}</span>,
    },
    { accessorKey: 'customerName', header: t('customer'), meta: { filter: 'text', filterLabel: t('customer') } },
    {
      accessorKey: 'amount', header: t('amount'), meta: { align: 'right' },
      cell: ({ getValue }) => <span className="tabular-nums">{formatTHB(getValue<number>())}</span>,
    },
    {
      accessorKey: 'whtAmount', header: t('wht.column'), meta: { align: 'right' },
      cell: ({ getValue }) => {
        const v = getValue<number>();
        return <span className="tabular-nums">{v > 0 ? formatTHB(v) : '—'}</span>;
      },
    },
    {
      accessorKey: 'status', header: tc('status'), meta: { filter: 'select', filterLabel: tc('status') },
      cell: ({ getValue }) => <StatusBadge status={getValue<string>()} />,
    },
    {
      id: 'actions', header: '', enableSorting: false, meta: { align: 'right' },
      cell: ({ row }) => (
        <Link href={`/receipts/${row.original.receiptId}`} className="link link-primary text-sm">
          {tc('view')}
        </Link>
      ),
    },
  ], [t, tc]);

  return (
    <>
      <PageHeader
        title={t('title')}
        actions={
          <Link href="/receipts/new" className="btn btn-primary btn-sm gap-1">
            <Plus className="h-4 w-4" aria-hidden /> {t('create')}
          </Link>
        }
      />
      <DataTable
        data={q.data ?? []}
        columns={columns}
        isLoading={q.isLoading}
        getRowId={(r) => String(r.receiptId)}
        searchPlaceholder={t('docNo')}
        initialSorting={[{ id: 'docDate', desc: true }]}
      />
    </>
  );
}
