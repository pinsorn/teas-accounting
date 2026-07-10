'use client';

import Link from 'next/link';
import { useMemo } from 'react';
import { useTranslations } from 'next-intl';
import { Plus } from 'lucide-react';
import type { ColumnDef } from '@tanstack/react-table';
import { PageHeader } from '@/components/ui/PageHeader';
import { PermissionGate } from '@/components/PermissionGate';
import { DataTable, RowLink } from '@/components/ui/DataTable';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { useFixedAssets } from '@/lib/queries';
import type { FixedAssetListItem } from '@/lib/types';

const money = (n: number) => n.toLocaleString('th-TH', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

// Cycle D (specs/fixed-assets.md §9.12) — asset register list. Mirrors bank-accounts/page.tsx:
// PageHeader + PermissionGate "New" button + DataTable (client-side status/category filters).
export default function FixedAssetListPage() {
  const t = useTranslations('fixedAssets');
  const tc = useTranslations('common');
  const q = useFixedAssets();

  const columns = useMemo<ColumnDef<FixedAssetListItem>[]>(() => [
    {
      accessorKey: 'docNo',
      header: t('docNo'),
      cell: ({ row }) => (
        <RowLink href={`/fixed-assets/${row.original.fixedAssetId}`}>
          {row.original.docNo ?? t('draftLabel')}
        </RowLink>
      ),
    },
    { accessorKey: 'name', header: t('name'), meta: { filter: 'text' } },
    {
      accessorKey: 'category', header: t('category'), meta: { filter: 'select' },
      cell: ({ getValue }) => <span>{getValue<string | null>() ?? '—'}</span>,
    },
    { accessorKey: 'acquireDate', header: t('acquireDate') },
    {
      accessorKey: 'cost', header: t('cost'), meta: { align: 'right' },
      cell: ({ getValue }) => money(getValue<number>()),
    },
    {
      accessorKey: 'accumulatedDepreciation', header: t('accumulatedDepreciation'), meta: { align: 'right' },
      cell: ({ getValue }) => money(getValue<number>()),
    },
    {
      accessorKey: 'nbv', header: t('nbv'), meta: { align: 'right' },
      cell: ({ getValue }) => money(getValue<number>()),
    },
    {
      accessorKey: 'status', header: tc('status'), meta: { filter: 'select' },
      cell: ({ getValue }) => <StatusBadge status={getValue<string>()} />,
    },
    {
      id: 'actions', header: '', enableSorting: false, meta: { align: 'right' },
      cell: ({ row }) => (
        <Link href={`/fixed-assets/${row.original.fixedAssetId}`} className="link link-primary text-sm">
          {tc('view')}
        </Link>
      ),
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
  ], [t, tc]);

  return (
    <>
      <PageHeader
        title={t('title')}
        actions={
          <PermissionGate scope="fixedasset.manage">
            <Link href="/fixed-assets/new" data-testid="fixed-asset-create" className="btn btn-primary btn-sm gap-1">
              <Plus className="h-4 w-4" aria-hidden /> {t('create')}
            </Link>
          </PermissionGate>
        }
      />
      <DataTable
        data={q.data ?? []}
        columns={columns}
        isLoading={q.isLoading}
        getRowId={(r) => String(r.fixedAssetId)}
        searchPlaceholder={t('name')}
      />
    </>
  );
}
