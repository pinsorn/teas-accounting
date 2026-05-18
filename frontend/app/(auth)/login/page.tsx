'use client';

import { useState } from 'react';
import { useRouter } from 'next/navigation';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { toast } from 'sonner';
import { auth, type LoginResponse } from '@/lib/auth';
import { ApiError } from '@/lib/api-client';

const schema = z.object({
  username: z.string().min(1, 'ระบุชื่อผู้ใช้'),
  password: z.string().min(1, 'ระบุรหัสผ่าน'),
  mfaCode: z.string().optional(),
});

type FormValues = z.infer<typeof schema>;

export default function LoginPage() {
  const router = useRouter();
  const [needMfa, setNeedMfa] = useState(false);
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
        toast.info('กรอก MFA code 6 หลักจาก Authenticator app');
        return;
      }
      // BFF route set the httpOnly access_token cookie; middleware will now allow /.
      router.push('/');
      router.refresh();
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : 'เกิดข้อผิดพลาด';
      toast.error(msg);
    }
  }

  return (
    <main className="flex min-h-screen items-center justify-center bg-base-200 p-4">
      <form
        onSubmit={handleSubmit(onSubmit)}
        className="card w-full max-w-md bg-base-100 p-8 shadow-xl"
      >
        <h1 className="mb-6 text-2xl font-bold">TEAS — เข้าสู่ระบบ</h1>

        <label className="form-control mb-3">
          <span className="label-text">ชื่อผู้ใช้</span>
          <input
            type="text"
            autoComplete="username"
            {...register('username')}
            className="input input-bordered"
          />
          {errors.username && <span className="text-error text-sm">{errors.username.message}</span>}
        </label>

        <label className="form-control mb-3">
          <span className="label-text">รหัสผ่าน</span>
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
            <span className="label-text">MFA code</span>
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
          {isSubmitting ? 'กำลังเข้าสู่ระบบ...' : 'เข้าสู่ระบบ'}
        </button>
      </form>
    </main>
  );
}
