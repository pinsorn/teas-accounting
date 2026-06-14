'use client';

import Link from 'next/link';
import { useMemo } from 'react';
import { useSearchParams } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { Plus } from 'lucide-react';
import type { ColumnDef } from '@tanstack/react-table';
import { PageHeader } from '@/components/ui/PageHeader';
import { PermissionGate } from '@/components/PermissionGate';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { DataTable, RowLink, dateRangeFilter } from '@/components/ui/DataTable';
import { IncompleteOnlyToggle } from '@/components/ui/IncompleteOnlyToggle';
import { IncompleteFlag } from '@/components/ui/CompletenessBadge';
import { usePaymentVouchers, useBusinessUnitName } from '@/lib/queries';
import type { PaymentVoucherListItem } from '@/lib/types';
import { formatTHB, formatDate } from '@/lib/utils';

// cont.82 — PV list rebuilt on the shared <DataTable> (TanStack). The server-side
// incompleteOnly flag stays wired through the hook (toggle above the table); client-side
// global search + per-column filters (status / vendor), sortable headers, docNo → detail.
export default function PaymentVoucherListPage() {
  const t = useTranslations('pv');
  const tc = useTranslations('common');
  const params = useSearchParams();
  const incompleteOnly = params.get('incompleteOnly') === 'true';
  const q = usePaymentVouchers(incompleteOnly);
  const buName = useBusinessUnitName();

  const columns = useMemo<ColumnDef<PaymentVoucherListItem>[]>(() => [
    {
      accessorKey: 'docNo',
      header: t('docNo'),
      cell: ({ row }) => (
        <div className="flex items-center gap-2">
          <RowLink href={`/payment-vouchers/${row.original.paymentVoucherId}`} mono>
            {row.original.docNo ?? `#${row.original.paymentVoucherId}`}
          </RowLink>
          {row.original.status === 'Posted' && <IncompleteFlag isComplete={row.original.isComplete} />}
        </div>
      ),
    },
    {
      accessorKey: 'docDate', header: tc('date'),
      meta: { filter: 'dateRange' },
      filterFn: dateRangeFilter,
      cell: ({ getValue }) => <span className="tabular-nums">{formatDate(getValue<string>())}</span>,
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
      accessorKey: 'whtAmount', header: t('wht'), meta: { align: 'right' },
      cell: ({ getValue }) => <span className="tabular-nums">{formatTHB(getValue<number>())}</span>,
    },
    {
      accessorKey: 'totalPaid', header: t('netPaid'), meta: { align: 'right' },
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
      <PageHeader
        title={t('title')}
        actions={
          <PermissionGate scope="purchase.payment_voucher.create">
            <Link href="/payment-vouchers/new" className="btn btn-primary btn-sm gap-1">
              <Plus className="h-4 w-4" aria-hidden /> {t('create')}
            </Link>
          </PermissionGate>
        }
      />
      <IncompleteOnlyToggle />
      <DataTable
        data={q.data ?? []}
        columns={columns}
        isLoading={q.isLoading}
        getRowId={(r) => String(r.paymentVoucherId)}
        searchPlaceholder={t('docNo')}
        initialSorting={[{ id: 'docDate', desc: true }]}
      />
    </>
  );
}
