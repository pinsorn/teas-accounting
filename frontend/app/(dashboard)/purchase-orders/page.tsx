'use client';

import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { Plus } from 'lucide-react';
import { PageHeader } from '@/components/ui/PageHeader';
import { usePurchaseOrders } from '@/lib/queries';
import { formatTHB, formatDate } from '@/lib/utils';

export default function PurchaseOrdersPage() {
  const t = useTranslations('purchaseOrder');
  const tc = useTranslations('common');
  const q = usePurchaseOrders();
  const rows = q.data ?? [];
  return (
    <>
      <PageHeader title={t('title')} actions={
        <Link href="/purchase-orders/new" className="btn btn-primary btn-sm gap-1">
          <Plus className="h-4 w-4" aria-hidden /> {tc('save')}
        </Link>
      } />
      <div className="overflow-x-auto rounded-lg border border-base-300">
        <table className="table table-zebra">
          <thead><tr>
            <th>{t('docNo')}</th><th>{t('status')}</th><th>{t('vendor')}</th>
            <th>{t('expectedDelivery')}</th><th className="text-right">{t('total')}</th><th />
          </tr></thead>
          <tbody>
            {q.isLoading && (<tr><td colSpan={6} className="py-6 text-center text-base-content/50">{tc('loading')}</td></tr>)}
            {!q.isLoading && rows.length === 0 && (<tr><td colSpan={6} className="py-6 text-center text-base-content/50">{tc('empty')}</td></tr>)}
            {rows.map((r) => (
              <tr key={r.purchaseOrderId}>
                <td className="font-mono">{r.docNo ?? `#${r.purchaseOrderId}`}</td>
                <td><span className="badge badge-ghost">{r.status}</span></td>
                <td>{r.vendorName}</td>
                <td className="tabular-nums">{r.expectedDeliveryDate ? formatDate(r.expectedDeliveryDate) : '—'}</td>
                <td className="text-right tabular-nums">{formatTHB(r.totalAmount)}</td>
                <td className="text-right">
                  <Link href={`/purchase-orders/${r.purchaseOrderId}`} className="btn btn-ghost btn-xs">
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
