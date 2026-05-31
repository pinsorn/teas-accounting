'use client';

import { useMemo } from 'react';
import { useTranslations } from 'next-intl';
import type { ColumnDef } from '@tanstack/react-table';
import { PageHeader } from '@/components/ui/PageHeader';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { DataTable, RowLink, dateRangeFilter } from '@/components/ui/DataTable';
import { useWhtCertificates } from '@/lib/queries';
import type { WhtCertificateListItem } from '@/lib/types';
import { formatTHB, formatDate } from '@/lib/utils';

// cont.82 — WHT cert list rebuilt on the shared <DataTable> (TanStack): client-side
// global search + per-column filters (status / payee), sortable headers, docNo → detail.
export default function WhtCertificateListPage() {
  const t = useTranslations('wht');
  const tc = useTranslations('common');
  const q = useWhtCertificates();

  const columns = useMemo<ColumnDef<WhtCertificateListItem>[]>(() => [
    {
      accessorKey: 'docNo',
      header: t('docNo'),
      cell: ({ row }) => (
        <RowLink href={`/wht-certificates/${row.original.whtCertificateId}`} mono>
          {row.original.docNo ?? `#${row.original.whtCertificateId}`}
        </RowLink>
      ),
    },
    {
      accessorKey: 'certDate', header: tc('date'),
      meta: { filter: 'dateRange' },
      filterFn: dateRangeFilter,
      cell: ({ getValue }) => <span className="tabular-nums">{formatDate(getValue<string>())}</span>,
    },
    { accessorKey: 'payeeName', header: t('payee'), meta: { filter: 'text', filterLabel: t('payee') } },
    { accessorKey: 'formType', header: t('formType') },
    {
      accessorKey: 'incomeTypeCode', header: t('incomeType'),
      cell: ({ getValue }) => <span className="font-mono">{getValue<string>()}</span>,
    },
    {
      accessorKey: 'whtAmount', header: t('whtAmount'), meta: { align: 'right' },
      cell: ({ getValue }) => <span className="tabular-nums">{formatTHB(getValue<number>())}</span>,
    },
    {
      accessorKey: 'status', header: tc('status'), meta: { filter: 'select', filterLabel: tc('status') },
      cell: ({ getValue }) => <StatusBadge status={getValue<string>()} />,
    },
  ], [t, tc]);

  return (
    <>
      <PageHeader title={t('title')} subtitle={t('subtitle')} />
      <DataTable
        data={q.data ?? []}
        columns={columns}
        isLoading={q.isLoading}
        getRowId={(r) => String(r.whtCertificateId)}
        searchPlaceholder={t('docNo')}
        initialSorting={[{ id: 'certDate', desc: true }]}
      />
    </>
  );
}
