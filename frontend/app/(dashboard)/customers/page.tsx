'use client';

import Link from 'next/link';
import { useState } from 'react';
import { useTranslations } from 'next-intl';
import { Plus, Search } from 'lucide-react';
import { PageHeader } from '@/components/ui/PageHeader';
import { EmptyState } from '@/components/ui/EmptyState';
import { useCustomers } from '@/lib/queries';
import { formatTaxId } from '@/lib/utils';

// Sprint 13j-FE — Customer master list (sales). Mirrors /vendors, restyled with
// peach/ink tokens + mascot empty state. BE: GET /customers (CustomerEndpoints).
export default function CustomerListPage() {
  const t = useTranslations('cust');
  const tc = useTranslations('common');
  const [search, setSearch] = useState('');
  const q = useCustomers(search.trim() || undefined);
  const rows = q.data ?? [];

  return (
    <>
      <PageHeader
        title={t('title')}
        subtitle={t('subtitle')}
        actions={
          <Link href="/customers/new" className="btn btn-primary btn-sm gap-1">
            <Plus className="h-4 w-4" aria-hidden /> {t('create')}
          </Link>
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
        <div className="overflow-x-auto rounded-card border border-ink-100 bg-base-100 shadow-warm-sm">
          <table className="table">
            <thead>
              <tr className="border-ink-100 text-ink-500">
                <th>{t('code')}</th>
                <th>{t('nameTh')}</th>
                <th>{t('taxId')}</th>
                <th>{t('type')}</th>
                <th>VAT</th>
                <th>{tc('status')}</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {q.isLoading && (
                <tr><td colSpan={7} className="py-8 text-center text-ink-400">{tc('loading')}</td></tr>
              )}
              {rows.map((c) => (
                <tr key={c.customerId} className="hover:bg-base-200">
                  <td>
                    <Link href={`/customers/${c.customerId}`} className="font-mono font-semibold text-peach-700 hover:underline">
                      {c.customerCode}
                    </Link>
                  </td>
                  <td className="font-medium text-ink-900">
                    <Link href={`/customers/${c.customerId}`} className="hover:text-peach-700 hover:underline">
                      {c.nameTh}
                      {c.nameEn && <span className="ml-2 text-xs text-ink-400">{c.nameEn}</span>}
                    </Link>
                  </td>
                  <td className="font-mono tabular-nums text-ink-600">{formatTaxId(c.taxId)}</td>
                  <td className="text-ink-600">{c.customerType === 'Individual' ? t('individual') : t('corporate')}</td>
                  <td>
                    {c.vatRegistered ? (
                      <span className="rounded-full bg-status-success-bg px-2 py-0.5 text-xs font-semibold text-status-success">VAT</span>
                    ) : (
                      <span className="text-ink-300">—</span>
                    )}
                  </td>
                  <td>
                    {c.isActive ? (
                      <span className="inline-flex items-center gap-1.5 rounded-full bg-status-success-bg px-2.5 py-0.5 text-xs font-semibold text-status-success">
                        <span className="h-1.5 w-1.5 rounded-full bg-current" />{tc('active')}
                      </span>
                    ) : (
                      <span className="inline-flex items-center gap-1.5 rounded-full bg-status-draft-bg px-2.5 py-0.5 text-xs font-semibold text-status-draft">
                        <span className="h-1.5 w-1.5 rounded-full bg-current" />{tc('inactive')}
                      </span>
                    )}
                  </td>
                  <td className="text-right">
                    <Link href={`/customers/${c.customerId}`} className="btn btn-ghost btn-xs gap-1 text-peach-700">
                      {tc('view')}
                    </Link>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </>
  );
}
