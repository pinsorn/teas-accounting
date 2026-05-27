'use client';

import { useTranslations } from 'next-intl';
import { PageHeader } from '@/components/ui/PageHeader';
import { useExpenseCategories } from '@/lib/queries';
import type { ExpenseCategoryLite } from '@/lib/types';
import { QueryState } from '@/components/states/QueryState';

// Sprint 13j-PURCH Phase F (F2) — read-only list of the seeded
// sys.expense_categories (the 19 reference rows used by VI/PV expense lines).
// Mirrors settings/wht-types structure; NO CRUD (reference data is seed-managed,
// changed via migration, not the UI).
export default function ExpenseCategoriesSettingsPage() {
  const t = useTranslations('expenseCategory');
  const q = useExpenseCategories();
  const rows = q.data ?? [];

  return (
    <>
      <PageHeader title={t('title')} />

      <QueryState query={q} isEmpty={!q.isLoading && rows.length === 0}>
        <div className="overflow-x-auto rounded-lg border border-base-300">
          <table className="table table-zebra">
            <thead>
              <tr>
                <th>{t('code')}</th>
                <th>{t('nameTh')}</th>
                <th>{t('recoverableVat')}</th>
                <th>{t('capex')}</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((c: ExpenseCategoryLite) => (
                <tr key={c.categoryId} className="hover">
                  <td className="font-mono">{c.categoryCode}</td>
                  <td>{c.nameTh}</td>
                  {/* BP-02 — bind the real BE field (defaultIsRecoverableVat) and
                      render ✓ / ✗ instead of the dead "—" the old binding produced. */}
                  <td className={c.defaultIsRecoverableVat ? 'text-success' : 'text-base-content/40'}>
                    {c.defaultIsRecoverableVat ? '✓' : '✗'}
                  </td>
                  <td className={c.isCapex ? 'text-success' : 'text-base-content/40'}>
                    {c.isCapex ? '✓' : '✗'}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </QueryState>
    </>
  );
}
