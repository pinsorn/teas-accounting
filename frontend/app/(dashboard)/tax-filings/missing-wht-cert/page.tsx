'use client';

import { useState } from 'react';
import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { PageHeader } from '@/components/ui/PageHeader';
import { useWhtMissingCert } from '@/lib/queries';
import { formatTHB } from '@/lib/utils';

function thisMonth() {
  return new Date().toISOString().slice(0, 7);
}

// Sprint 13j-tail — "ใบเสร็จที่ขาดใบทวิ 50": posted receipts that withheld WHT but
// have no customer 50ทวิ cert number yet. Filter by filing period (month); each
// row links to the receipt detail where the cert can be entered late.
export default function MissingWhtCertPage() {
  const t = useTranslations('tf');
  const [ym, setYm] = useState(thisMonth());
  const period = Number(ym.replace('-', ''));
  const { data, isLoading } = useWhtMissingCert(period);

  return (
    <>
      <PageHeader title={t('missingCertTitle')} />
      <p className="mb-4 text-sm text-base-content/70">{t('missingCertDesc')}</p>

      <div className="mb-4 flex flex-wrap items-end gap-3">
        <label className="form-control">
          <span className="label-text text-xs">{t('period')}</span>
          <input type="month" className="input input-bordered input-sm"
            value={ym} onChange={(e) => setYm(e.target.value)} />
        </label>
        {data && (
          <span data-testid="missing-cert-count" className="badge badge-ghost">
            {t('missingCertCount', { count: data.rows.length })}
          </span>
        )}
      </div>

      <div className="overflow-x-auto rounded-lg border border-base-300">
        <table className="table table-zebra">
          <thead>
            <tr>
              <th>{t('docNo')}</th>
              <th>{t('docDate')}</th>
              <th>{t('customer')}</th>
              <th>{t('customerTaxId')}</th>
              <th className="text-right">{t('whtAmount')}</th>
            </tr>
          </thead>
          <tbody>
            {isLoading && (
              <tr><td colSpan={5} className="py-6 text-center text-base-content/50">…</td></tr>
            )}
            {!isLoading && data && data.rows.length === 0 && (
              <tr><td colSpan={5} className="py-6 text-center text-base-content/50">
                {t('missingCertEmpty')}
              </td></tr>
            )}
            {data?.rows.map((r) => (
              <tr key={r.receiptId}>
                <td className="font-mono">
                  <Link href={`/receipts/${r.receiptId}`} className="link link-primary">
                    {r.docNo}
                  </Link>
                </td>
                <td>{r.docDate}</td>
                <td>{r.customerName}</td>
                <td className="font-mono text-xs">{r.customerTaxId ?? '—'}</td>
                <td className="text-right tabular-nums">{formatTHB(r.whtAmount)}</td>
              </tr>
            ))}
          </tbody>
          {data && data.rows.length > 0 && (
            <tfoot>
              <tr className="font-bold">
                <td colSpan={4} className="text-right">{t('total')}</td>
                <td className="text-right tabular-nums">{formatTHB(data.totalWht)}</td>
              </tr>
            </tfoot>
          )}
        </table>
      </div>
    </>
  );
}
