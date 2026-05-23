'use client';

import { useRouter } from 'next/navigation';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { PageHeader } from '@/components/ui/PageHeader';
import { useCreateCustomer, useUpdateCustomer } from '@/lib/queries';
import type { CustomerDetail } from '@/lib/types';
import type { ReactNode } from 'react';

// Sprint 13j-FE — Customer create/edit form (sales master). VAT-registered ⇒
// Tax ID + branch code required (ม.86/4 #3). In edit mode `customerCode` +
// `customerType` are locked (UpdateCustomerRequest can't change them).

const schema = z
  .object({
    customerCode: z.string().min(1, 'required').max(50, 'max50'),
    customerType: z.enum(['Individual', 'Corporate']),
    nameTh: z.string().min(1, 'required').max(255, 'max255'),
    nameEn: z.string().max(255, 'max255').optional(),
    taxId: z.string().optional(),
    branchCode: z.string().optional(),
    branchName: z.string().optional(),
    vatRegistered: z.boolean(),
    billingAddress: z.string().optional(),
    contactPerson: z.string().optional(),
    phone: z.string().optional(),
    email: z.string().email('email').or(z.literal('')).optional(),
    creditLimit: z.number().min(0),
    paymentTermDays: z.number().int().min(0),
    defaultCurrency: z.string().length(3, 'currency'),
  })
  .superRefine((v, ctx) => {
    if (v.vatRegistered) {
      if (!v.taxId?.trim()) ctx.addIssue({ code: 'custom', path: ['taxId'], message: 'vatRequired' });
      if (!v.branchCode?.trim()) ctx.addIssue({ code: 'custom', path: ['branchCode'], message: 'vatRequired' });
    }
    if (v.taxId && v.taxId.trim() && !/^\d{13}$/.test(v.taxId.trim()))
      ctx.addIssue({ code: 'custom', path: ['taxId'], message: 'taxId13' });
    if (v.branchCode && v.branchCode.trim() && !/^\d{5}$/.test(v.branchCode.trim()))
      ctx.addIssue({ code: 'custom', path: ['branchCode'], message: 'branch5' });
  });
type FormValues = z.infer<typeof schema>;

function Section({ n, title, children }: { n: number; title: string; children: ReactNode }) {
  return (
    <section className="rounded-card border border-ink-100 bg-base-100 p-5 shadow-warm-sm">
      <h2 className="mb-4 flex items-center gap-2 text-sm font-bold text-ink-900">
        <span className="grid h-[22px] w-[22px] place-items-center rounded-full bg-peach-100 text-[11px] font-bold text-peach-700">{n}</span>
        {title}
      </h2>
      {children}
    </section>
  );
}

