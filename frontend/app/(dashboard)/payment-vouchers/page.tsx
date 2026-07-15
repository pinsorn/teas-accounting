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
import { AgentPendingBadge } from '@/components/ui/AgentPendingBadge';
import { usePaymentVouchers, useBusinessUnitName, useBusinessUnits } from '@/lib/queries';
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
  // R1 fix (troubles-wiki.md) — `columns` below is memoized on [t, tc] only; an accessorFn
  // closing over `buName` alone freezes on whatever business-units data was loaded at mount.
  // Depend on the query's own data so the memo recomputes once it arrives.
  const { data: businessUnits } = useBusinessUnits(true);

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
          {row.original.status === 'Draft' && row.original.createdViaApiKey && <AgentPendingBadge />}
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
      // R8 fix (troubles-wiki.md) — `row.getValue()` caches this accessorFn's result on
      // the row object FOREVER (TanStack Table core: `row._valuesCache`), invalidated only
      // when the `data` array itself gets a new reference — NOT when `columns` (and thus a
      // fresher `buName` closure) changes. `accessorFn` still returns the resolved name (so
      // the faceted filter dropdown/options keep working), but `cell` re-resolves directly
      // from the immutable `row.original.businessUnitId` on every render — bypassing that
      // cache — so the DISPLAYED value is always live.
      accessorFn: (r) => buName(r.businessUnitId),
      header: tc('businessUnit'),
      meta: { filter: 'select' },
      cell: ({ row }) => <span className="text-sm text-base-content/70">{buName(row.original.businessUnitId)}</span>,
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
  ], [t, tc, businessUnits]);

  return (
    <>
      <PageHeader
        title={t('title')}
        actions={
          <PermissionGate scope="purchase.payment_voucher.create">
            <Link href="/payment-vouchers/new" data-testid="payment-voucher-create" className="btn btn-primary btn-sm gap-1">
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
