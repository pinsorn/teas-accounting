'use client';

import Link from 'next/link';
import { useMemo, useState } from 'react';
import { useTranslations } from 'next-intl';
import { Plus, Search } from 'lucide-react';
import type { ColumnDef } from '@tanstack/react-table';
import { PageHeader } from '@/components/ui/PageHeader';
import { PermissionGate } from '@/components/PermissionGate';
import { EmptyState } from '@/components/ui/EmptyState';
import { DataTable, RowLink } from '@/components/ui/DataTable';
import { useCustomers } from '@/lib/queries';
import type { CustomerListItem } from '@/lib/types';
import { formatTaxId } from '@/lib/utils';

// Sprint 13j-FE — Customer master list (sales). cont.82 — table rebuilt on the
// shared <DataTable>; existing server search (useCustomers) stays, DataTable's own
// global search is off to avoid a second box; mascot EmptyState kept for no-data.
export default function CustomerListPage() {
  const t = useTranslations('cust');
  const tc = useTranslations('common');
  const [search, setSearch] = useState('');
  const q = useCustomers(search.trim() || undefined);
  const rows = q.data ?? [];

  const columns = useMemo<ColumnDef<CustomerListItem>[]>(() => [
    {
      accessorKey: 'customerCode', header: t('code'),
      cell: ({ row }) => (
        <RowLink href={`/customers/${row.original.customerId}`} mono>
          {row.original.customerCode}
        </RowLink>
      ),
    },
    {
      accessorKey: 'nameTh', header: t('nameTh'),
      cell: ({ row }) => (
        <Link href={`/customers/${row.original.customerId}`} className="font-medium text-ink-900 hover:text-peach-700 hover:underline">
          {row.original.nameTh}
          {row.original.nameEn && <span className="ml-2 text-xs text-ink-400">{row.original.nameEn}</span>}
        </Link>
      ),
    },
    {
      accessorKey: 'taxId', header: t('taxId'),
      cell: ({ getValue }) => <span className="font-mono tabular-nums text-ink-600">{formatTaxId(getValue<string | null>())}</span>,
    },
    {
      accessorFn: (c) => (c.customerType === 'Individual' ? t('individual') : t('corporate')),
      id: 'customerType', header: t('type'), meta: { filter: 'select', filterLabel: t('type') },
    },
    {
      accessorKey: 'vatRegistered', header: 'VAT', enableSorting: false,
      cell: ({ getValue }) => getValue<boolean>()
        ? <span className="rounded-full bg-status-success-bg px-2 py-0.5 text-xs font-semibold text-status-success">VAT</span>
        : <span className="text-ink-300">—</span>,
    },
    {
      accessorFn: (c) => (c.isActive ? tc('active') : tc('inactive')),
      id: 'isActive', header: tc('status'), meta: { filter: 'select', filterLabel: tc('status') },
      cell: ({ row }) => row.original.isActive
        ? <span className="inline-flex items-center gap-1.5 rounded-full bg-status-success-bg px-2.5 py-0.5 text-xs font-semibold text-status-success"><span className="h-1.5 w-1.5 rounded-full bg-current" />{tc('active')}</span>
        : <span className="inline-flex items-center gap-1.5 rounded-full bg-status-draft-bg px-2.5 py-0.5 text-xs font-semibold text-status-draft"><span className="h-1.5 w-1.5 rounded-full bg-current" />{tc('inactive')}</span>,
    },
    {
      id: 'actions', header: '', enableSorting: false, meta: { align: 'right' },
      cell: ({ row }) => (
        <Link href={`/customers/${row.original.customerId}`} className="btn btn-ghost btn-xs gap-1 text-peach-700">
          {tc('view')}
        </Link>
      ),
    },
  ], [t, tc]);

  return (
    <>
      <PageHeader
        title={t('title')}
        subtitle={t('subtitle')}
        actions={
          <PermissionGate scope="master.customer.manage">
            <Link href="/customers/new" data-testid="customer-create" className="btn btn-primary btn-sm gap-1">
              <Plus className="h-4 w-4" aria-hidden /> {t('create')}
            </Link>
          </PermissionGate>
        }
      />

      <div className="mb-4 flex w-full max-w-sm items-center gap-2 rounded-full border border-ink-100 bg-base-100 px-3 py-1.5 text-ink-500">
        <Search className="h-4 w-4 shrink-0" aria-hidden />
        <input
          className="min-w-0 flex-1 border-none bg-transparent text-sm text-ink-900 outline-none placeholder:text-ink-400"
          placeholder={tc('search')}
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          aria-label={tc('search')}
        />
      </div>

      {q.isSuccess && rows.length === 0 ? (
        <EmptyState
          title={t('emptyTitle')}
          description={t('emptyDesc')}
          cta={{ label: t('create'), href: '/customers/new' }}
        />
      ) : (
        <DataTable
          data={rows}
          columns={columns}
          isLoading={q.isLoading}
          getRowId={(r) => String(r.customerId)}
          globalSearch={false}
        />
      )}
    </>
  );
}
