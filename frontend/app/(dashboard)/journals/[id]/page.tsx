'use client';

import type { ReactNode } from 'react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { PageHeader } from '@/components/ui/PageHeader';
import { useJournal } from '@/lib/queries';
import { formatTHB, formatDate } from '@/lib/utils';

export default function JournalDetailPage() {
  const id = Number(useParams<{ id: string }>().id);
  const t = useTranslations('je');
  const tc = useTranslations('common');
  const { data: d, isLoading } = useJournal(id);

  if (isLoading) return <p className="text-base-content/50">{tc('loading')}</p>;
  // 404 (not found OR other-tenant, identical per BE) — same handling as other doc detail pages.
  if (!d) return <p className="text-base-content/50">{tc('notFound')}</p>;

  return (
    <>
      <PageHeader title={t('title')} subtitle={d.docNo ?? undefined} />

      <div className="card mb-4 bg-base-100 shadow-sm">
        <div className="card-body grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
          <Field label={t('docDate')} value={formatDate(d.docDate)} />
          <Field label={t('postingDate')} value={formatDate(d.postingDate)} />
          <Field label={t('status')}>
            <span className={`badge ${d.status === 'Posted' ? 'badge-success' : 'badge-ghost'}`}>
              {d.status}
            </span>
          </Field>
          <Field label={t('description')} value={d.description} />
          <Field label={t('reference')} value={d.reference ?? '—'} />
          <Field label={t('postedAt')} value={d.postedAt ? formatDate(d.postedAt) : '—'} />
          {d.reversalOfId != null && (
            <Field label={t('reversalOf')}>
              <Link href={`/journals/${d.reversalOfId}`} className="link link-primary">
                #{d.reversalOfId}
              </Link>
            </Field>
          )}
        </div>
      </div>

      <div className="overflow-x-auto rounded-lg border border-base-300">
        <table className="table table-zebra">
          <thead>
            <tr>
              <th>{t('account')}</th>
              <th>{t('description')}</th>
              <th className="text-right">{t('debit')}</th>
              <th className="text-right">{t('credit')}</th>
            </tr>
          </thead>
          <tbody>
            {d.lines.map((l) => (
              <tr key={l.lineNo}>
                <td><span className="font-mono">{l.accountCode}</span> {l.accountNameTh}</td>
                <td>{l.description ?? d.description}</td>
                <td className="text-right tabular-nums">{l.debit ? formatTHB(l.debit) : ''}</td>
                <td className="text-right tabular-nums">{l.credit ? formatTHB(l.credit) : ''}</td>
              </tr>
            ))}
          </tbody>
          <tfoot>
            <tr className="font-bold">
              <td colSpan={2} className="text-right">{t('totalRow')}</td>
              <td className="text-right tabular-nums">{formatTHB(d.totalDebit)}</td>
              <td className="text-right tabular-nums">{formatTHB(d.totalCredit)}</td>
            </tr>
          </tfoot>
        </table>
      </div>
    </>
  );
}

function Field({ label, value, children }: { label: string; value?: string; children?: ReactNode }) {
  return (
    <div>
      <div className="text-xs text-base-content/60">{label}</div>
      <div className="font-medium">{children ?? value}</div>
    </div>
  );
}
