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
import { useExpenseClaims } from '@/lib/queries';
import type { ExpenseClaimListItem } from '@/lib/types';

// Cycle C (specs/expense-claims.md §5) — expense claim list. Mirrors bank-accounts/page.tsx:
// PageHeader + PermissionGate "New" button + DataTable (client-side status/employee filters).
export default function ExpenseClaimListPage() {
  const t = useTranslations('expenseClaims');
  const tc = useTranslations('common');
  const q = useExpenseClaims();

  const columns = useMemo<ColumnDef<ExpenseClaimListItem>[]>(() => [
    {
      accessorKey: 'docNo',
      header: t('docNo'),
      cell: ({ row }) => (
        <RowLink href={`/expense-claims/${row.original.expenseClaimId}`}>
          {row.original.docNo ?? t('draftLabel')}
        </RowLink>
      ),
    },
    { accessorKey: 'employeeName', header: t('employee'), meta: { filter: 'text' } },
    { accessorKey: 'claimDate', header: t('claimDate') },
    {
      accessorKey: 'status', header: tc('status'), meta: { filter: 'select' },
      cell: ({ getValue }) => <StatusBadge status={getValue<string>()} />,
    },
    {
      accessorKey: 'totalAmount', header: t('totalAmount'), meta: { align: 'right' },
      cell: ({ getValue }) => getValue<number>().toLocaleString('th-TH', { minimumFractionDigits: 2, maximumFractionDigits: 2 }),
    },
    {
      id: 'actions', header: '', enableSorting: false, meta: { align: 'right' },
      cell: ({ row }) => (
        <Link href={`/expense-claims/${row.original.expenseClaimId}`} className="link link-primary text-sm">
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
          <PermissionGate scope="expense.claim.create">
            <Link href="/expense-claims/new" data-testid="expense-claim-create" className="btn btn-primary btn-sm gap-1">
              <Plus className="h-4 w-4" aria-hidden /> {t('create')}
            </Link>
          </PermissionGate>
        }
      />
      <DataTable
        data={q.data ?? []}
        columns={columns}
        isLoading={q.isLoading}
        getRowId={(r) => String(r.expenseClaimId)}
        searchPlaceholder={t('employee')}
      />
    </>
  );
}
