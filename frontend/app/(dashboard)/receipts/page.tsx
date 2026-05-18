'use client';

import { useState } from 'react';
import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { Plus } from 'lucide-react';
import { PageHeader } from '@/components/ui/PageHeader';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { DocumentNumberBadge } from '@/components/ui/DocumentNumberBadge';
import { useReceipts, useBusinessUnits } from '@/lib/queries';
import { formatTHB, formatDate } from '@/lib/utils';

export default function ReceiptListPage() {
  const t = useTranslations('rc');
  const tc = useTranslations('common');
  const tb = useTranslations('businessUnit');
  const [buId, setBuId] = useState<number | undefined>();
  const [includeUnspec, setIncludeUnspec] = useState(false);
  const { data: bus = [] } = useBusinessUnits();
  const q = useReceipts(buId, includeUnspec || undefined);
  const rows = q.data?.pages.flatMap((p) => p.items) ?? [];

  return (
    <>
      <PageHeader
        title={t('title')}
        actions={
          <Link href="/receipts/new" className="btn btn-primary btn-sm gap-1">
            <Plus className="h-4 w-4" aria-hidden /> {t('create')}
          </Link>
        }
      />
      <div className="mb-4 flex flex-wrap items-end gap-3">
        <label className="form-control">
          <span className="label-text text-xs">{tb('filter')}</span>
          <select
            className="select select-bordered select-sm"
            aria-label={tb('filter')}
            value={buId ?? ''}
            onChange={(e) => setBuId(e.target.value ? Number(e.target.value) : undefined)}
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
            checked={includeUnspec}
            onChange={(e) => setIncludeUnspec(e.target.checked)}
          />
          <span className="label-text text-xs">{tb('includeUnspecified')}</span>
        </label>
      </div>
      <div className="overflow-x-auto rounded-lg border border-base-300">
        <table className="table table-zebra">
          <thead>
            <tr><th>No.</th><th>Date</th><th>Customer</th><th className="text-right">Amount</th><th className="text-right">{t('wht.column')}</th><th>Status</th></tr>
          </thead>
          <tbody>
            {q.isLoading && <tr><td colSpan={6} className="py-8 text-center text-base-content/50">{tc('loading')}</td></tr>}
            {!q.isLoading && rows.length === 0 && <tr><td colSpan={6} className="py-8 text-center text-base-content/50">{tc('empty')}</td></tr>}
            {rows.map((r) => (
              <tr key={r.receiptId} className="hover">
                <td><Link href={`/receipts/${r.receiptId}`}><DocumentNumberBadge value={r.docNo} /></Link></td>
                <td className="tabular-nums">{formatDate(r.docDate)}</td>
                <td>{r.customerName}</td>
                <td className="text-right tabular-nums">{formatTHB(r.amount)}</td>
                <td className="text-right tabular-nums">{r.whtAmount > 0 ? formatTHB(r.whtAmount) : '—'}</td>
                <td><StatusBadge status={r.status} /></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {q.hasNextPage && (
        <div className="mt-4 text-center">
          <button className="btn btn-ghost btn-sm" onClick={() => q.fetchNextPage()} disabled={q.isFetchingNextPage}>
            {tc('loadMore')}
          </button>
        </div>
      )}
    </>
  );
}
