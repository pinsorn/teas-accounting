'use client';

import { useState } from 'react';
import Image from 'next/image';
import { useRouter } from 'next/navigation';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { auth, type LoginResponse } from '@/lib/auth';
import { ApiError } from '@/lib/api';

type FormValues = {
  username: string;
  password: string;
  mfaCode?: string;
};

export default function LoginPage() {
  const router = useRouter();
  const t = useTranslations('login');
  const [needMfa, setNeedMfa] = useState(false);

  // Build schema inside the component so validation messages go through i18n
  const schema = z.object({
    username: z.string().min(1, t('usernameRequired')),
    password: z.string().min(1, t('passwordRequired')),
    mfaCode: z.string().optional(),
  });

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({ resolver: zodResolver(schema) });

  async function onSubmit(values: FormValues) {
    try {
      const res: LoginResponse = await auth.login(values.username, values.password, values.mfaCode);
      if ('mfa_required' in res && res.mfa_required) {
        setNeedMfa(true);
        toast.info(t('mfaPrompt'));
        return;
      }
      // BFF route set the httpOnly access_token cookie; middleware will now allow /.
      router.push('/');
      router.refresh();
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : t('genericError');
      toast.error(msg);
    }
  }

  return (
    <main className="flex min-h-screen items-center justify-center bg-base-200 p-4">
      <form
        onSubmit={handleSubmit(onSubmit)}
        className="card w-full max-w-md bg-base-100 p-8 shadow-warm-lg"
      >
        <div className="mb-6 flex flex-col items-center text-center">
          <span className="mb-3 grid h-20 w-20 place-items-center overflow-hidden rounded-full bg-gradient-to-br from-peach-100 to-peach-50 shadow-[inset_0_0_0_2px_rgba(232,168,124,0.3)]">
            <Image
              src="/teas-mascot.png"
              alt="TEAS"
              width={80}
              height={80}
              priority
              className="h-full w-full scale-[1.4] object-cover object-[center_30%]"
            />
          </span>
          <h1 className="text-2xl font-bold text-ink-900">{t('title')}</h1>
        </div>

        <label className="form-control mb-3">
          <span className="label-text">{t('username')}</span>
          <input
            type="text"
            autoComplete="username"
            {...register('username')}
            className="input input-bordered"
          />
          {errors.username && <span className="text-error text-sm">{errors.username.message}</span>}
        </label>

        <label className="form-control mb-3">
          <span className="label-text">{t('password')}</span>
          <input
            type="password"
            autoComplete="current-password"
            {...register('password')}
            className="input input-bordered"
          />
          {errors.password && <span className="text-error text-sm">{errors.password.message}</span>}
        </label>

        {needMfa && (
          <label className="form-control mb-3">
            <span className="label-text">{t('mfa')}</span>
            <input
              type="text"
              inputMode="numeric"
              pattern="\d{6}"
              maxLength={6}
              autoComplete="one-time-code"
              {...register('mfaCode')}
              className="input input-bordered tracking-widest"
              placeholder="123456"
            />
          </label>
        )}

        <button
          type="submit"
          disabled={isSubmitting}
          className="btn btn-primary mt-4"
        >
          {isSubmitting ? t('submitting') : t('submit')}
        </button>
      </form>
    </main>
  );
}
