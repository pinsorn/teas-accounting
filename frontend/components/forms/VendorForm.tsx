'use client';

import { useRouter } from 'next/navigation';
import { useForm, Controller } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { PageHeader } from '@/components/ui/PageHeader';
import { TaxIdInput, isValidThaiTaxId } from '@/components/ui/TaxIdInput';
import { useCreateVendor, useUpdateVendor } from '@/lib/queries';
import { apiErrorToast } from '@/lib/api/errors';
import type { CreateVendorRequest, VendorDetail } from '@/lib/types';

// Shared vendor create/edit form. Built from the original `vendors/new` markup so
// the create path (labels, "บันทึกผู้ขาย / Save vendor" button text, redirect to
// /vendors) is byte-for-byte what the createVendor e2e helper drives. In edit mode
// the code + type are locked (UpdateVendorRequest can't change them) and the form
// submits PUT /vendors/{id} via useUpdateVendor, preserving the row's isActive flag.

// M8 — Zod schema mirroring CustomerForm.tsx's pattern (length caps, email format,
// numeric bounds). taxId keeps VendorForm's pre-existing checksum rule: an UNCHANGED
// stored taxId must never block Save (a legacy/seed taxId that fails our client
// checksum must still be editable — the backend ThaiTaxId parser is the real gate).
function buildSchema(edit?: VendorDetail) {
  return z
    .object({
      vendorCode: z.string().min(1, 'required').max(50, 'max50'),
      vendorType: z.enum(['Individual', 'Corporate']),
      nameTh: z.string().min(1, 'required').max(255, 'max255'),
      nameEn: z.string().max(255, 'max255').optional(),
      taxId: z.string().optional(),
      branchCode: z.string().optional(),
      branchName: z.string().optional(),
      vatRegistered: z.boolean(),
      address: z.string().optional(),
      contactPerson: z.string().optional(),
      phone: z.string().optional(),
      email: z.string().email('email').or(z.literal('')).optional(),
      paymentTermDays: z.number().int().min(0),
      defaultCurrency: z.string().length(3, 'currency'),
      defaultWhtTypeCode: z.string().optional(),
      isForeign: z.boolean(),
      hasThaiVatDReg: z.boolean(),
      countryCode: z.string().optional(),
      bankName: z.string().optional(),
      bankAccountNo: z.string().optional(),
      bankAccountName: z.string().optional(),
      swiftCode: z.string().optional(),
    })
    .superRefine((v, ctx) => {
      const taxId = v.taxId?.trim();
      // WP1.4 (F13) — a domestic VAT-registered vendor must carry a taxId (server rule mirror,
      // VendorDtos.cs CreateVendorValidator/UpdateVendorValidator). Foreign vendors are always
      // force VAT-registered but have no Thai taxId, so scope to !isForeign.
      if (!taxId) {
        if (v.vatRegistered && !v.isForeign) {
          ctx.addIssue({ code: 'custom', path: ['taxId'], message: 'taxIdRequiredForVat' });
        }
        return;
      }
      const unchanged = edit != null && taxId === (edit.taxId ?? '').trim();
      if (!unchanged && !isValidThaiTaxId(taxId)) {
        ctx.addIssue({ code: 'custom', path: ['taxId'], message: 'taxId13' });
      }
    });
}
type FormValues = z.infer<ReturnType<typeof buildSchema>>;

const EMPTY_FORM: FormValues = {
  vendorCode: '', vendorType: 'Corporate', nameTh: '', nameEn: '', taxId: '',
  branchCode: '', branchName: '', vatRegistered: true, address: '',
  contactPerson: '', phone: '', email: '', paymentTermDays: 30,
  defaultCurrency: 'THB', defaultWhtTypeCode: '',
  isForeign: false, hasThaiVatDReg: false, countryCode: '',
  // ITEM 8 — vendor remittance details.
  bankName: '', bankAccountNo: '', bankAccountName: '', swiftCode: '',
};

