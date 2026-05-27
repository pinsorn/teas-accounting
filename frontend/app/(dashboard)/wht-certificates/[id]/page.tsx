'use client';

import Link from 'next/link';
import { useParams } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { PageHeader } from '@/components/ui/PageHeader';
import { PrintMenu } from '@/components/ui/PrintMenu';
import { useWhtCertificate } from '@/lib/queries';
import { formatTHB, formatDate, formatTaxId } from '@/lib/utils';
import { PurchaseDocumentChain } from '@/components/doc/PurchaseDocumentChain';

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
          // Sprint 13j-PURCH D3 — print/download the bespoke 50ทวิ PDF. Untracked:
          // WHT keeps its own ภ.ง.ด.50ทวิ endpoint (no ?copy / mark-printed).
          <PrintMenu docType="wht-certificates" id={id} tracked={false} />
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
      {/* Sprint 13j-PURCH D-supplement — FE chain panel (PO → VI → PV → WHT). */}
      <div className="mt-4">
        <PurchaseDocumentChain type="wht-certificate" id={id} />
      </div>
    </>
  );
}
