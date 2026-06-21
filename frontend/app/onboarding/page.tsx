'use client';

import { useEffect, useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { Building2, KeyRound, ShieldCheck, UserCog } from 'lucide-react';

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

// A base64 string that decodes to exactly 32 bytes (AES-256). atob throws on invalid base64.
function isBase64_32Bytes(s: string): boolean {
  try {
    return atob(s).length === 32;
  } catch {
    return false;
  }
}

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
    // ม.86/4 — founding registered address. Province + 5-digit postal are required (RD forms);
    // street-level parts are structured but optional. Mirrors the backend validator + profile map.
    addrHouseNo: z.string().optional(),
    addrMoo: z.string().optional(),
    addrSoi: z.string().optional(),
    addrStreet: z.string().optional(),
    addrSubDistrict: z.string().optional(),
    addrDistrict: z.string().optional(),
    addrProvince: z.string().min(1, 'required'),
    addrPostalCode: z.string().regex(/^\d{5}$/, 'postal5'),
    addrBuilding: z.string().optional(),
    addrFloor: z.string().optional(),
    addrRoomNo: z.string().optional(),
    addrVillage: z.string().optional(),
    // First-run INSTANCE security settings (instance-wide, shown once). Written to the
    // git-ignored appsettings.Secrets.json by POST /system/setup/instance-keys — NOT a
    // committed file. The MFA key is a 32-byte (AES-256) base64; JWT lifetime 5–1440 min.
    mfaAesKeyBase64: z.string().min(1, 'required').refine(isBase64_32Bytes, 'mfaKey32'),
    jwtAccessTokenMinutes: z.coerce.number().int().min(5, 'minutesRange').max(1440, 'minutesRange'),
    // Optional: also provision a sample/demo company with example data (best chosen at install time).
    seedDemo: z.boolean(),
  })
  .superRefine((v, ctx) => {
    if (v.vatRegistered && !v.vatRegisterDate?.trim()) {
      ctx.addIssue({ code: 'custom', path: ['vatRegisterDate'], message: 'required' });
    }
  });
type FormValues = z.input<typeof schema>;

// Onboarding always runs as a 2-step wizard:
//   1. createAdmin — ONLY on a brand-new install (no session / zero users). Creates the first
//      super-admin via POST /system/setup/bootstrap-admin (anonymous, zero-users-gated), then logs
//      in with those credentials so the company step runs as the companyId=0 super-admin.
//   2. company    — the existing create-first-company step. Reached directly when the visitor is
//      already the authenticated companyId=0 super-admin (the seeded/just-created admin).
type Phase = 'checking' | 'createAdmin' | 'company';

