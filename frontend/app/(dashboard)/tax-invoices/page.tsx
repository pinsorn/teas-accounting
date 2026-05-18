'use client';

import { useState } from 'react';
import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { Plus } from 'lucide-react';
import { PageHeader } from '@/components/ui/PageHeader';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { DocumentNumberBadge } from '@/components/ui/DocumentNumberBadge';
import { useTaxInvoices, useBusinessUnits, type TaxInvoiceFilters } from '@/lib/queries';
import { formatTHB, formatDate } from '@/lib/utils';

export default function TaxInvoiceListPage() {
  const t = useTranslations('ti');
  const tb = useTranslations('businessUnit');
  const tc = useTranslations('common');
  const [filters, setFilters] = useState<TaxInvoiceFilters>({});
  const { data: bus = [] } = useBusinessUnits();

  const q = useTaxInvoices(filters);
  const rows = q.data?.pages.flatMap((p) => p.items) ?? [];

  return (
    <>
      <PageHeader
        title={t('title')}
        actions={
          <Link href="/tax-invoices/new" className="btn btn-primary btn-sm gap-1">
            <Plus className="h-4 w-4" aria-hidden /> {t('create')}
          </Link>
        }
      />

      {/* Filter chips */}
      <div className="mb-4 flex flex-wrap items-end gap-3">
        <label className="form-control">
          <span className="label-text text-xs">{tc('from')}</span>
          <input
            type="date"
            className="input input-bordered input-sm"
            onChange={(e) => setFilters((f) => ({ ...f, dateFrom: e.target.value || undefined }))}
          />
        </label>
        <label className="form-control">
          <span className="label-text text-xs">{tc('to')}</span>
          <input
            type="date"
            className="input input-bordered input-sm"
            onChange={(e) => setFilters((f) => ({ ...f, dateTo: e.target.value || undefined }))}
          />
        </label>
        <label className="form-control">
          <span className="label-text text-xs">{t('list.status')}</span>
          <select
            className="select select-bordered select-sm"
            onChange={(e) => setFilters((f) => ({ ...f, status: e.target.value || undefined }))}
          >
            <option value="">{tc('all')}</option>
            <option value="Draft">Draft</option>
            <option value="Posted">Posted</option>
            <option value="Voided">Voided</option>
          </select>
        </label>
        <label className="form-control">
          <span className="label-text text-xs">{tb('filter')}</span>
          <select
            className="select select-bordered select-sm"
            aria-label={tb('filter')}
            onChange={(e) => setFilters((f) => ({
              ...f, businessUnitId: e.target.value ? Number(e.target.value) : undefined,
            }))}
          >
            <option value="">{tc('all')}</option>
            {bus.map((u) => (
              <option key={u.businessUnitId} value={u.businessUnitId}>{u.code}</option>
            ))}
          </select>
        </label>
        <label className="label cursor-pointer gap-2 self-end">
          <input
            type="checkbox"
            className="checkbox checkbox-sm"
            onChange={(e) => setFilters((f) => ({
              ...f, includeUnspecified: e.target.checked || undefined,
            }))}
          />
          <span className="label-text text-xs">{tb('includeUnspecified')}</span>
        </label>
      </div>

      <div className="overflow-x-auto rounded-lg border border-base-300">
        <table className="table table-zebra">
          <thead>
            <tr>
              <th>{t('list.docNo')}</th>
              <th>{t('list.docDate')}</th>
              <th>{t('list.customer')}</th>
              <th className="text-right">{t('list.total')}</th>
              <th className="text-right">{t('list.vat')}</th>
              <th>{t('list.status')}</th>
              <th>{t('list.payment')}</th>
            </tr>
          </thead>
          <tbody>
            {q.isLoading && (
              <tr><td colSpan={7} className="py-8 text-center text-base-content/50">{tc('loading')}</td></tr>
            )}
            {q.isError && (
              <tr><td colSpan={7} className="py-8 text-center text-error">{tc('error')}</td></tr>
            )}
            {!q.isLoading && rows.length === 0 && (
              <tr><td colSpan={7} className="py-8 text-center text-base-content/50">{tc('empty')}</td></tr>
            )}
            {rows.map((r) => (
              <tr key={r.taxInvoiceId} className="hover">
                <td>
                  <Link href={`/tax-invoices/${r.taxInvoiceId}`} className="hover:text-primary">
                    <DocumentNumberBadge value={r.docNo} />
                  </Link>
                </td>
                <td className="tabular-nums">{formatDate(r.docDate)}</td>
                <td>{r.customerName}</td>
                <td className="text-right tabular-nums">{formatTHB(r.totalAmount)}</td>
                <td className="text-right tabular-nums">{formatTHB(r.taxAmount)}</td>
                <td><StatusBadge status={r.status} /></td>
                <td><StatusBadge status={r.paymentStatus} /></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {q.hasNextPage && (
        <div className="mt-4 text-center">
          <button
            className="btn btn-ghost btn-sm"
            onClick={() => q.fetchNextPage()}
            disabled={q.isFetchingNextPage}
          >
            {q.isFetchingNextPage ? tc('loading') : tc('loadMore')}
          </button>
        </div>
      )}
    </>
  );
}
