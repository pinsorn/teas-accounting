'use client';

import { use, useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { PageHeader } from '@/components/ui/PageHeader';
import { StatementImportSection } from '@/components/bank/StatementImportSection';
import { useBankAccount, useUpdateBankAccount, useGlAccounts } from '@/lib/queries';

// Bank reconciliation (specs/bank-reconciliation.md B1.11) — bank-account create-edit form
// (view + edit combined; bankCode/accountNo are immutable post-create per UpdateBankAccountRequest).
const schema = z.object({
  bankName: z.string().min(1, 'required').max(255, 'maxLength'),
  accountName: z.string().max(255, 'maxLength').optional(),
  accountType: z.string().max(50, 'maxLength').optional(),
  glCashAccountId: z.string().min(1, 'required'),
  currency: z.string().min(1, 'required').max(3, 'currency'),
  isActive: z.boolean(),
});
type FormValues = z.infer<typeof schema>;

export default function BankAccountEditPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const bid = Number(id);
  const router = useRouter();
  const t = useTranslations('bank');
  const tc = useTranslations('common');
  const q = useBankAccount(bid);
  const update = useUpdateBankAccount(bid);
  const glAccounts = useGlAccounts();
  const d = q.data;

  const {
    register,
    handleSubmit,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({ resolver: zodResolver(schema) });

  useEffect(() => {
    if (d) {
      reset({
        bankName: d.bankName,
        accountName: d.accountName ?? '',
        accountType: d.accountType ?? '',
        glCashAccountId: String(d.glCashAccountId),
        currency: d.currency,
        isActive: d.isActive,
      });
    }
  }, [d, reset]);

  if (q.isLoading) return <div className="p-6 text-ink-400">{tc('loading')}</div>;
  if (q.isError || !d) return <div className="p-6 text-status-danger">{tc('error')}</div>;

  async function onSubmit(v: FormValues) {
    try {
      await update.mutateAsync({
        bankName: v.bankName.trim(),
        accountName: v.accountName?.trim() || null,
        accountType: v.accountType?.trim() || null,
        glCashAccountId: Number(v.glCashAccountId),
        currency: v.currency.trim().toUpperCase(),
        isActive: v.isActive,
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
      <PageHeader title={t('editTitle')} subtitle={d.accountNo} />
      <form className="max-w-2xl space-y-5" onSubmit={handleSubmit(onSubmit)}>
        <section className="rounded-card border border-ink-100 bg-base-100 p-5 shadow-warm-sm">
          <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
            <label className="form-control">
              <span className="label-text text-ink-600">{t('bankCode')}</span>
              <input className="input input-bordered disabled:bg-base-200" value={d.bankCode} disabled aria-label={t('bankCode')} />
            </label>
            <label className="form-control">
              <span className="label-text text-ink-600">{t('accountNo')}</span>
              <input className="input input-bordered disabled:bg-base-200 font-mono" value={d.accountNo} disabled aria-label={t('accountNo')} />
            </label>
            <label className="form-control">
              <span className="label-text text-ink-600">{t('bankName')} *</span>
              <input className="input input-bordered" {...register('bankName')} aria-label={t('bankName')} />
              {err('bankName')}
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
              <span className="label-text text-ink-600">{t('glCashAccount')} *</span>
              <select className="select select-bordered" {...register('glCashAccountId')} aria-label={t('glCashAccount')}>
                {(glAccounts.data ?? []).map((a) => (
                  <option key={a.accountId} value={a.accountId}>{a.accountCode} — {a.accountNameTh}</option>
                ))}
              </select>
              {err('glCashAccountId')}
            </label>
            <label className="label mt-1 cursor-pointer justify-start gap-3 md:col-span-2">
              <input type="checkbox" className="toggle toggle-primary" {...register('isActive')} />
              <span className="font-semibold text-ink-900">{tc('active')}</span>
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

      <StatementImportSection bankAccountId={bid} />
    </>
  );
}
