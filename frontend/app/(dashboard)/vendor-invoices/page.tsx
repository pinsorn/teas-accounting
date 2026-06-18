'use client';

import { useMemo } from 'react';
import { useSearchParams } from 'next/navigation';
import { useTranslations } from 'next-intl';
import type { ColumnDef } from '@tanstack/react-table';
import { PageHeader } from '@/components/ui/PageHeader';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { DataTable, RowLink, dateRangeFilter } from '@/components/ui/DataTable';
import { IncompleteOnlyToggle } from '@/components/ui/IncompleteOnlyToggle';
import { IncompleteFlag } from '@/components/ui/CompletenessBadge';
import { AgentPendingBadge } from '@/components/ui/AgentPendingBadge';
import { useVendorInvoices, useBusinessUnitName } from '@/lib/queries';
import type { VendorInvoiceListItem } from '@/lib/types';
import { formatTHB, formatDate } from '@/lib/utils';

// cont.82 — VI list rebuilt on the shared <DataTable> (TanStack). The server-side
// incompleteOnly flag stays wired through the hook (toggle above the table); client-side
// global search + per-column filters (status / vendor), sortable headers, docNo → detail.
export default function VendorInvoiceListPage() {
  const t = useTranslations('vi');
  const tc = useTranslations('common');
  const params = useSearchParams();
  const incompleteOnly = params.get('incompleteOnly') === 'true';
  const q = useVendorInvoices(incompleteOnly);
  const buName = useBusinessUnitName();

  const columns = useMemo<ColumnDef<VendorInvoiceListItem>[]>(() => [
    {
      accessorKey: 'docNo',
      header: t('docNo'),
      cell: ({ row }) => (
        <div className="flex items-center gap-2">
          <RowLink href={`/vendor-invoices/${row.original.vendorInvoiceId}`} mono>
            {row.original.docNo ?? `#${row.original.vendorInvoiceId}`}
          </RowLink>
          {row.original.status === 'Posted' && <IncompleteFlag isComplete={row.original.isComplete} />}
          {row.original.status === 'Draft' && row.original.createdViaApiKey && <AgentPendingBadge />}
        </div>
      ),
    },
    {
      accessorKey: 'docDate', header: tc('date'),
      meta: { filter: 'dateRange' }, filterFn: dateRangeFilter,
      cell: ({ getValue }) => <span className="tabular-nums">{formatDate(getValue<string>())}</span>,
    },
    { accessorKey: 'vendorTaxInvoiceNo', header: t('vendorTiNo'),
      cell: ({ getValue }) => <span className="font-mono">{getValue<string>()}</span>,
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
      accessorKey: 'vatClaimPeriod', header: t('claimPeriod'),
      cell: ({ getValue }) => <span className="tabular-nums">{getValue<number>()}</span>,
    },
    {
      accessorKey: 'totalAmount', header: t('total'), meta: { align: 'right' },
      cell: ({ getValue }) => <span className="tabular-nums">{formatTHB(getValue<number>())}</span>,
    },
    {
      accessorKey: 'status', header: tc('status'), meta: { filter: 'select', filterLabel: tc('status') },
      cell: ({ getValue }) => <StatusBadge status={getValue<string>()} />,
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
  ], [t, tc]);

  return (
    <>
      {/* purchase-completeness D1 — anti-floating: NO "create floating VI" entry
          point. VI creation is reached from the PV (PV→VI guided create). */}
      <PageHeader title={t('title')} subtitle={t('createFromPvHint')} />
      <IncompleteOnlyToggle />
      <DataTable
        data={q.data ?? []}
        columns={columns}
        isLoading={q.isLoading}
        getRowId={(r) => String(r.vendorInvoiceId)}
        searchPlaceholder={t('docNo')}
        initialSorting={[{ id: 'docNo', desc: true }]}
        emptyContent={t('createFromPvHint')}
      />
    </>
  );
}
