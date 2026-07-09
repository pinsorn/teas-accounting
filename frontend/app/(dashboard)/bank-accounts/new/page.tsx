'use client';

import { useRouter } from 'next/navigation';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { PageHeader } from '@/components/ui/PageHeader';
import { useCreateBankAccount, useGlAccounts } from '@/lib/queries';

// Bank reconciliation (specs/bank-reconciliation.md B1.11) — bank-account create form.
// glCashAccountId left blank ⇒ BE defaults to the company's 1120 account (D6).
const schema = z.object({
  bankCode: z.string().min(1, 'required').max(20, 'maxLength'),
  bankName: z.string().min(1, 'required').max(255, 'maxLength'),
  accountNo: z.string().min(1, 'required').max(50, 'maxLength'),
  accountName: z.string().max(255, 'maxLength').optional(),
  accountType: z.string().max(50, 'maxLength').optional(),
  glCashAccountId: z.string().optional(),
  currency: z.string().max(3, 'currency').optional(),
});
type FormValues = z.infer<typeof schema>;

export default function NewBankAccountPage() {
  const router = useRouter();
  const t = useTranslations('bank');
  const tc = useTranslations('common');
  const create = useCreateBankAccount();
  const glAccounts = useGlAccounts();

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { currency: 'THB' },
  });

  async function onSubmit(v: FormValues) {
    try {
      await create.mutateAsync({
        bankCode: v.bankCode.trim(),
        bankName: v.bankName.trim(),
        accountNo: v.accountNo.trim(),
        accountName: v.accountName?.trim() || null,
        accountType: v.accountType?.trim() || null,
        glCashAccountId: v.glCashAccountId ? Number(v.glCashAccountId) : null,
        currency: v.currency?.trim() || null,
      });
      toast.success(tc('save'));
      router.push('/bank-accounts');
    } catch (e) {
      toast.error((e as { detail?: string })?.detail ?? tc('error'));
    }
  }

  const err = (field: keyof FormValues) =>
    errors[field] ? (
      <span className="mt-1 text-xs text-status-danger">{t(`err.${String(errors[field]?.message ?? 'required')}`)}</span>
    ) : null;

  return (
    <>
      <PageHeader title={t('createTitle')} />
      <form className="max-w-2xl space-y-5" onSubmit={handleSubmit(onSubmit)}>
        <section className="rounded-card border border-ink-100 bg-base-100 p-5 shadow-warm-sm">
          <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
            <label className="form-control">
              <span className="label-text text-ink-600">{t('bankCode')} *</span>
              <input className="input input-bordered" {...register('bankCode')} aria-label={t('bankCode')} />
              {err('bankCode')}
            </label>
            <label className="form-control">
              <span className="label-text text-ink-600">{t('bankName')} *</span>
              <input className="input input-bordered" {...register('bankName')} aria-label={t('bankName')} />
              {err('bankName')}
            </label>
            <label className="form-control">
              <span className="label-text text-ink-600">{t('accountNo')} *</span>
              <input className="input input-bordered font-mono" {...register('accountNo')} aria-label={t('accountNo')} />
              {err('accountNo')}
            </label>
            <label className="form-control">
              <span className="label-text text-ink-600">{t('accountName')}</span>
              <input className="input input-bordered" {...register('accountName')} aria-label={t('accountName')} />
            </label>
            <label className="form-control">
              <span className="label-text text-ink-600">{t('accountType')}</span>
              <input className="input input-bordered" {...register('accountType')} aria-label={t('accountType')} />
            </label>
            <label className="form-control">
              <span className="label-text text-ink-600">{t('currency')}</span>
              <input className="input input-bordered uppercase" maxLength={3} {...register('currency')} aria-label={t('currency')} />
              {err('currency')}
            </label>
            <label className="form-control md:col-span-2">
              <span className="label-text text-ink-600">{t('glCashAccount')}</span>
              <select className="select select-bordered" {...register('glCashAccountId')} aria-label={t('glCashAccount')}>
                <option value="">{t('glCashAccountDefault')}</option>
                {(glAccounts.data ?? []).map((a) => (
                  <option key={a.accountId} value={a.accountId}>{a.accountCode} — {a.accountNameTh}</option>
                ))}
              </select>
            </label>
          </div>
        </section>

        <div className="flex justify-end gap-2">
          <button type="button" className="btn btn-ghost" onClick={() => router.push('/bank-accounts')}>
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