const COUNTRIES = ['US','SG','IE','JP','GB','DE','AU','CN','IN','NL','CA','FR',
  'HK','KR','TW','MY','VN','ID','PH','CH','SE','NZ','AE','LU'];

function fromDetail(d: VendorDetail): FormValues {
  return {
    vendorCode: d.vendorCode, vendorType: d.vendorType, nameTh: d.nameTh,
    nameEn: d.nameEn ?? '', taxId: d.taxId ?? '', branchCode: d.branchCode ?? '',
    branchName: d.branchName ?? '', vatRegistered: d.vatRegistered, address: d.address ?? '',
    contactPerson: d.contactPerson ?? '', phone: d.phone ?? '', email: d.email ?? '',
    paymentTermDays: d.paymentTermDays, defaultCurrency: d.defaultCurrency,
    defaultWhtTypeCode: d.defaultWhtTypeCode ?? '',
    isForeign: d.isForeign, hasThaiVatDReg: d.hasThaiVatDReg, countryCode: d.countryCode ?? '',
    bankName: d.bankName ?? '', bankAccountNo: d.bankAccountNo ?? '',
    bankAccountName: d.bankAccountName ?? '', swiftCode: d.swiftCode ?? '',
  };
}

export function VendorForm({ edit }: { edit?: VendorDetail } = {}) {
  const t = useTranslations('ven');
  const tc = useTranslations('common');
  const router = useRouter();
  const isEdit = edit != null;
  const create = useCreateVendor();
  const update = useUpdateVendor(edit?.vendorId ?? 0);

  const {
    register, control, handleSubmit, watch, setValue,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(buildSchema(edit)),
    defaultValues: edit ? fromDetail(edit) : EMPTY_FORM,
  });

  const pending = create.isPending || update.isPending;
  const foreign = watch('isForeign');
  const vatRegistered = watch('vatRegistered');
  const hasThaiVatDReg = watch('hasThaiVatDReg');
  const countryCode = watch('countryCode');

  const branchCodeField = register('branchCode');

  async function onSubmit(v: FormValues) {
    try {
      if (isEdit && edit) {
        await update.mutateAsync({
          nameTh: v.nameTh,
          nameEn: v.nameEn?.trim() || null,
          taxId: v.taxId?.trim() || null,
          branchCode: v.branchCode?.trim() || null,
          branchName: v.branchName?.trim() || null,
          vatRegistered: v.vatRegistered,
          address: v.address?.trim() || null,
          contactPerson: v.contactPerson?.trim() || null,
          phone: v.phone?.trim() || null,
          email: v.email?.trim() || null,
          paymentTermDays: v.paymentTermDays,
          defaultCurrency: v.defaultCurrency,
          defaultWhtTypeCode: v.defaultWhtTypeCode?.trim() || null,
          isActive: edit.isActive,
          isForeign: v.isForeign,
          hasThaiVatDReg: v.hasThaiVatDReg,
          countryCode: v.countryCode?.trim() || null,
          bankName: v.bankName?.trim() || null,
          bankAccountNo: v.bankAccountNo?.trim() || null,
          bankAccountName: v.bankAccountName?.trim() || null,
          swiftCode: v.swiftCode?.trim() || null,
        });
        toast.success(t('save'));
        router.push(`/vendors/${edit.vendorId}`);
        router.refresh();
      } else {
        const req: CreateVendorRequest = {
          vendorCode: v.vendorCode.trim(),
          vendorType: v.vendorType,
          nameTh: v.nameTh,
          nameEn: v.nameEn?.trim() || null,
          taxId: v.taxId?.trim() || null,
          branchCode: v.branchCode?.trim() || null,
          branchName: v.branchName?.trim() || null,
          vatRegistered: v.vatRegistered,
          address: v.address?.trim() || null,
          contactPerson: v.contactPerson?.trim() || null,
          phone: v.phone?.trim() || null,
          email: v.email?.trim() || null,
          paymentTermDays: v.paymentTermDays,
          defaultCurrency: v.defaultCurrency,
          defaultWhtTypeCode: v.defaultWhtTypeCode?.trim() || null,
          isForeign: v.isForeign,
          hasThaiVatDReg: v.hasThaiVatDReg,
          countryCode: v.countryCode?.trim() || null,
          bankName: v.bankName?.trim() || null,
          bankAccountNo: v.bankAccountNo?.trim() || null,
          bankAccountName: v.bankAccountName?.trim() || null,
          swiftCode: v.swiftCode?.trim() || null,
        };
        await create.mutateAsync(req);
        toast.success(t('save'));
        router.push('/vendors');
        router.refresh();
      }
    } catch (e) {
      apiErrorToast(e);
    }
  }

  const err = (field: keyof FormValues) =>
    errors[field] ? <span className="mt-1 text-xs text-status-danger">{t(`err.${String(errors[field]?.message ?? 'required')}`)}</span> : null;

  return (
    <>
      <PageHeader title={isEdit ? t('editTitle') : t('create')} subtitle={isEdit ? edit?.vendorCode : undefined} />
      <form onSubmit={handleSubmit(onSubmit)}>
      <div className="grid max-w-2xl grid-cols-1 gap-4 sm:grid-cols-2">
        <label className="form-control" htmlFor="vendor-code">
          <span className="label-text">{t('code')} *</span>
          <input id="vendor-code" className="input input-bordered disabled:bg-base-200" disabled={isEdit}
            {...register('vendorCode')} />
          {err('vendorCode')}
        </label>
        <label className="form-control" htmlFor="vendor-type">
          <span className="label-text">{t('type')}</span>
          <select id="vendor-type" className="select select-bordered disabled:bg-base-200" disabled={isEdit}
            {...register('vendorType')}>
            <option value="Corporate">{t('corporate')}</option>
            <option value="Individual">{t('individual')}</option>
          </select>
        </label>
        <label className="form-control" htmlFor="vendor-name-th">
          <span className="label-text">{t('nameTh')} *</span>
          <input id="vendor-name-th" className="input input-bordered" {...register('nameTh')} />
          {err('nameTh')}
        </label>
        <label className="form-control" htmlFor="vendor-name-en">
          <span className="label-text">{t('nameEn')}</span>
          <input id="vendor-name-en" className="input input-bordered" {...register('nameEn')} />
        </label>
        <div>
          <Controller control={control} name="taxId" render={({ field }) => (
            <TaxIdInput
              label={t('taxId') + (vatRegistered && !foreign ? ' *' : '')}
              value={field.value ?? ''}
              onChange={field.onChange}
            />
          )} />
          {err('taxId')}
        </div>
        <label className="form-control" htmlFor="vendor-branch-code">
          <span className="label-text">{t('branchCode')}</span>
          <input id="vendor-branch-code" className="input input-bordered" inputMode="numeric" maxLength={5}
            {...branchCodeField}
            onChange={(e) => {
              e.target.value = e.target.value.replace(/\D/g, '').slice(0, 5);
              branchCodeField.onChange(e);
            }} />
        </label>
        <label className="form-control" htmlFor="vendor-branch-name">
          <span className="label-text">{t('branchName')}</span>
          <input id="vendor-branch-name" className="input input-bordered" {...register('branchName')} />
        </label>
        <label className="form-control" htmlFor="vendor-payment-terms">
          <span className="label-text">{t('paymentTerms')}</span>
          <input id="vendor-payment-terms" type="number" className="input input-bordered"
            {...register('paymentTermDays', { valueAsNumber: true })} />
        </label>
        {/* cont.78 (Ham) — no vendor-level default WHT type. The 50ทวิ income type is
            chosen PER LINE on the Payment Voucher (from the product/line), so this field
            is dropped from the vendor form. The nullable column stays for back-compat. */}
        <label className="form-control sm:col-span-2" htmlFor="vendor-address">
          <span className="label-text">{t('address')}</span>
          <textarea id="vendor-address" className="textarea textarea-bordered" {...register('address')} />
        </label>

        {/* ITEM 8 — vendor remittance / payment details (all optional). */}
        <div className="sm:col-span-2 rounded-lg border border-base-300 p-3">
          <div className="mb-2 text-sm font-semibold">{t('payment.group')}</div>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <label className="form-control" htmlFor="vendor-bank-name">
              <span className="label-text">{t('payment.bankName')}</span>
              <input id="vendor-bank-name" className="input input-bordered" {...register('bankName')} />
            </label>
            <label className="form-control" htmlFor="vendor-bank-account-no">
              <span className="label-text">{t('payment.bankAccountNo')}</span>
              <input id="vendor-bank-account-no" className="input input-bordered" {...register('bankAccountNo')} />
            </label>
            <label className="form-control" htmlFor="vendor-bank-account-name">
              <span className="label-text">{t('payment.bankAccountName')}</span>
              <input id="vendor-bank-account-name" className="input input-bordered" {...register('bankAccountName')} />
            </label>
            <label className="form-control" htmlFor="vendor-swift-code">
              <span className="label-text">{t('payment.swiftCode')}</span>
              <input id="vendor-swift-code" className="input input-bordered" placeholder={t('payment.swiftHint')}
                {...register('swiftCode')} />
            </label>
          </div>
        </div>

        <div className="sm:col-span-2 rounded-lg border border-base-300 p-3 space-y-2">
          <label className="label cursor-pointer justify-start gap-3" htmlFor="vendor-vat-registered">
            <input id="vendor-vat-registered" type="checkbox" className="checkbox checkbox-sm"
              checked={vatRegistered} disabled={foreign}
              onChange={(e) => setValue('vatRegistered', e.target.checked)} />
            <span className="label-text">{t('foreign.vatRegisteredToggle')}</span>
          </label>
          {!vatRegistered && !foreign && (
            <p className="text-xs text-info">{t('foreign.nonVatInfo')}</p>
          )}
          <label className="label cursor-pointer justify-start gap-3" htmlFor="vendor-foreign">
            <input id="vendor-foreign" type="checkbox" className="checkbox checkbox-sm" checked={foreign}
              onChange={(e) => {
                const fg = e.target.checked;
                setValue('isForeign', fg);
                setValue('vatRegistered', fg ? true : vatRegistered);
                setValue('hasThaiVatDReg', fg ? hasThaiVatDReg : false);
                setValue('countryCode', fg ? (countryCode || 'US') : '');
              }} />
            <span className="label-text">{t('foreign.toggle')}</span>
          </label>
          {foreign && (
            <div className="space-y-2 pl-6">
              <label className="form-control max-w-xs" htmlFor="vendor-country-code">
                <span className="label-text">{t('foreign.country')}</span>
                <select id="vendor-country-code" className="select select-bordered select-sm"
                  aria-label={t('foreign.country')}
                  value={countryCode || 'US'}
                  onChange={(e) => setValue('countryCode', e.target.value)}>
                  {COUNTRIES.map((c) => <option key={c} value={c}>{c}</option>)}
                </select>
              </label>
              <label className="label cursor-pointer justify-start gap-3" htmlFor="vendor-vat-d-reg">
                <input id="vendor-vat-d-reg" type="checkbox" className="checkbox checkbox-sm"
                  checked={hasThaiVatDReg}
                  onChange={(e) => setValue('hasThaiVatDReg', e.target.checked)} />
                <span className="label-text">{t('foreign.vatDReg')}</span>
              </label>
              <p className={`text-xs ${hasThaiVatDReg ? 'text-success' : 'text-warning'}`}>
                {hasThaiVatDReg ? t('foreign.vatDInfo') : t('foreign.noVatDWarning')}
              </p>
            </div>
          )}
        </div>
      </div>
      <div className="mt-6 flex gap-2">
        <button type="submit" className="btn btn-primary btn-sm" disabled={pending}>
          {pending ? t('saving') : t('save')}
        </button>
        <button type="button" className="btn btn-ghost btn-sm" onClick={() => router.back()}>
          {tc('cancel')}
        </button>
      </div>
      </form>
    </>
  );
}
