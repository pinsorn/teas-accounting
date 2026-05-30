'use client';

import { useState } from 'react';
import { useRouter } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { PageHeader } from '@/components/ui/PageHeader';
import { TaxIdInput, isValidThaiTaxId } from '@/components/ui/TaxIdInput';
import { useCreateVendor } from '@/lib/queries';
import type { CreateVendorRequest, VendorType } from '@/lib/types';

const EMPTY: CreateVendorRequest = {
  vendorCode: '', vendorType: 'Corporate', nameTh: '', nameEn: null, taxId: null,
  branchCode: null, branchName: null, vatRegistered: true, address: null,
  contactPerson: null, phone: null, email: null, paymentTermDays: 30,
  defaultCurrency: 'THB', defaultWhtTypeCode: null,
  isForeign: false, hasThaiVatDReg: false, countryCode: null,
  // ITEM 8 — vendor remittance details.
  bankName: null, bankAccountNo: null, bankAccountName: null, swiftCode: null,
};

const COUNTRIES = ['US','SG','IE','JP','GB','DE','AU','CN','IN','NL','CA','FR',
  'HK','KR','TW','MY','VN','ID','PH','CH','SE','NZ','AE','LU'];

export default function VendorNewPage() {
  const t = useTranslations('ven');
  const tc = useTranslations('common');
  const router = useRouter();
  const create = useCreateVendor();
  const [f, setF] = useState<CreateVendorRequest>(EMPTY);

  const taxOk = !f.taxId || isValidThaiTaxId(f.taxId);
  const canSave = f.vendorCode.trim() !== '' && f.nameTh.trim() !== '' && taxOk;

  function set<K extends keyof CreateVendorRequest>(k: K, v: CreateVendorRequest[K]) {
    setF((p) => ({ ...p, [k]: v }));
  }

  async function submit() {
    try {
      await create.mutateAsync({
        ...f,
        nameEn: f.nameEn || null,
        taxId: f.taxId || null,
        branchCode: f.branchCode || null,
        address: f.address || null,
        contactPerson: f.contactPerson || null,
        phone: f.phone || null,
        email: f.email || null,
        defaultWhtTypeCode: f.defaultWhtTypeCode || null,
        bankName: f.bankName || null,
        bankAccountNo: f.bankAccountNo || null,
        bankAccountName: f.bankAccountName || null,
        swiftCode: f.swiftCode || null,
      });
      toast.success(t('save'));
      router.push('/vendors');
      router.refresh();
    } catch {
      toast.error(tc('error'));
    }
  }

  return (
    <>
      <PageHeader title={t('create')} />
      <div className="grid max-w-2xl grid-cols-1 gap-4 sm:grid-cols-2">
        <label className="form-control">
          <span className="label-text">{t('code')} *</span>
          <input className="input input-bordered" value={f.vendorCode}
            onChange={(e) => set('vendorCode', e.target.value)} />
        </label>
        <label className="form-control">
          <span className="label-text">{t('type')}</span>
          <select className="select select-bordered" value={f.vendorType}
            onChange={(e) => set('vendorType', e.target.value as VendorType)}>
            <option value="Corporate">{t('corporate')}</option>
            <option value="Individual">{t('individual')}</option>
          </select>
        </label>
        <label className="form-control">
          <span className="label-text">{t('nameTh')} *</span>
          <input className="input input-bordered" value={f.nameTh}
            onChange={(e) => set('nameTh', e.target.value)} />
        </label>
        <label className="form-control">
          <span className="label-text">{t('nameEn')}</span>
          <input className="input input-bordered" value={f.nameEn ?? ''}
            onChange={(e) => set('nameEn', e.target.value)} />
        </label>
        <TaxIdInput label={t('taxId')} value={f.taxId ?? ''}
          onChange={(v) => set('taxId', v)} />
        <label className="form-control">
          <span className="label-text">{t('branchCode')}</span>
          <input className="input input-bordered" inputMode="numeric" maxLength={5}
            value={f.branchCode ?? ''}
            onChange={(e) => set('branchCode', e.target.value.replace(/\D/g, '').slice(0, 5))} />
        </label>
        <label className="form-control">
          <span className="label-text">{t('paymentTerms')}</span>
          <input type="number" className="input input-bordered" value={f.paymentTermDays}
            onChange={(e) => set('paymentTermDays', Number(e.target.value) || 0)} />
        </label>
        <label className="form-control">
          <span className="label-text">{t('defaultWht')}</span>
          <input className="input input-bordered" placeholder="เช่น SVC, RENT"
            value={f.defaultWhtTypeCode ?? ''}
            onChange={(e) => set('defaultWhtTypeCode', e.target.value || null)} />
        </label>
        <label className="form-control sm:col-span-2">
          <span className="label-text">{t('address')}</span>
          <textarea className="textarea textarea-bordered" value={f.address ?? ''}
            onChange={(e) => set('address', e.target.value)} />
        </label>

        {/* ITEM 8 — vendor remittance / payment details (all optional). */}
        <div className="sm:col-span-2 rounded-lg border border-base-300 p-3">
          <div className="mb-2 text-sm font-semibold">{t('payment.group')}</div>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <label className="form-control">
              <span className="label-text">{t('payment.bankName')}</span>
              <input className="input input-bordered" value={f.bankName ?? ''}
                onChange={(e) => set('bankName', e.target.value || null)} />
            </label>
            <label className="form-control">
              <span className="label-text">{t('payment.bankAccountNo')}</span>
              <input className="input input-bordered" value={f.bankAccountNo ?? ''}
                onChange={(e) => set('bankAccountNo', e.target.value || null)} />
            </label>
            <label className="form-control">
              <span className="label-text">{t('payment.bankAccountName')}</span>
              <input className="input input-bordered" value={f.bankAccountName ?? ''}
                onChange={(e) => set('bankAccountName', e.target.value || null)} />
            </label>
            <label className="form-control">
              <span className="label-text">{t('payment.swiftCode')}</span>
              <input className="input input-bordered" value={f.swiftCode ?? ''}
                placeholder={t('payment.swiftHint')}
                onChange={(e) => set('swiftCode', e.target.value || null)} />
            </label>
          </div>
        </div>

        <div className="sm:col-span-2 rounded-lg border border-base-300 p-3 space-y-2">
          <label className="label cursor-pointer justify-start gap-3">
            <input type="checkbox" className="checkbox checkbox-sm"
              checked={f.vatRegistered} disabled={f.isForeign}
              onChange={(e) => set('vatRegistered', e.target.checked)} />
            <span className="label-text">{t('foreign.vatRegisteredToggle')}</span>
          </label>
          {!f.vatRegistered && !f.isForeign && (
            <p className="text-xs text-info">{t('foreign.nonVatInfo')}</p>
          )}
          <label className="label cursor-pointer justify-start gap-3">
            <input type="checkbox" className="checkbox checkbox-sm" checked={f.isForeign}
              onChange={(e) => {
                const fg = e.target.checked;
                setF((p) => ({ ...p, isForeign: fg,
                  vatRegistered: fg ? true : p.vatRegistered,
                  hasThaiVatDReg: fg ? p.hasThaiVatDReg : false,
                  countryCode: fg ? (p.countryCode ?? 'US') : null }));
              }} />
            <span className="label-text">{t('foreign.toggle')}</span>
          </label>
          {f.isForeign && (
            <div className="space-y-2 pl-6">
              <label className="form-control max-w-xs">
                <span className="label-text">{t('foreign.country')}</span>
                <select className="select select-bordered select-sm"
                  aria-label={t('foreign.country')}
                  value={f.countryCode ?? 'US'}
                  onChange={(e) => set('countryCode', e.target.value)}>
                  {COUNTRIES.map((c) => <option key={c} value={c}>{c}</option>)}
                </select>
              </label>
              <label className="label cursor-pointer justify-start gap-3">
                <input type="checkbox" className="checkbox checkbox-sm"
                  checked={f.hasThaiVatDReg}
                  onChange={(e) => set('hasThaiVatDReg', e.target.checked)} />
                <span className="label-text">{t('foreign.vatDReg')}</span>
              </label>
              <p className={`text-xs ${f.hasThaiVatDReg ? 'text-success' : 'text-warning'}`}>
                {f.hasThaiVatDReg ? t('foreign.vatDInfo') : t('foreign.noVatDWarning')}
              </p>
            </div>
          )}
        </div>
      </div>
      <div className="mt-6 flex gap-2">
        <button className="btn btn-primary btn-sm" disabled={!canSave || create.isPending}
          onClick={submit}>
          {create.isPending ? t('saving') : t('save')}
        </button>
        <button className="btn btn-ghost btn-sm" onClick={() => router.back()}>
          {tc('cancel')}
        </button>
      </div>
    </>
  );
}
