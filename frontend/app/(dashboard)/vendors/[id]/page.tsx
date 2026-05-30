'use client';

import Link from 'next/link';
import { useParams } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { Pencil } from 'lucide-react';
import { PageHeader } from '@/components/ui/PageHeader';
import { useVendor } from '@/lib/queries';
import { formatTaxId } from '@/lib/utils';

export default function VendorDetailPage() {
  const id = Number(useParams<{ id: string }>().id);
  const t = useTranslations('ven');
  const tc = useTranslations('common');
  const { data: d, isLoading, isError } = useVendor(id);

  if (isLoading) return <p className="text-base-content/50">{tc('loading')}</p>;
  if (isError || !d) return <p className="text-error">{tc('error')}</p>;

  const Row = ({ k, v }: { k: string; v: React.ReactNode }) => (
    <p><b>{k}:</b> {v || '—'}</p>
  );

  return (
    <>
      <PageHeader title={d.nameTh} subtitle={d.vendorCode}
        actions={
          <Link href={`/vendors/${id}/edit`} className="btn btn-sm btn-primary">
            <Pencil className="h-4 w-4" aria-hidden /> {t('edit')}
          </Link>
        }
      />
      <div className="card bg-base-100 shadow-sm">
        <div className="card-body grid grid-cols-1 gap-x-8 gap-y-1 sm:grid-cols-2">
          <Row k={t('code')} v={d.vendorCode} />
          <Row k={t('type')} v={d.vendorType === 'Individual' ? t('individual') : t('corporate')} />
          <Row k={t('nameEn')} v={d.nameEn} />
          <Row k={t('taxId')} v={<span className="font-mono">{formatTaxId(d.taxId)}</span>} />
          <Row k={t('branchCode')} v={d.branchCode} />
          <Row k={t('vatRegistered')} v={d.vatRegistered ? '✓' : '—'} />
          <Row k={t('foreign.toggle')} v={d.isForeign
            ? `${d.countryCode ?? '?'}${d.hasThaiVatDReg ? ' · VAT-D' : ''}` : '—'} />
          <Row k={t('contact')} v={d.contactPerson} />
          <Row k={t('phone')} v={d.phone} />
          <Row k={t('email')} v={d.email} />
          <Row k={t('paymentTerms')} v={d.paymentTermDays} />
          <Row k={t('currency')} v={d.defaultCurrency} />
          <Row k={t('defaultWht')} v={d.defaultWhtTypeCode} />
          <Row k={t('active')} v={d.isActive ? '✓' : '—'} />
          <p className="sm:col-span-2"><b>{t('address')}:</b> {d.address || '—'}</p>
        </div>
      </div>

      {/* ITEM 8 — vendor remittance / payment details (rendered when any is set). */}
      {(d.bankName || d.bankAccountNo || d.bankAccountName || d.swiftCode) && (
        <div className="card mt-4 bg-base-100 shadow-sm">
          <div className="card-body">
            <h2 className="card-title text-base">{t('payment.group')}</h2>
            <div className="grid grid-cols-1 gap-x-8 gap-y-1 sm:grid-cols-2">
              <Row k={t('payment.bankName')} v={d.bankName} />
              <Row k={t('payment.bankAccountNo')} v={d.bankAccountNo} />
              <Row k={t('payment.bankAccountName')} v={d.bankAccountName} />
              <Row k={t('payment.swiftCode')} v={d.swiftCode} />
            </div>
          </div>
        </div>
      )}
    </>
  );
}
