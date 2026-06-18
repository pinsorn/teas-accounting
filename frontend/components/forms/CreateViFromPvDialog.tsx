'use client';

import { useEffect } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useRouter } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { useCreateViFromPv } from '@/lib/queries';
import { ApiError } from '@/lib/api';

// purchase-completeness Phase 3 (D1) — guided PV→VI create. The standalone VI
// create entry point is hidden; the user reaches VI creation from the PV. This
// dialog collects the vendor tax-invoice no./date (+ optional claim period),
// POSTs to /payment-vouchers/{id}/vendor-invoice, then navigates to the VI.
const schema = z.object({
  vendorTaxInvoiceNo: z.string().trim().min(1),
  vendorTaxInvoiceDate: z.string().min(1),
  vatClaimPeriod: z.string().optional(),
});
type FormValues = z.infer<typeof schema>;

export function CreateViFromPvDialog({
  pvId,
  open,
  defaultDate,
  onClose,
}: {
  pvId: number;
  open: boolean;
  defaultDate: string;
  onClose: () => void;
}) {
  const t = useTranslations('pv');
  const tc = useTranslations('common');
  const router = useRouter();
  const create = useCreateViFromPv();
  const {
    register, handleSubmit, reset,
    formState: { errors, isValid },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    mode: 'onChange',
    defaultValues: { vendorTaxInvoiceNo: '', vendorTaxInvoiceDate: defaultDate, vatClaimPeriod: '' },
  });

  useEffect(() => {
    if (open) reset({ vendorTaxInvoiceNo: '', vendorTaxInvoiceDate: defaultDate, vatClaimPeriod: '' });
  }, [open, defaultDate, reset]);

  if (!open) return null;

  async function onSubmit(v: FormValues) {
    try {
      const res = await create.mutateAsync({
        pvId,
        req: {
          vendorTaxInvoiceNo: v.vendorTaxInvoiceNo.trim(),
          vendorTaxInvoiceDate: v.vendorTaxInvoiceDate,
          vatClaimPeriod: v.vatClaimPeriod ? Number(v.vatClaimPeriod) : null,
        },
      });
      toast.success(tc('save'));
      onClose();
      router.push(`/vendor-invoices/${res.vendor_invoice_id}`);
    } catch (e) {
      // 409 pv.vi_exists — a VI is already linked to this PV.
      if (e instanceof ApiError && e.status === 409) {
        toast.error(t('createVi.alreadyLinked'));
        onClose();
        return;
      }
      toast.error(tc('error'));
    }
  }

  return (
    <div className="modal modal-open" role="dialog" aria-modal="true"
      aria-labelledby="create-vi-title" data-testid="create-vi-dialog">
      <div className="modal-box">
        <h3 id="create-vi-title" className="text-lg font-semibold">{t('createVi.title')}</h3>
        <p className="mt-1 text-sm text-base-content/60">{t('createVi.hint')}</p>
        <form className="mt-4 space-y-4" onSubmit={handleSubmit(onSubmit)}>
          <label className="form-control">
            <span className="label-text">{t('createVi.tiNo')} *</span>
            <input className="input input-bordered" data-testid="create-vi-ti-no"
              {...register('vendorTaxInvoiceNo')} aria-label={t('createVi.tiNo')} />
            {errors.vendorTaxInvoiceNo && (
              <span className="label-text-alt text-error">{t('createVi.required')}</span>
            )}
          </label>
          <label className="form-control">
            <span className="label-text">{t('createVi.tiDate')} *</span>
            <input type="date" className="input input-bordered" data-testid="create-vi-ti-date"
              {...register('vendorTaxInvoiceDate')} aria-label={t('createVi.tiDate')} />
          </label>
          <label className="form-control">
            <span className="label-text">{t('createVi.claimPeriod')}</span>
            <input type="number" className="input input-bordered" placeholder="YYYYMM"
              data-testid="create-vi-claim" {...register('vatClaimPeriod')}
              aria-label={t('createVi.claimPeriod')} />
            <span className="label-text-alt text-base-content/50">{t('createVi.claimHint')}</span>
          </label>
          <div className="modal-action">
            <button type="button" className="btn btn-ghost" onClick={onClose}
              disabled={create.isPending}>
              {tc('cancel')}
            </button>
            <button type="submit" className="btn btn-primary" data-testid="create-vi-submit"
              disabled={!isValid || create.isPending}>
              {tc('confirm')}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