export function CustomerForm({ edit }: { edit?: CustomerDetail } = {}) {
  const router = useRouter();
  const t = useTranslations('cust');
  const tc = useTranslations('common');
  const isEdit = edit != null;
  const create = useCreateCustomer();
  const update = useUpdateCustomer(edit?.customerId ?? 0);

  const {
    register,
    handleSubmit,
    watch,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: edit
      ? {
          customerCode: edit.customerCode,
          customerType: edit.customerType,
          nameTh: edit.nameTh,
          nameEn: edit.nameEn ?? '',
          taxId: edit.taxId ?? '',
          branchCode: edit.branchCode ?? '',
          branchName: edit.branchName ?? '',
          vatRegistered: edit.vatRegistered,
          billingAddress: edit.billingAddress ?? '',
          contactPerson: edit.contactPerson ?? '',
          phone: edit.phone ?? '',
          email: edit.email ?? '',
          creditLimit: edit.creditLimit,
          paymentTermDays: edit.paymentTermDays,
          defaultCurrency: edit.defaultCurrency,
        }
      : {
          customerType: 'Corporate',
          vatRegistered: true,
          creditLimit: 0,
          paymentTermDays: 30,
          defaultCurrency: 'THB',
        },
  });
  const vat = watch('vatRegistered');

  async function onSubmit(v: FormValues) {
    try {
      if (isEdit && edit) {
        await update.mutateAsync({
          nameTh: v.nameTh.trim(),
          nameEn: v.nameEn?.trim() || null,
          taxId: v.taxId?.trim() || null,
          branchCode: v.branchCode?.trim() || null,
          branchName: v.branchName?.trim() || null,
          vatRegistered: v.vatRegistered,
          billingAddress: v.billingAddress?.trim() || null,
          contactPerson: v.contactPerson?.trim() || null,
          phone: v.phone?.trim() || null,
          email: v.email?.trim() || null,
          creditLimit: v.creditLimit,
          paymentTermDays: v.paymentTermDays,
          defaultCurrency: v.defaultCurrency.trim().toUpperCase(),
          isActive: edit.isActive,
        });
        toast.success(tc('save'));
        router.push(`/customers/${edit.customerId}`);
      } else {
        await create.mutateAsync({
          customerCode: v.customerCode.trim(),
          customerType: v.customerType,
          nameTh: v.nameTh.trim(),
          nameEn: v.nameEn?.trim() || null,
          taxId: v.taxId?.trim() || null,
          branchCode: v.branchCode?.trim() || null,
          branchName: v.branchName?.trim() || null,
          vatRegistered: v.vatRegistered,
          billingAddress: v.billingAddress?.trim() || null,
          contactPerson: v.contactPerson?.trim() || null,
          phone: v.phone?.trim() || null,
          email: v.email?.trim() || null,
          creditLimit: v.creditLimit,
          paymentTermDays: v.paymentTermDays,
          defaultCurrency: v.defaultCurrency.trim().toUpperCase(),
        });
        toast.success(tc('save'));
        router.push('/customers');
      }
    } catch (e) {
      toast.error((e as { detail?: string })?.detail ?? tc('error'));
    }
  }

  const err = (field: keyof FormValues) =>
    errors[field] ? <span className="mt-1 text-xs text-status-danger">{t(`err.${String(errors[field]?.message ?? 'required')}`)}</span> : null;

  return (
    <>
      <PageHeader title={isEdit ? t('editTitle') : t('createTitle')} subtitle={edit?.customerCode ?? t('subtitle')} />
      <form className="max-w-3xl space-y-5" onSubmit={handleSubmit(onSubmit)}>
        <Section n={1} title={t('secIdentity')}>
          <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
            <label className="form-control">
              <span className="label-text text-ink-600">{t('code')} *</span>
              <input className="input input-bordered disabled:bg-base-200" {...register('customerCode')} disabled={isEdit} aria-label={t('code')} />
              {err('customerCode')}
            </label>
            <label className="form-control">
              <span className="label-text text-ink-600">{t('type')} *</span>
              <select className="select select-bordered disabled:bg-base-200" {...register('customerType')} disabled={isEdit} aria-label={t('type')}>
                <option value="Corporate">{t('corporate')}</option>
                <option value="Individual">{t('individual')}</option>
              </select>
            </label>
            <label className="form-control">
              <span className="label-text text-ink-600">{t('nameTh')} *</span>
              <input className="input input-bordered" {...register('nameTh')} aria-label={t('nameTh')} />
              {err('nameTh')}
            </label>
            <label className="form-control">
              <span className="label-text text-ink-600">{t('nameEn')}</span>
              <input className="input input-bordered" {...register('nameEn')} aria-label={t('nameEn')} />
            </label>
          </div>
        </Section>

        <Section n={2} title={t('secTax')}>
          <label className="label mb-3 cursor-pointer justify-start gap-3">
            <input type="checkbox" className="toggle toggle-primary" {...register('vatRegistered')} />
            <span className="font-semibold text-ink-900">{t('vatRegistered')}</span>
          </label>
          <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
            <label className="form-control">
              <span className="label-text text-ink-600">{t('taxId')} {vat && '*'}</span>
              <input className="input input-bordered font-mono" maxLength={13} inputMode="numeric" {...register('taxId')} aria-label={t('taxId')} />
              {err('taxId')}
            </label>
            <div className="grid grid-cols-2 gap-3">
              <label className="form-control">
                <span className="label-text text-ink-600">{t('branchCode')} {vat && '*'}</span>
                <input className="input input-bordered font-mono" maxLength={5} inputMode="numeric" placeholder="00000" {...register('branchCode')} aria-label={t('branchCode')} />
                {err('branchCode')}
              </label>
              <label className="form-control">
                <span className="label-text text-ink-600">{t('branchName')}</span>
                <input className="input input-bordered" {...register('branchName')} aria-label={t('branchName')} />
              </label>
            </div>
          </div>
        </Section>

        <Section n={3} title={t('secContact')}>
          <div className="grid grid-cols-1 gap-4">
            <label className="form-control">
              <span className="label-text text-ink-600">{t('billingAddress')}</span>
              <textarea className="textarea textarea-bordered" rows={2} {...register('billingAddress')} aria-label={t('billingAddress')} />
            </label>
            <div className="grid grid-cols-1 gap-4 md:grid-cols-3">
              <label className="form-control">
                <span className="label-text text-ink-600">{t('contactPerson')}</span>
                <input className="input input-bordered" {...register('contactPerson')} aria-label={t('contactPerson')} />
              </label>
              <label className="form-control">
                <span className="label-text text-ink-600">{t('phone')}</span>
                <input className="input input-bordered" {...register('phone')} aria-label={t('phone')} />
              </label>
              <label className="form-control">
                <span className="label-text text-ink-600">{t('email')}</span>
                <input className="input input-bordered" type="email" {...register('email')} aria-label={t('email')} />
                {err('email')}
              </label>
            </div>
          </div>
        </Section>

        <Section n={4} title={t('secTerms')}>
          <div className="grid grid-cols-1 gap-4 md:grid-cols-3">
            <label className="form-control">
              <span className="label-text text-ink-600">{t('creditLimit')}</span>
              <input className="input input-bordered tabular-nums" type="number" step="0.01" {...register('creditLimit', { valueAsNumber: true })} aria-label={t('creditLimit')} />
            </label>
            <label className="form-control">
              <span className="label-text text-ink-600">{t('paymentTermDays')}</span>
              <input className="input input-bordered tabular-nums" type="number" {...register('paymentTermDays', { valueAsNumber: true })} aria-label={t('paymentTermDays')} />
            </label>
            <label className="form-control">
              <span className="label-text text-ink-600">{t('currency')}</span>
              <input className="input input-bordered uppercase" maxLength={3} {...register('defaultCurrency')} aria-label={t('currency')} />
            </label>
          </div>
        </Section>

        <div className="flex justify-end gap-2">
          <button type="button" className="btn btn-ghost" onClick={() => router.push(isEdit && edit ? `/customers/${edit.customerId}` : '/customers')}>
            {tc('cancel')}
          </button>
          <button type="submit" className="btn btn-primary" disabled={isSubmitting}>
            {tc('save')}
          </button>
        </div>
      </form>
    </>
  );
}
