'use client';

import Link from 'next/link';
import { useParams } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { Download } from 'lucide-react';
import { PageHeader } from '@/components/ui/PageHeader';
import { useWhtCertificate } from '@/lib/queries';
import { downloadFile } from '@/lib/api';
import { formatTHB, formatDate, formatTaxId } from '@/lib/utils';

export default function WhtCertificateDetailPage() {
  const id = Number(useParams<{ id: string }>().id);
  const t = useTranslations('wht');
  const tc = useTranslations('common');
  const { data: d, isLoading, isError } = useWhtCertificate(id);

  if (isLoading) return <p className="text-base-content/50">{tc('loading')}</p>;
  if (isError || !d) return <p className="text-error">{tc('error')}</p>;

  return (
    <>
      <PageHeader
        title={t('title')}
        subtitle={d.docNo}
        actions={
          <button className="btn btn-ghost btn-sm gap-1"
            onClick={() => downloadFile(`wht-certificates/${id}/pdf`, `wht-50tawi-${id}.pdf`)}>
            <Download className="h-4 w-4" aria-hidden /> PDF (50 ทวิ)
          </button>
        }
      />
      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
        <div className="card bg-base-100 shadow-sm">
          <div className="card-body">
            <h3 className="font-semibold">{t('payer')}</h3>
            <p>{d.payerName}</p>
            <p className="font-mono text-sm">{formatTaxId(d.payerTaxId)} · {d.payerBranchCode}</p>
            <p className="text-sm text-base-content/70">{d.payerAddress}</p>
          </div>
        </div>
        <div className="card bg-base-100 shadow-sm">
          <div className="card-body">
            <h3 className="font-semibold">{t('payee')}</h3>
            <p>{d.payeeName}</p>
            <p className="font-mono text-sm">{formatTaxId(d.payeeTaxId)}</p>
            <p className="text-sm text-base-content/70">{d.payeeAddress}</p>
          </div>
        </div>
      </div>
      <div className="card mt-4 bg-base-100 shadow-sm">
        <div className="card-body">
          <p><b>{t('formType')}:</b> {d.formType} · {formatDate(d.certDate)}</p>
          <table className="table">
            <thead>
              <tr>
                <th>{t('incomeType')}</th>
                <th className="text-right">{t('incomeAmount')}</th>
                <th className="text-right">{t('rate')}</th>
                <th className="text-right">{t('whtAmount')}</th>
              </tr>
            </thead>
            <tbody>
              <tr>
                <td className="font-mono">
                  {d.incomeTypeCode}
                  {d.incomeDescription ? ` — ${d.incomeDescription}` : ''}
                </td>
                <td className="text-right tabular-nums">{formatTHB(d.incomeAmount)}</td>
                <td className="text-right tabular-nums">
                  {(d.whtRate * 100).toFixed(2)}%
                </td>
                <td className="text-right tabular-nums">{formatTHB(d.whtAmount)}</td>
              </tr>
            </tbody>
          </table>
          <p className="mt-2 text-sm">
            <b>{t('fromPv')}:</b>{' '}
            <Link href={`/payment-vouchers/${d.paymentVoucherId}`} className="link link-primary">
              PV #{d.paymentVoucherId}
            </Link>
          </p>
        </div>
      </div>
    </>
  );
}
