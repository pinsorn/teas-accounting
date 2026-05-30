'use client';

import { useState } from 'react';
import { useRouter } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { PageHeader } from '@/components/ui/PageHeader';
import { TaxIdInput, isValidThaiTaxId } from '@/components/ui/TaxIdInput';
import { useCreateVendor, useUpdateVendor } from '@/lib/queries';
import type { CreateVendorRequest, VendorType, VendorDetail } from '@/lib/types';

// Shared vendor create/edit form. Built from the original `vendors/new` markup so
// the create path (labels, "บันทึกผู้ขาย / Save vendor" button text, redirect to
// /vendors) is byte-for-byte what the createVendor e2e helper drives. In edit mode
// the code + type are locked (UpdateVendorRequest can't change them) and the form
// submits PUT /vendors/{id} via useUpdateVendor, preserving the row's isActive flag.

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

function fromDetail(d: VendorDetail): CreateVendorRequest {
  return {
    vendorCode: d.vendorCode, vendorType: d.vendorType, nameTh: d.nameTh,
    nameEn: d.nameEn, taxId: d.taxId, branchCode: d.branchCode, branchName: d.branchName,
    vatRegistered: d.vatRegistered, address: d.address, contactPerson: d.contactPerson,
    phone: d.phone, email: d.email, paymentTermDays: d.paymentTermDays,
    defaultCurrency: d.defaultCurrency, defaultWhtTypeCode: d.defaultWhtTypeCode,
    isForeign: d.isForeign, hasThaiVatDReg: d.hasThaiVatDReg, countryCode: d.countryCode,
    bankName: d.bankName, bankAccountNo: d.bankAccountNo,
    bankAccountName: d.bankAccountName, swiftCode: d.swiftCode,
  };
}

export function VendorForm({ edit }: { edit?: VendorDetail } = {}) {
  const t = useTranslations('ven');
  const tc = useTranslations('common');
  const router = useRouter();
  const isEdit = edit != null;
  const create = useCreateVendor();
  const update = useUpdateVendor(edit?.vendorId ?? 0);
  const [f, setF] = useState<CreateVendorRequest>(edit ? fromDetail(edit) : EMPTY);

  const pending = create.isPending || update.isPending;
  // The backend (ThaiTaxId.TryParse) is the source of truth and has already
  // accepted any pre-existing taxId. The client checksum is a stricter early
  // gate, so on edit an UNCHANGED stored taxId must never block Save (otherwise
  // a vendor whose seed/legacy taxId fails our checksum can never be edited at
  // all — not even a phone-number change). Validate only when the value changed.
  const taxUnchanged = isEdit && (f.taxId ?? '') === (edit?.taxId ?? '');
  const taxOk = !f.taxId || taxUnchanged || isValidThaiTaxId(f.taxId);
  const canSave = f.vendorCode.trim() !== '' && f.nameTh.trim() !== '' && taxOk;

  function set<K extends keyof CreateVendorRequest>(k: K, v: CreateVendorRequest[K]) {
    setF((p) => ({ ...p, [k]: v }));
  }

  async function submit() {
    try {
      if (isEdit && edit) {
        await update.mutateAsync({
          nameTh: f.nameTh,
          nameEn: f.nameEn || null,
          taxId: f.taxId || null,
          branchCode: f.branchCode || null,
          branchName: f.branchName || null,
          vatRegistered: f.vatRegistered,
          address: f.address || null,
          contactPerson: f.contactPerson || null,
          phone: f.phone || null,
          email: f.email || null,
          paymentTermDays: f.paymentTermDays,
          defaultCurrency: f.defaultCurrency,
          defaultWhtTypeCode: f.defaultWhtTypeCode || null,
          isActive: edit.isActive,
          isForeign: f.isForeign,
          hasThaiVatDReg: f.hasThaiVatDReg,
          countryCode: f.countryCode || null,
          bankName: f.bankName || null,
          bankAccountNo: f.bankAccountNo || null,
          bankAccountName: f.bankAccountName || null,
          swiftCode: f.swiftCode || null,
        });
        toast.success(t('save'));
        router.push(`/vendors/${edit.vendorId}`);
        router.refresh();
      } else {
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
      }
    } catch {
      toast.error(tc('error'));
    }
  }

  return (
    <>
      <PageHeader title={isEdit ? t('editTitle') : t('create')} subtitle={isEdit ? f.vendorCode : undefined} />
      <div className="grid max-w-2xl grid-cols-1 gap-4 sm:grid-cols-2">
        <label className="form-control">
          <span className="label-text">{t('code')} *</span>
          <input className="input input-bordered disabled:bg-base-200" value={f.vendorCode}
            disabled={isEdit}
            onChange={(e) => set('vendorCode', e.target.value)} />
        </label>
        <label className="form-control">
          <span className="label-text">{t('type')}</span>
          <select className="select select-bordered disabled:bg-base-200" value={f.vendorType}
            disabled={isEdit}
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
          <span className="label-text">{t('branchName')}</span>
          <input className="input input-bordered" value={f.branchName ?? ''}
            onChange={(e) => set('branchName', e.target.value || null)} />
        </label>
        <label className="form-control">
          <span className="label-text">{t('paymentTerms')}</span>
          <input type="number" className="input input-bordered" value={f.paymentTermDays}
            onChange={(e) => set('paymentTermDays', Number(e.target.value) || 0)} />
        </label>
        {/* cont.78 (Ham) — no vendor-level default WHT type. The 50ทวิ income type is
            chosen PER LINE on the Payment Voucher (from the product/line), so this field
            is dropped from the vendor form. The nullable column stays for back-compat. */}
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
        <button className="btn btn-primary btn-sm" disabled={!canSave || pending}
          onClick={submit}>
          {pending ? t('saving') : t('save')}
        </button>
        <button className="btn btn-ghost btn-sm" onClick={() => router.back()}>
          {tc('cancel')}
        </button>
      </div>
    </>
  );
}
