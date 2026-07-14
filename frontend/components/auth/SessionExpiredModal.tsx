'use client';

import { useEffect, useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { AlertTriangle } from 'lucide-react';
import { auth, type LoginResponse } from '@/lib/auth';
import { ApiError } from '@/lib/api';
import { sessionEvents, SESSION_EXPIRED_EVENT } from '@/lib/session-events';

type FormValues = {
  username: string;
  password: string;
  mfaCode?: string;
};

/**
 * WP2.2 (F16/F1) — global in-place re-login. Mounted once in the dashboard layout; opens on
 * the `session-expired` event (lib/api.ts's request() dispatches it on any 401 through the
 * proxy). Re-login happens IN this modal (same POST /api/auth/login the login page uses,
 * re-setting the httpOnly cookie) — the page never navigates, so every open form's React state
 * is untouched. Closing on success just dismisses the overlay; the user re-submits their own
 * save (auto-retry isn't attempted — the failed mutation's own error path already ran).
 */
export function SessionExpiredModal() {
  const t = useTranslations('sessionExpired');
  const tl = useTranslations('login');
  const [open, setOpen] = useState(false);
  const [needMfa, setNeedMfa] = useState(false);

  useEffect(() => {
    const onExpired = () => setOpen(true);
    sessionEvents.addEventListener(SESSION_EXPIRED_EVENT, onExpired);
    return () => sessionEvents.removeEventListener(SESSION_EXPIRED_EVENT, onExpired);
  }, []);

  const schema = z.object({
    username: z.string().min(1, tl('usernameRequired')),
    password: z.string().min(1, tl('passwordRequired')),
    mfaCode: z.string().optional(),
  });

  const {
    register,
    handleSubmit,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({ resolver: zodResolver(schema) });

  async function onSubmit(values: FormValues) {
    try {
      const res: LoginResponse = await auth.login(values.username, values.password, values.mfaCode);
      if ('mfa_required' in res && res.mfa_required) {
        setNeedMfa(true);
        toast.info(tl('mfaPrompt'));
        return;
      }
      setOpen(false);
      setNeedMfa(false);
      reset();
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : tl('genericError');
      toast.error(msg);
    }
  }

  if (!open) return null;

  return (
    <div className="modal modal-open" role="dialog" aria-modal="true">
      <div className="modal-box">
        <h3 className="flex items-center gap-2 text-lg font-bold text-warning">
          <AlertTriangle className="h-5 w-5" aria-hidden />
          {t('title')}
        </h3>
        <p className="mt-2 text-sm text-base-content/70">{t('description')}</p>

        <form onSubmit={handleSubmit(onSubmit)} className="mt-4 space-y-3">
          <label className="form-control">
            <span className="label-text">{tl('username')}</span>
            <input
              type="text"
              autoComplete="username"
              autoFocus
              {...register('username')}
              className="input input-bordered"
            />
            {errors.username && <span className="text-error text-sm">{errors.username.message}</span>}
          </label>

          <label className="form-control">
            <span className="label-text">{tl('password')}</span>
            <input
              type="password"
              autoComplete="current-password"
              {...register('password')}
              className="input input-bordered"
            />
            {errors.password && <span className="text-error text-sm">{errors.password.message}</span>}
          </label>

          {needMfa && (
            <label className="form-control">
              <span className="label-text">{tl('mfa')}</span>
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

          <div className="modal-action">
            <button type="submit" disabled={isSubmitting} className="btn btn-primary w-full">
              {isSubmitting ? tl('submitting') : t('submit')}
            </button>
          </div>
        </form>
      </div>
      {/* No backdrop-dismiss — an expired session must be re-authenticated, not shrugged off. */}
    </div>
  );
}
