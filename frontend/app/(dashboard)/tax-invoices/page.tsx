'use client';

import Link from 'next/link';
import { useSearchParams } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { Plus } from 'lucide-react';
import { PageHeader } from '@/components/ui/PageHeader';
import { QueryStateRow } from '@/components/states/QueryState';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { DocumentNumberBadge } from '@/components/ui/DocumentNumberBadge';
import { ListFilters } from '@/components/ui/ListFilters';
import { useTaxInvoices, useSystemInfo, type TaxInvoiceFilters } from '@/lib/queries';
import { NonVatGuard } from '@/components/ui/NonVatGuard';
import { formatTHB, formatDate } from '@/lib/utils';

// Sprint 13i C3 — TI list keeps its server-side (paginated) filtering but is now
// URL-driven (status + BU + customer + date range) via the shared <ListFilters>.
const TI_STATUSES = ['Draft', 'Posted', 'Voided'] as const;

export default function TaxInvoiceListPage() {
  const t = useTranslations('ti');
  const tc = useTranslations('common');
  const params = useSearchParams();

  const filters: TaxInvoiceFilters = {
    status: params.get('status') || undefined,
    businessUnitId: params.get('bu') ? Number(params.get('bu')) : undefined,
    customerId: params.get('customerId') ? Number(params.get('customerId')) : undefined,
    dateFrom: params.get('dateFrom') || undefined,
    dateTo: params.get('dateTo') || undefined,
  };

  const q = useTaxInvoices(filters);
  const rows = q.data?.pages.flatMap((p) => p.items) ?? [];
  // ม.86/4 — a non-VAT company cannot issue Tax Invoices (and never had any), so the
  // whole page is hidden. Nav also hides it; this guards direct URL access.
  const vatMode = useSystemInfo().data?.vatMode ?? true;
  if (!vatMode) return <NonVatGuard title={t('title')} />;

  return (
    <>
      <PageHeader
        title={t('title')}
        actions={
          vatMode ? (
            <Link href="/tax-invoices/new" className="btn btn-primary btn-sm gap-1">
              <Plus className="h-4 w-4" aria-hidden /> {t('create')}
            </Link>
          ) : null
        }
      />

      <ListFilters statusOptions={TI_STATUSES} statusTestId="ti-filter-status" />

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
              <th className="text-right" />
            </tr>
          </thead>
          <tbody>
            <QueryStateRow query={q} colSpan={8} isEmpty={rows.length === 0} />
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
                <td className="text-right">
                  <Link href={`/tax-invoices/${r.taxInvoiceId}`} className="link link-primary text-sm">
                    {tc('view')}
                  </Link>
                </td>
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
