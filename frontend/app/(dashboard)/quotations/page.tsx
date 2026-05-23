'use client';

import Link from 'next/link';
import { useSearchParams } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { Plus } from 'lucide-react';
import { PageHeader } from '@/components/ui/PageHeader';
import { QueryStateRow } from '@/components/states/QueryState';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { ListFilters } from '@/components/ui/ListFilters';
import { EmptyState } from '@/components/ui/EmptyState';
import { applyListFilters } from '@/lib/list-filter';
import { useQuotations } from '@/lib/queries';
import { formatTHB, formatDate } from '@/lib/utils';

// Sprint 13i C3 — Q list gains status + BU + customer + date filters, URL-persisted.
const Q_STATUSES = ['Draft', 'Sent', 'Accepted', 'Rejected', 'Converted', 'Cancelled'] as const;

export default function QuotationsPage() {
  const t = useTranslations('quotation');
  const tc = useTranslations('common');
  const params = useSearchParams();
  const q = useQuotations();
  const rows = applyListFilters(q.data ?? [], params, {
    status: (r) => r.status,
    businessUnitId: (r) => r.businessUnitId,
    customerId: (r) => r.customerId,
    docDate: (r) => r.docDate,
  });

  return (
    <>
      <PageHeader title={t('listTitle')} actions={
        <Link href="/quotations/new" className="btn btn-primary btn-sm gap-1">
          <Plus className="h-4 w-4" aria-hidden /> {t('create')}
        </Link>
      } />
      <ListFilters statusOptions={Q_STATUSES} statusTestId="q-filter-status" />
      {q.isSuccess && rows.length === 0 ? (
        <EmptyState
          title={t('listTitle')}
          description={tc('empty')}
          cta={{ label: t('create'), href: '/quotations/new' }}
        />
      ) : (
      <div className="overflow-x-auto rounded-lg border border-base-300">
        <table className="table table-zebra">
          <thead><tr>
            <th>{t('docNo')}</th><th>{tc('status')}</th><th>{t('customer')}</th>
            <th>{t('docDate')}</th><th className="text-right">{t('total')}</th><th />
          </tr></thead>
          <tbody>
            <QueryStateRow query={q} colSpan={6} isEmpty={rows.length === 0} />
            {rows.map((r) => (
              <tr key={r.quotationId}>
                <td className="font-mono">{r.docNo ?? `#${r.quotationId}`}</td>
                <td><StatusBadge status={r.status} /></td>
                <td>{r.customerName}</td>
                <td className="tabular-nums">{formatDate(r.docDate)}</td>
                <td className="text-right tabular-nums">{formatTHB(r.totalAmount)}</td>
                <td className="text-right">
                  {r.status === 'Draft' ? (
                    <span className="inline-flex gap-1">
                      <Link href={`/quotations/${r.quotationId}`} className="btn btn-ghost btn-xs">
                        {tc('view')}
                      </Link>
                      <Link href={`/quotations/${r.quotationId}/edit`} className="btn btn-ghost btn-xs">
                        {tc('edit')}
                      </Link>
                    </span>
                  ) : (
                    <Link href={`/quotations/${r.quotationId}`} className="btn btn-ghost btn-xs">
                      {tc('view')}
                    </Link>
                  )}
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
