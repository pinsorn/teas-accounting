'use client';

import { useState } from 'react';
import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { PageHeader } from '@/components/ui/PageHeader';
import { useOutstandingPo } from '@/lib/queries';
import { formatTHB, formatDate } from '@/lib/utils';

function today() { return new Date().toISOString().slice(0, 10); }

export default function OutstandingPoPage() {
  const t = useTranslations('purchaseOrder');
  const tc = useTranslations('common');
  const [asOf, setAsOf] = useState(today());
  const [overdueOnly, setOverdueOnly] = useState(false);
  const q = useOutstandingPo(asOf, undefined, overdueOnly);
  const rows = q.data?.rows ?? [];

  return (
    <>
      <PageHeader title={t('outstandingReport')} />
      <div className="mb-4 flex flex-wrap items-end gap-3">
        <label className="form-control">
          <span className="label-text text-xs">{tc('to')}</span>
          <input type="date" className="input input-bordered input-sm"
            value={asOf} onChange={(e) => setAsOf(e.target.value)} />
        </label>
        <label className="label cursor-pointer gap-2">
          <input type="checkbox" className="checkbox checkbox-sm"
            checked={overdueOnly} onChange={(e) => setOverdueOnly(e.target.checked)} />
          <span className="label-text text-xs">{t('overdueOnly')}</span>
        </label>
      </div>
      <div className="overflow-x-auto rounded-lg border border-base-300">
        <table className="table table-zebra">
          <thead><tr>
            <th>{t('docNo')}</th><th>{t('vendor')}</th><th>{t('expectedDelivery')}</th>
            <th className="text-right">{t('overdueDays')}</th><th>{t('aging')}</th>
            <th className="text-right">{t('total')}</th>
            <th className="text-right">{t('remaining')}</th>
          </tr></thead>
          <tbody>
            {q.isLoading && (<tr><td colSpan={7} className="py-6 text-center text-base-content/50">{tc('loading')}</td></tr>)}
            {!q.isLoading && rows.length === 0 && (<tr><td colSpan={7} className="py-6 text-center text-base-content/50">{tc('empty')}</td></tr>)}
            {rows.map((r) => (
              <tr key={r.poId}>
                <td><Link className="link link-primary font-mono"
                  href={`/purchase-orders/${r.poId}`}>{r.docNo ?? `#${r.poId}`}</Link></td>
                <td>{r.vendorName}</td>
                <td className="tabular-nums">{r.expectedDeliveryDate ? formatDate(r.expectedDeliveryDate) : '—'}</td>
                <td className="text-right tabular-nums">{r.daysOverdue}</td>
                <td><span className={`badge badge-sm ${r.daysOverdue > 14 ? 'badge-error' : r.daysOverdue > 0 ? 'badge-warning' : 'badge-ghost'}`}>{r.agingBucket}</span></td>
                <td className="text-right tabular-nums">{formatTHB(r.poTotal)}</td>
                <td className="text-right tabular-nums">{formatTHB(r.remaining)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </>
  );
}
