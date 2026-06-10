'use client';

import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { PageHeader } from '@/components/ui/PageHeader';
import { useTaxFilings, useSystemInfo } from '@/lib/queries';

// vatOnly: ภ.พ.30 (VAT return) is filed only by VAT registrants. ภ.ง.ด.3/53/54 (WHT)
// and ภ.พ.36 (reverse charge on imported services, ม.83/6) apply to non-VAT too.
const FORMS = [
  { href: '/reports/pnd30', code: 'PND30', vatOnly: true },
  { href: '/tax-filings/pnd3', code: 'PND3' },
  { href: '/tax-filings/pnd53', code: 'PND53' },
  { href: '/tax-filings/pnd54', code: 'PND54' },
  { href: '/tax-filings/pnd36', code: 'PND36' },
  { href: '/tax-filings/pnd51', code: 'PND51' },
] as const;

export default function TaxFilingsIndexPage() {
  const t = useTranslations('tf');
  const tc = useTranslations('common');
  const tcit = useTranslations('cit');
  const hist = useTaxFilings();
  const vatMode = useSystemInfo().data?.vatMode ?? true;

  return (
    <>
      <PageHeader title={t('indexTitle')} />

      <div className="mb-6 flex flex-wrap gap-2">
        {FORMS.filter((f) => !('vatOnly' in f && f.vatOnly) || vatMode).map((f) => (
          <Link key={f.code} href={f.href}
            className="btn btn-sm btn-outline">{f.code}</Link>
        ))}
        {/* Phase C-C — CIT yearly data (SME profile, ม.65ทวิ/65ตรี adjustments, loss c/f) */}
        <Link href="/tax-filings/cit" className="btn btn-sm btn-outline">{tcit('indexLink')}</Link>
      </div>

      <h2 className="mb-2 font-semibold">{t('history')}</h2>
      <div className="overflow-x-auto rounded-lg border border-base-300">
        <table className="table table-zebra">
          <thead>
            <tr>
              <th>{t('form')}</th><th>{t('period')}</th><th>{t('status')}</th>
              <th>{t('finalizedAt')}</th><th>{t('submission')}</th><th>{t('ackRef')}</th>
            </tr>
          </thead>
          <tbody>
            {hist.isLoading && (
              <tr><td colSpan={6} className="py-6 text-center text-base-content/50">{tc('loading')}</td></tr>
            )}
            {hist.data?.length === 0 && (
              <tr><td colSpan={6} className="py-6 text-center text-base-content/50">{tc('empty')}</td></tr>
            )}
            {hist.data?.map((h) => (
              <tr key={h.filingId}>
                <td className="font-semibold">{h.formType}</td>
                <td className="tabular-nums">{h.period}</td>
                <td>
                  <span className={`badge ${h.status === 'Finalized' || h.status === 'Submitted'
                    ? 'badge-success' : 'badge-ghost'}`}>{h.status}</span>
                </td>
                <td className="tabular-nums">{h.finalizedAt?.slice(0, 10) ?? '—'}</td>
                <td>{h.submissionMode ?? '—'}</td>
                <td className="font-mono text-xs">{h.rdAckRef ?? '—'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </>
  );
}
