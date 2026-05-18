'use client';

import { useState } from 'react';
import { useTranslations } from 'next-intl';
import { PageHeader } from '@/components/ui/PageHeader';
import { useWhtReceivableRegister, useWhtReceivableAging } from '@/lib/queries';
import { formatTHB, formatDate } from '@/lib/utils';

export default function WhtReceivablePage() {
  const t = useTranslations('whtReceivable');
  const tc = useTranslations('common');
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const reg = useWhtReceivableRegister(from, to);
  const aging = useWhtReceivableAging();

  return (
    <>
      <PageHeader title={t('title')} />

      <section className="mb-6">
        <h2 className="mb-2 font-semibold">{t('register')}</h2>
        <div className="mb-3 flex flex-wrap items-end gap-3">
          <label className="form-control">
            <span className="label-text text-xs">{t('from')}</span>
            <input type="date" className="input input-bordered input-sm"
              value={from} onChange={(e) => setFrom(e.target.value)} />
          </label>
          <label className="form-control">
            <span className="label-text text-xs">{t('to')}</span>
            <input type="date" className="input input-bordered input-sm"
              value={to} onChange={(e) => setTo(e.target.value)} />
          </label>
        </div>
        <div className="overflow-x-auto rounded-lg border border-base-300">
          <table className="table table-zebra">
            <thead>
              <tr>
                <th>{t('docNo')}</th><th>{t('date')}</th><th>{t('customer')}</th>
                <th>{t('taxId')}</th><th className="text-right">{t('amount')}</th>
                <th>{t('certNo')}</th>
              </tr>
            </thead>
            <tbody>
              {(!from || !to) && (
                <tr><td colSpan={6} className="py-6 text-center text-base-content/50">{t('from')} / {t('to')}…</td></tr>
              )}
              {from && to && reg.isLoading && (
                <tr><td colSpan={6} className="py-6 text-center text-base-content/50">{tc('loading')}</td></tr>
              )}
              {reg.data?.rows.length === 0 && (
                <tr><td colSpan={6} className="py-6 text-center text-base-content/50">{tc('empty')}</td></tr>
              )}
              {reg.data?.rows.map((r, i) => (
                <tr key={i}>
                  <td className="font-mono">{r.docNo}</td>
                  <td className="tabular-nums">{formatDate(r.docDate)}</td>
                  <td>{r.customerName}</td>
                  <td className="font-mono">{r.customerTaxId ?? '—'}</td>
                  <td className="text-right tabular-nums">{formatTHB(r.whtAmount)}</td>
                  <td>{r.customerWhtCertNo ?? '—'}</td>
                </tr>
              ))}
            </tbody>
            {reg.data && reg.data.rows.length > 0 && (
              <tfoot>
                <tr className="font-bold">
                  <td colSpan={4} className="text-right">{t('total')}</td>
                  <td className="text-right tabular-nums">{formatTHB(reg.data.totalWht)}</td>
                  <td />
                </tr>
              </tfoot>
            )}
          </table>
        </div>
      </section>

      <section>
        <h2 className="mb-2 font-semibold">{t('aging')}</h2>
        <div className="overflow-x-auto rounded-lg border border-base-300">
          <table className="table table-zebra">
            <thead>
              <tr>
                <th>{t('customer')}</th><th>{t('taxId')}</th><th>{t('docNo')}</th>
                <th>{t('date')}</th><th className="text-right">{t('amount')}</th>
                <th className="text-right">{t('ageDays')}</th>
              </tr>
            </thead>
            <tbody>
              {aging.isLoading && (
                <tr><td colSpan={6} className="py-6 text-center text-base-content/50">{tc('loading')}</td></tr>
              )}
              {aging.data?.rows.length === 0 && (
                <tr><td colSpan={6} className="py-6 text-center text-base-content/50">{tc('empty')}</td></tr>
              )}
              {aging.data?.rows.map((r, i) => (
                <tr key={i}>
                  <td>{r.customerName}</td>
                  <td className="font-mono">{r.customerTaxId ?? '—'}</td>
                  <td className="font-mono">{r.docNo}</td>
                  <td className="tabular-nums">{formatDate(r.docDate)}</td>
                  <td className="text-right tabular-nums">{formatTHB(r.whtAmount)}</td>
                  <td className="text-right tabular-nums">{r.ageDays}</td>
                </tr>
              ))}
            </tbody>
            {aging.data && aging.data.rows.length > 0 && (
              <tfoot>
                <tr className="font-bold">
                  <td colSpan={4} className="text-right">{t('total')}</td>
                  <td className="text-right tabular-nums">{formatTHB(aging.data.totalOutstanding)}</td>
                  <td />
                </tr>
              </tfoot>
            )}
          </table>
        </div>
      </section>
    </>
  );
}
