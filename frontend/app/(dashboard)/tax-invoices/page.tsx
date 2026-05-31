'use client';

import Link from 'next/link';
import { useMemo } from 'react';
import { useTranslations } from 'next-intl';
import { Plus } from 'lucide-react';
import type { ColumnDef } from '@tanstack/react-table';
import { PageHeader } from '@/components/ui/PageHeader';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { DataTable, RowLink, dateRangeFilter } from '@/components/ui/DataTable';
import { useTaxInvoices, useSystemInfo, useBusinessUnitName } from '@/lib/queries';
import { NonVatGuard } from '@/components/ui/NonVatGuard';
import type { TaxInvoiceListItem } from '@/lib/types';
import { formatTHB, formatDate } from '@/lib/utils';

// cont.81 — TI list rebuilt on the shared <DataTable> (TanStack): fetch-all +
// client-side global search, per-column filters (status / payment / customer),
// sortable headers, clickable docNo → detail. Same look as every other list.
export default function TaxInvoiceListPage() {
  const t = useTranslations('ti');
  const tc = useTranslations('common');
  const q = useTaxInvoices();
  const vatMode = useSystemInfo().data?.vatMode ?? true;
  const buName = useBusinessUnitName();

  const columns = useMemo<ColumnDef<TaxInvoiceListItem>[]>(() => [
    {
      accessorKey: 'docNo',
      header: t('list.docNo'),
      cell: ({ row }) => (
        <RowLink href={`/tax-invoices/${row.original.taxInvoiceId}`} mono>
          {row.original.docNo ?? `#${row.original.taxInvoiceId}`}
        </RowLink>
      ),
    },
    {
      accessorKey: 'docDate',
      header: t('list.docDate'),
      meta: { filter: 'dateRange' },
      filterFn: dateRangeFilter,
      cell: ({ getValue }) => <span className="tabular-nums">{formatDate(getValue<string>())}</span>,
    },
    { accessorKey: 'customerName', header: t('list.customer'), meta: { filter: 'text', filterLabel: t('list.customer') } },
    {
      id: 'businessUnit',
      accessorFn: (r) => buName(r.businessUnitId),
      header: tc('businessUnit'),
      meta: { filter: 'select' },
      cell: ({ getValue }) => <span className="text-sm text-base-content/70">{getValue<string>()}</span>,
    },
    {
      accessorKey: 'totalAmount', header: t('list.total'), meta: { align: 'right' },
      cell: ({ getValue }) => <span className="tabular-nums">{formatTHB(getValue<number>())}</span>,
    },
    {
      accessorKey: 'taxAmount', header: t('list.vat'), meta: { align: 'right' },
      cell: ({ getValue }) => <span className="tabular-nums">{formatTHB(getValue<number>())}</span>,
    },
    {
      accessorKey: 'status', header: t('list.status'), meta: { filter: 'select', filterLabel: tc('status') },
      cell: ({ getValue }) => <StatusBadge status={getValue<string>()} />,
    },
    {
      accessorKey: 'paymentStatus', header: t('list.payment'), meta: { filter: 'select', filterLabel: t('list.payment') },
      cell: ({ getValue }) => <StatusBadge status={getValue<string>()} />,
    },
    {
      id: 'actions', header: '', enableSorting: false, meta: { align: 'right' },
      cell: ({ row }) => (
        <Link href={`/tax-invoices/${row.original.taxInvoiceId}`} className="link link-primary text-sm">
          {tc('view')}
        </Link>
      ),
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
  ], [t, tc]);

  // ม.86/4 — a non-VAT company cannot issue Tax Invoices; guard direct URL access.
  if (!vatMode) return <NonVatGuard title={t('title')} />;

  return (
    <>
      <PageHeader
        title={t('title')}
        actions={
          <Link href="/tax-invoices/new" className="btn btn-primary btn-sm gap-1">
            <Plus className="h-4 w-4" aria-hidden /> {t('create')}
          </Link>
        }
      />
      <DataTable
        data={q.data ?? []}
        columns={columns}
        isLoading={q.isLoading}
        getRowId={(r) => String(r.taxInvoiceId)}
        searchPlaceholder={t('list.docNo')}
        initialSorting={[{ id: 'docDate', desc: true }]}
      />
    </>
  );
}