export default function OnboardingPage() {
  const t = useTranslations('onboarding');
  const [phase, setPhase] = useState<Phase>('checking');

  // On mount decide which step to show: do we already have a companyId=0 super-admin session?
  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const res = await fetch('/api/proxy/me', { cache: 'no-store' });
        if (!res.ok) { if (!cancelled) setPhase('createAdmin'); return; }
        // A super-admin lands here at companyId=0. If companies ALREADY exist this is NOT a fresh
        // install — they just have no home company (LoginService primary-scope=0). Switch into one
        // (the existing super-admin switcher path) and go to the dashboard instead of showing the
        // create-first-company form again — which was looping back here on every re-login. Only a
        // truly empty instance (zero companies) reaches the 'company' step to create the first.
        const me = await res.json().catch(() => null);
        const companies = (me?.allowedCompanies ?? []) as { id: number }[];
        const first = companies[0];
        if (first) {
          const sw = await fetch('/api/auth/switch-company', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ companyId: first.id }),
          }).catch(() => null);
          if (sw?.ok) { window.location.replace('/'); return; }
        }
        if (!cancelled) setPhase('company');
      } catch {
        if (!cancelled) setPhase('createAdmin');
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  const {
    register,
    handleSubmit,
    watch,
    setValue,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      legalEntityType: 'LimitedCompany',
      fiscalYearStartMonth: 1,
      vatRegistered: true,
      vatRate: 0.07,
      pnd30SubmissionMode: 'manual',
      mfaAesKeyBase64: '',
      jwtAccessTokenMinutes: 60,
      seedDemo: false,
      addrHouseNo: '',
      addrMoo: '',
      addrSoi: '',
      addrStreet: '',
      addrSubDistrict: '',
      addrDistrict: '',
      addrProvince: '',
      addrPostalCode: '',
      addrBuilding: '',
      addrFloor: '',
      addrRoomNo: '',
      addrVillage: '',
    },
  });
  const vat = watch('vatRegistered');

  // Generate a 32-byte AES-256 key with the cryptographically-secure WebCrypto RNG and
  // base64-encode it — purely client-side, no server round-trip. Fills the field directly.
  function generateMfaKey() {
    const bytes = crypto.getRandomValues(new Uint8Array(32));
    const b64 = btoa(String.fromCharCode(...bytes));
    setValue('mfaAesKeyBase64', b64, { shouldValidate: true, shouldDirty: true });
    toast.success(t('mfaKeyGenerated'));
  }

  // ── Step 1 (fresh install only): create the first super-admin, then advance to the company step.
  const [adminUsername, setAdminUsername] = useState('');
  const [adminPassword, setAdminPassword] = useState('');
  const [adminFullName, setAdminFullName] = useState('');
  const [adminEmail, setAdminEmail] = useState('');
  const [adminBusy, setAdminBusy] = useState(false);

  async function onCreateAdmin(e: React.FormEvent) {
    e.preventDefault();
    if (adminUsername.trim().length < 3 || adminPassword.length < 12) {
      toast.error(t('err.required'));
      return;
    }
    setAdminBusy(true);
    try {
      const res = await fetch('/api/setup/bootstrap-admin', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          username: adminUsername.trim(),
          password: adminPassword,
          fullName: adminFullName.trim() || null,
          email: adminEmail.trim() || null,
        }),
      });
      if (!res.ok) {
        const body = await res.json().catch(() => null);
        // 409 = an admin already exists on this system → send them to sign in.
        toast.error(res.status === 409 ? t('adminExists') : body?.detail ?? body?.title ?? t('error'));
        if (res.status === 409) window.location.assign('/login');
        return;
      }
      toast.success(t('adminCreated'));
      setPhase('company'); // now authenticated as the companyId=0 super-admin
    } catch {
      toast.error(t('error'));
    } finally {
      setAdminBusy(false);
    }
  }

  async function onSubmit(v: FormValues) {
    // 1) First-run INSTANCE security settings → git-ignored appsettings.Secrets.json.
    //    Done BEFORE company creation while the caller is still the companyId===0 super-admin.
    //    The instance MFA key is first-run only (the backend 409s on a second attempt), so a
    //    benign "already configured" is tolerated; any other failure aborts onboarding.
    try {
      const setupRes = await fetch('/api/proxy/system/setup/instance-keys', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          mfaAesKeyBase64: v.mfaAesKeyBase64,
          jwtAccessTokenMinutes: Number(v.jwtAccessTokenMinutes),
        }),
      });
      if (!setupRes.ok && setupRes.status !== 409) {
        const body = await setupRes.json().catch(() => null);
        toast.error(body?.detail ?? body?.title ?? t('error'));
        return;
      }
    } catch {
      toast.error(t('error'));
      return;
    }
    // baseCurrency is NOT a CreateCompanyRequest field (defaults to THB server-side) — omit it.
    const t0 = (s?: string) => s?.trim() || null;
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
      // ม.86/4 — founding registered address. Tambon/Amphoe/Province/Postal reuse the
      // companies tails; the street-level parts go to company_profile.Reg* (composed Line1).
      subDistrict: t0(v.addrSubDistrict),
      district: t0(v.addrDistrict),
      province: v.addrProvince.trim(),
      postalCode: v.addrPostalCode.trim(),
      regHouseNo: t0(v.addrHouseNo),
      regMoo: t0(v.addrMoo),
      regSoi: t0(v.addrSoi),
      regStreet: t0(v.addrStreet),
      regBuilding: t0(v.addrBuilding),
      regFloor: t0(v.addrFloor),
      regRoomNo: t0(v.addrRoomNo),
      regVillage: t0(v.addrVillage),
    };
    try {
      const res = await fetch('/api/onboarding', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        // seedDemo is carried alongside the company payload; the BFF forwards the real company
        // create regardless and (when supported) triggers the optional demo-company seed.
        body: JSON.stringify({ ...payload, seedDemo: v.seedDemo === true }),
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
  const KNOWN_ERR = new Set(['required', 'max255', 'taxId13', 'postal5', 'mfaKey32', 'minutesRange']);
  const err = (field: keyof FormValues) => {
    if (!errors[field]) return null;
    const raw = String(errors[field]?.message ?? 'required');
    const key = KNOWN_ERR.has(raw) ? raw : 'required';
    return <span className="mt-1 text-xs text-status-danger">{t(`err.${key}`)}</span>;
  };

  // ── Phase: deciding which step to show (brief; mount-time /me probe). ──
  if (phase === 'checking') {
    return (
      <main className="flex min-h-screen items-center justify-center bg-base-200 p-4">
        <span className="loading loading-spinner loading-lg text-peach-600" aria-label={t('loading')} />
      </main>
    );
  }

  // ── Phase: create the first super-admin (fresh install, zero users). ──
  if (phase === 'createAdmin') {
    return (
      <main className="flex min-h-screen items-center justify-center bg-base-200 p-4">
        <div className="card w-full max-w-md bg-base-100 p-8 shadow-warm-lg">
          <div className="mb-6 flex flex-col items-center text-center">
            <span className="mb-3 grid h-16 w-16 place-items-center rounded-full bg-gradient-to-br from-peach-100 to-peach-50 text-peach-600">
              <UserCog className="h-8 w-8" aria-hidden />
            </span>
            <h1 className="text-2xl font-bold text-ink-900">{t('adminTitle')}</h1>
            <p className="mt-1 text-sm text-ink-500">{t('adminSubtitle')}</p>
          </div>
          <form className="space-y-4" onSubmit={onCreateAdmin}>
            <label className="form-control">
              <span className="label-text text-ink-600">{t('adminUsername')} *</span>
              <input
                className="input input-bordered"
                autoComplete="username"
                value={adminUsername}
                onChange={(e) => setAdminUsername(e.target.value)}
                aria-label={t('adminUsername')}
              />
              <span className="mt-1 text-[11px] text-ink-400">{t('adminUsernameHint')}</span>
            </label>
            <label className="form-control">
              <span className="label-text text-ink-600">{t('adminPassword')} *</span>
              <input
                className="input input-bordered"
                type="password"
                autoComplete="new-password"
                value={adminPassword}
                onChange={(e) => setAdminPassword(e.target.value)}
                aria-label={t('adminPassword')}
              />
              <span className="mt-1 text-[11px] text-ink-400">{t('adminPasswordHint')}</span>
            </label>
            <label className="form-control">
              <span className="label-text text-ink-600">{t('adminFullName')}</span>
              <input
                className="input input-bordered"
                autoComplete="name"
                value={adminFullName}
                onChange={(e) => setAdminFullName(e.target.value)}
                aria-label={t('adminFullName')}
              />
            </label>
            <label className="form-control">
              <span className="label-text text-ink-600">{t('adminEmail')}</span>
              <input
                className="input input-bordered"
                type="email"
                autoComplete="email"
                value={adminEmail}
                onChange={(e) => setAdminEmail(e.target.value)}
                aria-label={t('adminEmail')}
              />
            </label>
            <button type="submit" className="btn btn-primary w-full" disabled={adminBusy}>
              {adminBusy ? t('adminCreating') : t('adminSubmit')}
            </button>
          </form>
        </div>
      </main>
    );
  }

  // ── Phase: create the first company (authenticated companyId=0 super-admin). ──
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
                    {/* auto is disabled in onboarding until the RD e-filing API is wired
                        (today 'auto' only hits MockRdEfilingClient — no real submission). */}
                    <option value="auto" disabled>{t('pnd30Auto')}</option>
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
            <h2 className="mb-1 text-sm font-bold text-ink-900">{t('secAddress')}</h2>
            <p className="mb-4 text-[11px] leading-snug text-ink-400">{t('addressHint')}</p>
            <div className="grid grid-cols-1 gap-4 md:grid-cols-3">
              <label className="form-control">
                <span className="label-text text-ink-600">{t('addrHouseNo')}</span>
                <input className="input input-bordered" {...register('addrHouseNo')} aria-label={t('addrHouseNo')} />
              </label>
              <label className="form-control">
                <span className="label-text text-ink-600">{t('addrMoo')}</span>
                <input className="input input-bordered" {...register('addrMoo')} aria-label={t('addrMoo')} />
              </label>
              <label className="form-control">
                <span className="label-text text-ink-600">{t('addrSoi')}</span>
                <input className="input input-bordered" {...register('addrSoi')} aria-label={t('addrSoi')} />
              </label>
              <label className="form-control md:col-span-2">
                <span className="label-text text-ink-600">{t('addrStreet')}</span>
                <input className="input input-bordered" {...register('addrStreet')} aria-label={t('addrStreet')} />
              </label>
              <label className="form-control">
                <span className="label-text text-ink-600">{t('addrSubDistrict')}</span>
                <input className="input input-bordered" {...register('addrSubDistrict')} aria-label={t('addrSubDistrict')} />
              </label>
              <label className="form-control">
                <span className="label-text text-ink-600">{t('addrDistrict')}</span>
                <input className="input input-bordered" {...register('addrDistrict')} aria-label={t('addrDistrict')} />
              </label>
              <label className="form-control">
                <span className="label-text text-ink-600">{t('addrProvince')} *</span>
                <input className="input input-bordered" {...register('addrProvince')} aria-label={t('addrProvince')} />
                {err('addrProvince')}
              </label>
              <label className="form-control">
                <span className="label-text text-ink-600">{t('addrPostalCode')} *</span>
                <input
                  className="input input-bordered font-mono"
                  maxLength={5}
                  inputMode="numeric"
                  {...register('addrPostalCode')}
                  aria-label={t('addrPostalCode')}
                />
                {err('addrPostalCode')}
              </label>
            </div>

            <details className="mt-4">
              <summary className="cursor-pointer text-xs font-semibold text-ink-500">
                {t('addrOptionalGroup')}
              </summary>
              <div className="mt-3 grid grid-cols-1 gap-4 md:grid-cols-2">
                <label className="form-control">
                  <span className="label-text text-ink-600">{t('addrBuilding')}</span>
                  <input className="input input-bordered" {...register('addrBuilding')} aria-label={t('addrBuilding')} />
                </label>
                <label className="form-control">
                  <span className="label-text text-ink-600">{t('addrFloor')}</span>
                  <input className="input input-bordered" {...register('addrFloor')} aria-label={t('addrFloor')} />
                </label>
                <label className="form-control">
                  <span className="label-text text-ink-600">{t('addrRoomNo')}</span>
                  <input className="input input-bordered" {...register('addrRoomNo')} aria-label={t('addrRoomNo')} />
                </label>
                <label className="form-control">
                  <span className="label-text text-ink-600">{t('addrVillage')}</span>
                  <input className="input input-bordered" {...register('addrVillage')} aria-label={t('addrVillage')} />
                </label>
              </div>
            </details>
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

          <section className="rounded-card border border-ink-100 bg-base-100 p-5 shadow-warm-sm">
            <h2 className="mb-1 flex items-center gap-2 text-sm font-bold text-ink-900">
              <ShieldCheck className="h-4 w-4 text-peach-600" aria-hidden />
              {t('secSecurity')}
            </h2>
            <p className="mb-4 text-[11px] leading-snug text-ink-400">{t('securityHint')}</p>
            <div className="grid grid-cols-1 gap-4">
              <label className="form-control">
                <span className="label-text text-ink-600">{t('mfaKey')} *</span>
                <div className="flex gap-2">
                  <input
                    className="input input-bordered flex-1 font-mono text-xs"
                    placeholder={t('mfaKeyPlaceholder')}
                    autoComplete="off"
                    spellCheck={false}
                    {...register('mfaAesKeyBase64')}
                    aria-label={t('mfaKey')}
                  />
                  <button
                    type="button"
                    className="btn btn-outline btn-secondary gap-1 whitespace-nowrap"
                    onClick={generateMfaKey}
                  >
                    <KeyRound className="h-4 w-4" aria-hidden />
                    {t('mfaKeyGenerate')}
                  </button>
                </div>
                <span className="mt-1 text-[11px] text-ink-400">{t('mfaKeyHint')}</span>
                {err('mfaAesKeyBase64')}
              </label>
              <label className="form-control md:max-w-xs">
                <span className="label-text text-ink-600">{t('jwtMinutes')}</span>
                <input
                  className="input input-bordered tabular-nums"
                  type="number"
                  min="5"
                  max="1440"
                  {...register('jwtAccessTokenMinutes')}
                  aria-label={t('jwtMinutes')}
                />
                <span className="mt-1 text-[11px] text-ink-400">{t('jwtMinutesHint')}</span>
                {err('jwtAccessTokenMinutes')}
              </label>
            </div>
          </section>

          <section className="rounded-card border border-ink-100 bg-base-100 p-5 shadow-warm-sm">
            <label className="label mb-1 cursor-pointer justify-start gap-3">
              <input type="checkbox" className="toggle toggle-primary" {...register('seedDemo')} />
              <span className="font-semibold text-ink-900">{t('seedDemo')}</span>
            </label>
            <p className="text-[11px] leading-snug text-ink-400">{t('seedDemoHint')}</p>
          </section>

          <button type="submit" className="btn btn-primary w-full" disabled={isSubmitting}>
            {isSubmitting ? t('creating') : t('submit')}
          </button>
        </form>
      </div>
    </main>
  );
}
