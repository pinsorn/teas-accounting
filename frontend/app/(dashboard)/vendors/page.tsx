'use client';

import Link from 'next/link';
import { useMemo, useState } from 'react';
import { useTranslations } from 'next-intl';
import { Plus } from 'lucide-react';
import type { ColumnDef } from '@tanstack/react-table';
import { PageHeader } from '@/components/ui/PageHeader';
import { PermissionGate } from '@/components/PermissionGate';
import { DataTable, RowLink } from '@/components/ui/DataTable';
import { useVendors } from '@/lib/queries';
import type { VendorListItem } from '@/lib/types';
import { formatTaxId } from '@/lib/utils';

// cont.82 — vendor master list on the shared <DataTable>. Server search (useVendors)
// stays; DataTable's own global search is off to avoid a second box.
export default function VendorListPage() {
  const t = useTranslations('ven');
  const tc = useTranslations('common');
  const [search, setSearch] = useState('');
  const q = useVendors(search.trim() || undefined);

  const columns = useMemo<ColumnDef<VendorListItem>[]>(() => [
    {
      accessorKey: 'vendorCode',
      header: t('code'),
      cell: ({ row }) => (
        <RowLink href={`/vendors/${row.original.vendorId}`} mono>
          {row.original.vendorCode}
        </RowLink>
      ),
    },
    { accessorKey: 'nameTh', header: t('nameTh') },
    {
      accessorKey: 'taxId', header: t('taxId'),
      cell: ({ getValue }) => <span className="font-mono tabular-nums">{formatTaxId(getValue<string | null>())}</span>,
    },
    {
      accessorFn: (v) => (v.vendorType === 'Individual' ? t('individual') : t('corporate')),
      id: 'vendorType', header: t('type'), meta: { filter: 'select', filterLabel: t('type') },
    },
    {
      accessorFn: (v) => (v.isActive ? tc('active') : tc('inactive')),
      id: 'isActive', header: t('active'), meta: { filter: 'select', filterLabel: tc('status') },
      cell: ({ row }) => <span>{row.original.isActive ? '✓' : '—'}</span>,
    },
  ], [t, tc]);

  return (
    <>
      <PageHeader
        title={t('title')}
        actions={
          <PermissionGate scope="master.vendor.manage">
            <Link href="/vendors/new" data-testid="vendor-create" className="btn btn-primary btn-sm gap-1">
              <Plus className="h-4 w-4" aria-hidden /> {t('create')}
            </Link>
          </PermissionGate>
        }
      />
      <input
        className="input input-bordered input-sm mb-4 w-full max-w-sm"
        placeholder={tc('search')}
        value={search}
        onChange={(e) => setSearch(e.target.value)}
      />
      <DataTable
        data={q.data ?? []}
        columns={columns}
        isLoading={q.isLoading}
        getRowId={(r) => String(r.vendorId)}
        globalSearch={false}
      />
    </>
  );
}
