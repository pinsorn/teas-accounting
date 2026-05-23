'use client';

import Link from 'next/link';
import { useSearchParams } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { Plus } from 'lucide-react';
import { PageHeader } from '@/components/ui/PageHeader';
import { QueryStateRow } from '@/components/states/QueryState';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { ListFilters } from '@/components/ui/ListFilters';
import { applyListFilters } from '@/lib/list-filter';
import { useBillingNotes } from '@/lib/queries';
import { formatTHB, formatDate } from '@/lib/utils';

// Sprint 13h P6.2 / 13i C3 — Billing Note list with status + BU + customer +
// date-range filters, URL-persisted (refresh + share-link safe).
const BN_STATUSES = ['Draft', 'Issued', 'Settled', 'Cancelled'] as const;

export default function BillingNotesPage() {
  const t = useTranslations('billingNote');
  const tc = useTranslations('common');
  const params = useSearchParams();
  const q = useBillingNotes();
  const rows = applyListFilters(q.data ?? [], params, {
    status: (r) => r.status,
    businessUnitId: (r) => r.businessUnitId,
    customerId: (r) => r.customerId,
    docDate: (r) => r.docDate,
  });

  return (
    <>
      <PageHeader title={t('listTitle')} actions={
        <Link href="/invoices/new" className="btn btn-primary btn-sm gap-1">
          <Plus className="h-4 w-4" aria-hidden /> {t('create')}
        </Link>
      } />
      <ListFilters statusOptions={BN_STATUSES} statusTestId="bn-filter-status" />
      <div className="overflow-x-auto rounded-lg border border-base-300">
        <table className="table table-zebra">
          <thead><tr>
            <th>{t('docNo')}</th><th>{tc('status')}</th><th>{t('customer')}</th>
            <th>{t('docDate')}</th><th>{t('dueDate')}</th>
            <th className="text-right">{t('total')}</th><th />
          </tr></thead>
          <tbody>
            <QueryStateRow query={q} colSpan={7} isEmpty={rows.length === 0} />
            {rows.map((r) => (
              <tr key={r.billingNoteId}>
                <td className="font-mono">{r.docNo ?? `#${r.billingNoteId}`}</td>
                <td><StatusBadge status={r.status} /></td>
                <td>{r.customerName}</td>
                <td className="tabular-nums">{formatDate(r.docDate)}</td>
                <td className="tabular-nums">{formatDate(r.dueDate)}</td>
                <td className="text-right tabular-nums">{formatTHB(r.totalAmount)}</td>
                <td className="text-right">
                  <Link href={`/invoices/${r.billingNoteId}`} className="btn btn-ghost btn-xs">
                    {tc('view')}
                  </Link>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </>
  );
}
