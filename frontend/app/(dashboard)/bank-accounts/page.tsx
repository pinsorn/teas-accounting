'use client';

import Link from 'next/link';
import { useMemo } from 'react';
import { useTranslations } from 'next-intl';
import { Plus } from 'lucide-react';
import type { ColumnDef } from '@tanstack/react-table';
import { PageHeader } from '@/components/ui/PageHeader';
import { PermissionGate } from '@/components/PermissionGate';
import { DataTable, RowLink } from '@/components/ui/DataTable';
import { useBankAccounts, useGlAccounts } from '@/lib/queries';
import type { BankAccountListItem } from '@/lib/types';

// Bank reconciliation (specs/bank-reconciliation.md B1.11) — bank-account master list.
// Mirrors receipts/page.tsx: PageHeader + PermissionGate + <DataTable>.
export default function BankAccountListPage() {
  const t = useTranslations('bank');
  const tc = useTranslations('common');
  const q = useBankAccounts();
  const glAccounts = useGlAccounts();

  const accountLabel = (id: number) => {
    const a = glAccounts.data?.find((x) => x.accountId === id);
    return a ? `${a.accountCode} — ${a.accountNameTh}` : `#${id}`;
  };

  const columns = useMemo<ColumnDef<BankAccountListItem>[]>(() => [
    {
      accessorKey: 'bankName',
      header: t('bankName'),
      cell: ({ row }) => (
        <RowLink href={`/bank-accounts/${row.original.bankAccountId}`}>
          {row.original.bankName}
        </RowLink>
      ),
    },
    { accessorKey: 'accountNo', header: t('accountNo'), meta: { filter: 'text' } },
    {
      accessorKey: 'accountName', header: t('accountName'),
      cell: ({ getValue }) => <span>{getValue<string | null>() ?? '—'}</span>,
    },
    {
      id: 'glCashAccount',
      header: t('glCashAccount'),
      cell: ({ row }) => (
        <span className="text-sm text-base-content/70">{accountLabel(row.original.glCashAccountId)}</span>
      ),
    },
    { accessorKey: 'currency', header: t('currency') },
    {
      accessorKey: 'isActive', header: tc('status'), meta: { filter: 'select' },
      cell: ({ getValue }) => (
        <span className={getValue<boolean>() ? 'text-status-success' : 'text-status-draft'}>
          {getValue<boolean>() ? tc('active') : tc('inactive')}
        </span>
      ),
    },
    {
      id: 'actions', header: '', enableSorting: false, meta: { align: 'right' },
      cell: ({ row }) => (
        <Link href={`/bank-accounts/${row.original.bankAccountId}`} className="link link-primary text-sm">
          {tc('view')}
        </Link>
      ),
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
  ], [t, tc, glAccounts.data]);

  return (
    <>
      <PageHeader
        title={t('title')}
        actions={
          <PermissionGate scope="bank.account.manage">
            <Link href="/bank-accounts/new" data-testid="bank-account-create" className="btn btn-primary btn-sm gap-1">
              <Plus className="h-4 w-4" aria-hidden /> {t('create')}
            </Link>
          </PermissionGate>
        }
      />
      <DataTable
        data={q.data ?? []}
        columns={columns}
        isLoading={q.isLoading}
        getRowId={(r) => String(r.bankAccountId)}
        searchPlaceholder={t('accountNo')}
      />
    </>
  );
}
