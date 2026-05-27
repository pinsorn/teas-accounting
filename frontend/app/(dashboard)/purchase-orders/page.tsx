'use client';

import Link from 'next/link';
import { useSearchParams } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { Plus } from 'lucide-react';
import { PageHeader } from '@/components/ui/PageHeader';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { ListFilters } from '@/components/ui/ListFilters';
import { EmptyState } from '@/components/ui/EmptyState';
import { applyListFilters } from '@/lib/list-filter';
import { usePurchaseOrders } from '@/lib/queries';
import { formatTHB, formatDate } from '@/lib/utils';

// Sprint 13j-PURCH D5 — PO statuses for the status filter chip.
const PO_STATUSES = ['Draft', 'Approved', 'Sent', 'Closed', 'Cancelled'] as const;

export default function PurchaseOrdersPage() {
  const t = useTranslations('purchaseOrder');
  const tc = useTranslations('common');
  const params = useSearchParams();
  const q = usePurchaseOrders();
  const rows = applyListFilters(q.data ?? [], params, {
    status: (r) => r.status,
    docDate: (r) => r.docDate,
  });

  return (
    <>
      <PageHeader title={t('listTitle')} actions={
        <Link href="/purchase-orders/new" className="btn btn-primary btn-sm gap-1">
          <Plus className="h-4 w-4" aria-hidden /> {t('create')}
        </Link>
      } />
      <ListFilters statusOptions={PO_STATUSES} statusTestId="po-filter-status" party="vendor" />
      {q.isSuccess && rows.length === 0 ? (
        <EmptyState
          title={t('listTitle')}
          description={tc('empty')}
          cta={{ label: t('create'), href: '/purchase-orders/new' }}
        />
      ) : (
      <div className="overflow-x-auto rounded-lg border border-base-300">
        <table className="table table-zebra">
          <thead><tr>
            <th>{t('docNo')}</th><th>{tc('status')}</th><th>{t('vendor')}</th>
            <th>{t('expectedDelivery')}</th><th className="text-right">{t('total')}</th><th />
          </tr></thead>
          <tbody>
            {q.isLoading && (<tr><td colSpan={6} className="py-6 text-center text-base-content/50">{tc('loading')}</td></tr>)}
            {rows.map((r) => (
              <tr key={r.purchaseOrderId}>
                <td className="font-mono">{r.docNo ?? `#${r.purchaseOrderId}`}</td>
                <td><StatusBadge status={r.status} /></td>
                <td>{r.vendorName}</td>
                <td className="tabular-nums">{r.expectedDeliveryDate ? formatDate(r.expectedDeliveryDate) : '—'}</td>
                <td className="text-right tabular-nums">{formatTHB(r.totalAmount)}</td>
                <td className="text-right">
                  <Link href={`/purchase-orders/${r.purchaseOrderId}`} className="btn btn-ghost btn-xs">
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
