'use client';

import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { PageHeader } from '@/components/ui/PageHeader';
import { useSalesOrders } from '@/lib/queries';
import { formatTHB, formatDate } from '@/lib/utils';

export default function SalesOrdersPage() {
  const t = useTranslations('salesOrder');
  const tc = useTranslations('common');
  const q = useSalesOrders();
  const rows = q.data ?? [];
  return (
    <>
      <PageHeader title={t('listTitle')} />
      <div className="overflow-x-auto rounded-lg border border-base-300">
        <table className="table table-zebra">
          <thead><tr>
            <th>{t('docNo')}</th><th>{tc('status')}</th><th>{t('customer')}</th>
            <th>{t('docDate')}</th><th className="text-right">{t('total')}</th><th />
          </tr></thead>
          <tbody>
            {q.isLoading && (<tr><td colSpan={6} className="py-6 text-center text-base-content/50">{tc('loading')}</td></tr>)}
            {!q.isLoading && rows.length === 0 && (<tr><td colSpan={6} className="py-6 text-center text-base-content/50">{tc('empty')}</td></tr>)}
            {rows.map((r) => (
              <tr key={r.salesOrderId}>
                <td className="font-mono">{r.docNo ?? `#${r.salesOrderId}`}</td>
                <td><span className="badge badge-ghost">{r.status}</span></td>
                <td>{r.customerName}</td>
                <td className="tabular-nums">{formatDate(r.docDate)}</td>
                <td className="text-right tabular-nums">{formatTHB(r.totalAmount)}</td>
                <td className="text-right">
                  <Link href={`/sales-orders/${r.salesOrderId}`} className="btn btn-ghost btn-xs">
                    {tc('edit')}
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
