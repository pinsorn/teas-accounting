'use client';

import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { PageHeader } from '@/components/ui/PageHeader';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { useWhtCertificates } from '@/lib/queries';
import { formatTHB, formatDate } from '@/lib/utils';

export default function WhtCertificateListPage() {
  const t = useTranslations('wht');
  const tc = useTranslations('common');
  const q = useWhtCertificates();
  const rows = q.data?.pages.flatMap((p) => p.items) ?? [];

  return (
    <>
      <PageHeader title={t('title')} subtitle={t('subtitle')} />
      <div className="overflow-x-auto rounded-lg border border-base-300">
        <table className="table table-zebra">
          <thead>
            <tr>
              <th>No.</th><th>Date</th><th>{t('payee')}</th>
              <th>{t('formType')}</th><th>{t('incomeType')}</th>
              <th className="text-right">{t('whtAmount')}</th><th>Status</th>
            </tr>
          </thead>
          <tbody>
            {q.isLoading && (
              <tr><td colSpan={7} className="py-8 text-center text-base-content/50">{tc('loading')}</td></tr>
            )}
            {!q.isLoading && rows.length === 0 && (
              <tr><td colSpan={7} className="py-8 text-center text-base-content/50">{tc('empty')}</td></tr>
            )}
            {rows.map((r) => (
              <tr key={r.whtCertificateId} className="hover">
                <td>
                  <Link href={`/wht-certificates/${r.whtCertificateId}`}
                    className="link link-primary font-mono">{r.docNo}</Link>
                </td>
                <td className="tabular-nums">{formatDate(r.certDate)}</td>
                <td>{r.payeeName}</td>
                <td>{r.formType}</td>
                <td className="font-mono">{r.incomeTypeCode}</td>
                <td className="text-right tabular-nums">{formatTHB(r.whtAmount)}</td>
                <td><StatusBadge status={r.status} /></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {q.hasNextPage && (
        <div className="mt-4 text-center">
          <button className="btn btn-ghost btn-sm" onClick={() => q.fetchNextPage()}
            disabled={q.isFetchingNextPage}>
            {tc('loadMore')}
          </button>
        </div>
      )}
    </>
  );
}
