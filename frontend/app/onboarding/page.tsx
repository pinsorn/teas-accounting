'use client';

import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import Image from 'next/image';
import { Building2 } from 'lucide-react';

// Onboarding-switcher spec (2026-06-16) — first-company wizard. The first thing a brand-new
// super-admin (companyId===0) sees. One clean step: create the first company. On submit, the
// dedicated /api/onboarding route creates the company, switches the JWT to it, and re-sets the
// httpOnly cookie; we then hard-navigate to / to land on the now-scoped dashboard.
//
// §10/§4.6 note: setting VAT fields HERE is the sanctioned super-admin company-create path —
// it is NOT a user-facing VAT settings UI (we never display other companies' VAT config).

const LEGAL_TYPES = [
  'LimitedCompany',
  'PublicLimitedCompany',
  'LimitedPartnership',
  'OrdinaryPartnership',
  'JointVenture',
  'SoleProprietor',
  'Other',
] as const;

const schema = z
  .object({
    nameTh: z.string().min(1, 'required').max(255, 'max255'),
    nameEn: z.string().max(255, 'max255').optional(),
    taxId: z.string().regex(/^\d{13}$/, 'taxId13'),
    legalEntityType: z.enum(LEGAL_TYPES),
    fiscalYearStartMonth: z.coerce.number().int().min(1).max(12),
    vatRegistered: z.boolean(),
    vatRate: z.coerce.number().min(0).max(1),
    pnd30SubmissionMode: z.enum(['manual', 'auto']),
    vatRegisterDate: z.string().optional(),
  })
  .superRefine((v, ctx) => {
    if (v.vatRegistered && !v.vatRegisterDate?.trim()) {
      ctx.addIssue({ code: 'custom', path: ['vatRegisterDate'], message: 'required' });
    }
  });
type FormValues = z.input<typeof schema>;

