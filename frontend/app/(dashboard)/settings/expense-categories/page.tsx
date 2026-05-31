'use client';

import { useMemo } from 'react';
import { useTranslations } from 'next-intl';
import type { ColumnDef } from '@tanstack/react-table';
import { PageHeader } from '@/components/ui/PageHeader';
import { DataTable } from '@/components/ui/DataTable';
import { useExpenseCategories } from '@/lib/queries';
import type { ExpenseCategoryLite } from '@/lib/types';
import { QueryState } from '@/components/states/QueryState';

// Sprint 13j-PURCH Phase F (F2) — read-only list of the seeded
// sys.expense_categories (the 19 reference rows used by VI/PV expense lines).
// cont.82 — rebuilt on the shared <DataTable>. NO CRUD (reference data is
// seed-managed, changed via migration, not the UI).
export default function ExpenseCategoriesSettingsPage() {
  const t = useTranslations('expenseCategory');
  const q = useExpenseCategories();
  const rows = q.data ?? [];

  const columns = useMemo<ColumnDef<ExpenseCategoryLite>[]>(() => [
    {
      accessorKey: 'categoryCode', header: t('code'),
      cell: ({ getValue }) => <span className="font-mono">{getValue<string>()}</span>,
    },
    { accessorKey: 'nameTh', header: t('nameTh') },
    {
      // BP-02 — bind the real BE field (defaultIsRecoverableVat) and render ✓ / ✗.
      accessorKey: 'defaultIsRecoverableVat', header: t('recoverableVat'),
      cell: ({ getValue }) => {
        const v = getValue<boolean>();
        return <span className={v ? 'text-success' : 'text-base-content/40'}>{v ? '✓' : '✗'}</span>;
      },
    },
    {
      accessorKey: 'isCapex', header: t('capex'),
      cell: ({ getValue }) => {
        const v = getValue<boolean>();
        return <span className={v ? 'text-success' : 'text-base-content/40'}>{v ? '✓' : '✗'}</span>;
      },
    },
  ], [t]);

  return (
    <>
      <PageHeader title={t('title')} />

      <QueryState query={q} isEmpty={!q.isLoading && rows.length === 0}>
        <DataTable
          data={rows}
          columns={columns}
          isLoading={q.isLoading}
          getRowId={(r) => String(r.categoryId)}
          searchPlaceholder={t('code')}
        />
      </QueryState>
    </>
  );
}
