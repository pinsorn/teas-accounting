'use client';

import Link from 'next/link';
import { useState } from 'react';
import { useTranslations } from 'next-intl';
import { Plus } from 'lucide-react';
import { PageHeader } from '@/components/ui/PageHeader';
import { useVendors } from '@/lib/queries';
import { formatTaxId } from '@/lib/utils';

export default function VendorListPage() {
  const t = useTranslations('ven');
  const tc = useTranslations('common');
  const [search, setSearch] = useState('');
  const q = useVendors(search.trim() || undefined);
  const rows = q.data ?? [];

  return (
    <>
      <PageHeader
        title={t('title')}
        actions={
          <Link href="/vendors/new" className="btn btn-primary btn-sm gap-1">
            <Plus className="h-4 w-4" aria-hidden /> {t('create')}
          </Link>
        }
      />
      <input
        className="input input-bordered input-sm mb-4 w-full max-w-sm"
        placeholder={tc('search')}
        value={search}
        onChange={(e) => setSearch(e.target.value)}
      />
      <div className="overflow-x-auto rounded-lg border border-base-300">
        <table className="table table-zebra">
          <thead>
            <tr>
              <th>{t('code')}</th><th>{t('nameTh')}</th><th>{t('taxId')}</th>
              <th>{t('type')}</th><th>{t('active')}</th>
            </tr>
          </thead>
          <tbody>
            {q.isLoading && (
              <tr><td colSpan={5} className="py-8 text-center text-base-content/50">{tc('loading')}</td></tr>
            )}
            {!q.isLoading && rows.length === 0 && (
              <tr><td colSpan={5} className="py-8 text-center text-base-content/50">{tc('empty')}</td></tr>
            )}
            {rows.map((v) => (
              <tr key={v.vendorId} className="hover">
                <td>
                  <Link href={`/vendors/${v.vendorId}`} className="link link-primary font-mono">
                    {v.vendorCode}
                  </Link>
                </td>
                <td>{v.nameTh}</td>
                <td className="font-mono tabular-nums">{formatTaxId(v.taxId)}</td>
                <td>{v.vendorType === 'Individual' ? t('individual') : t('corporate')}</td>
                <td>{v.isActive ? '✓' : '—'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </>
  );
}