export default function OnboardingPage() {
  const t = useTranslations('onboarding');

  const {
    register,
    handleSubmit,
    watch,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      legalEntityType: 'LimitedCompany',
      fiscalYearStartMonth: 1,
      vatRegistered: true,
      vatRate: 0.07,
      pnd30SubmissionMode: 'manual',
    },
  });
  const vat = watch('vatRegistered');

  async function onSubmit(v: FormValues) {
    // baseCurrency is NOT a CreateCompanyRequest field (defaults to THB server-side) — omit it.
    const payload = {
      taxId: v.taxId.trim(),
      nameTh: v.nameTh.trim(),
      nameEn: v.nameEn?.trim() || null,
      legalEntityType: v.legalEntityType,
      fiscalYearStartMonth: Number(v.fiscalYearStartMonth),
      vatRegistered: v.vatRegistered,
      vatRegisterDate: v.vatRegistered ? v.vatRegisterDate || null : null,
      vatRate: v.vatRegistered ? Number(v.vatRate) : 0,
      pnd30SubmissionMode: v.pnd30SubmissionMode,
    };
    try {
      const res = await fetch('/api/onboarding', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
      });
      if (!res.ok) {
        const body = await res.json().catch(() => null);
        toast.error(body?.detail ?? body?.title ?? t('error'));
        return;
      }
      toast.success(t('created'));
      // Hard nav so RSC + React Query load under the freshly-scoped company.
      window.location.assign('/');
    } catch {
      toast.error(t('error'));
    }
  }

  // Only these messages have i18n keys; anything else (e.g. zod's default range
  // message for fiscalYearStartMonth) falls back to 'required' so next-intl never
  // throws on a missing key (which would crash the render).
  const KNOWN_ERR = new Set(['required', 'max255', 'taxId13']);
  const err = (field: keyof FormValues) => {
    if (!errors[field]) return null;
    const raw = String(errors[field]?.message ?? 'required');
    const key = KNOWN_ERR.has(raw) ? raw : 'required';
    return <span className="mt-1 text-xs text-status-danger">{t(`err.${key}`)}</span>;
  };

  return (
    <main className="flex min-h-screen items-center justify-center bg-base-200 p-4">
      <div className="card w-full max-w-2xl bg-base-100 p-8 shadow-warm-lg">
        <div className="mb-6 flex flex-col items-center text-center">
          <span className="mb-3 grid h-16 w-16 place-items-center rounded-full bg-gradient-to-br from-peach-100 to-peach-50 text-peach-600">
            <Building2 className="h-8 w-8" aria-hidden />
          </span>
          <h1 className="text-2xl font-bold text-ink-900">{t('title')}</h1>
          <p className="mt-1 text-sm text-ink-500">{t('subtitle')}</p>
        </div>

        <form className="space-y-5" onSubmit={handleSubmit(onSubmit)}>
          <section className="rounded-card border border-ink-100 bg-base-100 p-5 shadow-warm-sm">
            <h2 className="mb-4 text-sm font-bold text-ink-900">{t('secIdentity')}</h2>
            <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
              <label className="form-control">
                <span className="label-text text-ink-600">{t('nameTh')} *</span>
                <input className="input input-bordered" {...register('nameTh')} aria-label={t('nameTh')} />
                {err('nameTh')}
              </label>
              <label className="form-control">
                <span className="label-text text-ink-600">{t('nameEn')}</span>
                <input className="input input-bordered" {...register('nameEn')} aria-label={t('nameEn')} />
              </label>
              <label className="form-control">
                <span className="label-text text-ink-600">{t('taxId')} *</span>
                <input
                  className="input input-bordered font-mono"
                  maxLength={13}
                  inputMode="numeric"
                  {...register('taxId')}
                  aria-label={t('taxId')}
                />
                {err('taxId')}
              </label>
              <label className="form-control">
                <span className="label-text text-ink-600">{t('legalEntityType')} *</span>
                <select
                  className="select select-bordered"
                  {...register('legalEntityType')}
                  aria-label={t('legalEntityType')}
                >
                  {LEGAL_TYPES.map((lt) => (
                    <option key={lt} value={lt}>
                      {t(`legal.${lt}`)}
                    </option>
                  ))}
                </select>
              </label>
            </div>
          </section>

          <section className="rounded-card border border-ink-100 bg-base-100 p-5 shadow-warm-sm">
            <h2 className="mb-4 text-sm font-bold text-ink-900">{t('secTax')}</h2>
            <label className="label mb-3 cursor-pointer justify-start gap-3">
              <input type="checkbox" className="toggle toggle-primary" {...register('vatRegistered')} />
              <span className="font-semibold text-ink-900">{t('vatRegistered')}</span>
            </label>
            {vat && (
              <div className="grid grid-cols-1 gap-4 md:grid-cols-3">
                <label className="form-control">
                  <span className="label-text text-ink-600">{t('vatRate')}</span>
                  <input
                    className="input input-bordered tabular-nums"
                    type="number"
                    step="0.01"
                    min="0"
                    max="1"
                    {...register('vatRate')}
                    aria-label={t('vatRate')}
                  />
                  <span className="mt-1 text-[11px] text-ink-400">{t('vatRateHint')}</span>
                </label>
                <label className="form-control">
                  <span className="label-text text-ink-600">{t('pnd30Mode')}</span>
                  <select
                    className="select select-bordered"
                    {...register('pnd30SubmissionMode')}
                    aria-label={t('pnd30Mode')}
                  >
                    <option value="manual">{t('pnd30Manual')}</option>
                    <option value="auto">{t('pnd30Auto')}</option>
                  </select>
                </label>
                <label className="form-control">
                  <span className="label-text text-ink-600">{t('vatRegisterDate')} *</span>
                  <input
                    className="input input-bordered"
                    type="date"
                    {...register('vatRegisterDate')}
                    aria-label={t('vatRegisterDate')}
                  />
                  {err('vatRegisterDate')}
                </label>
              </div>
            )}
          </section>

          <section className="rounded-card border border-ink-100 bg-base-100 p-5 shadow-warm-sm">
            <h2 className="mb-4 text-sm font-bold text-ink-900">{t('secFiscal')}</h2>
            <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
              <label className="form-control">
                <span className="label-text text-ink-600">{t('fiscalYearStartMonth')}</span>
                <input
                  className="input input-bordered tabular-nums"
                  type="number"
                  min="1"
                  max="12"
                  {...register('fiscalYearStartMonth')}
                  aria-label={t('fiscalYearStartMonth')}
                />
                {err('fiscalYearStartMonth')}
              </label>
              <label className="form-control">
                <span className="label-text text-ink-600">{t('baseCurrency')}</span>
                <input className="input input-bordered bg-base-200" value="THB" readOnly aria-label={t('baseCurrency')} />
              </label>
            </div>
          </section>

          <button type="submit" className="btn btn-primary w-full" disabled={isSubmitting}>
            {isSubmitting ? t('creating') : t('submit')}
          </button>
        </form>
      </div>
    </main>
  );
}
