'use client';

import { useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { PageHeader } from '@/components/ui/PageHeader';
import { PostConfirmDialog } from '@/components/ui/PostConfirmDialog';
import { AmountInput } from '@/components/ui/AmountInput';
import { DateInput } from '@/components/ui/DateInput';
import { BusinessUnitSelector } from '@/components/ui/BusinessUnitSelector';
import { useCreateAdjustmentNote, usePostAdjustmentNote, useCompanyBuSetting } from '@/lib/queries';
import { CREDIT_NOTE_REASONS, DEBIT_NOTE_REASONS, type AdjustmentNoteType } from '@/lib/types';
import { bangkokToday, formatTHB } from '@/lib/utils';

const schema = z.object({
  originalTaxInvoiceId: z.number().int().positive(),
  reasonCode: z.string().min(1),
  reason: z.string().min(1, 'เหตุผลบังคับตามกฎหมาย'),
  adjustmentSubtotal: z.number().positive(),
  taxRate: z.number().min(0).max(1),
});
type FormValues = z.infer<typeof schema>;

// Shared CN (ม.86/10) / DN (ม.86/9) create form. Amount-based per Answer-Sana-Q4 Q2(a).
// Customer is NOT entered — it's snapshot-copied server-side from the original TI
// (Sana: "CN cannot have a separate payer").
export function AdjustmentNoteForm({ noteType }: { noteType: AdjustmentNoteType }) {
  const router = useRouter();
  const sp = useSearchParams();
  const t = useTranslations('note');
  const tc = useTranslations('common');
  const docDate = bangkokToday();
  const isCredit = noteType === 'Credit';
  const reasons = isCredit ? CREDIT_NOTE_REASONS : DEBIT_NOTE_REASONS;
  const basePath = isCredit ? '/credit-notes' : '/debit-notes';

  const create = useCreateAdjustmentNote();
  const post = usePostAdjustmentNote();
  const buSetting = useCompanyBuSetting();
  const buRequired = buSetting.data?.requiresBusinessUnit ?? false;
  const [businessUnitId, setBusinessUnitId] = useState<number | null>(null);
  const [confirm, setConfirm] = useState<{ id: number } | null>(null);

  const { control, handleSubmit, watch, formState: { isSubmitting } } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      originalTaxInvoiceId: Number(sp.get('fromTaxInvoiceId')) || 0,
      reasonCode: sp.get('reason') ?? reasons[0],
      reason: '',
      adjustmentSubtotal: 0,
      taxRate: 0.07,
    },
  });
  const sub = watch('adjustmentSubtotal');
  const rate = watch('taxRate');
  const vat = sub * rate;

  async function saveDraft(v: FormValues): Promise<number | null> {
    if (buRequired && businessUnitId === null) { toast.error(tc('error')); return null; }
    try {
      const res = await create.mutateAsync({
        noteType: isCredit ? 'Credit' : 'Debit',
        docDate,
        originalTaxInvoiceId: v.originalTaxInvoiceId,
        reasonCode: v.reasonCode,
        reason: v.reason,
        adjustmentSubtotal: v.adjustmentSubtotal,
        taxRate: v.taxRate,
        currencyCode: 'THB',
        exchangeRate: 1,
        notes: null,
        businessUnitId,
      });
      toast.success('Draft saved');
      return res.note_id;
    } catch { toast.error(tc('error')); return null; }
  }

  return (
    <>
      <PageHeader title={isCredit ? t('cnTitle') : t('dnTitle')} subtitle={docDate} />
      <form className="max-w-2xl space-y-4"
        onSubmit={handleSubmit(async (v) => { const id = await saveDraft(v); if (id) router.push(basePath); })}>
        <Controller control={control} name="originalTaxInvoiceId" render={({ field, fieldState }) => (
          <label className="form-control">
            <span className="label-text">{t('originalTi')} *</span>
            <AmountInput value={field.value} step={1} onValueChange={field.onChange} aria-label="originalTaxInvoiceId" />
            {fieldState.error && <span className="text-error text-sm">ระบุใบกำกับภาษีเดิม</span>}
          </label>
        )} />

        <Controller control={control} name="reasonCode" render={({ field }) => (
          <label className="form-control">
            <span className="label-text">{t('reasonCode')} *</span>
            <select className="select select-bordered" {...field}>
              {reasons.map((r) => <option key={r} value={r}>{r}</option>)}
            </select>
          </label>
        )} />

        <Controller control={control} name="reason" render={({ field, fieldState }) => (
          <label className="form-control">
            <span className="label-text">{t('reasonText')} *</span>
            <textarea className="textarea textarea-bordered" rows={3} {...field} />
            {fieldState.error && <span className="text-error text-sm">{fieldState.error.message}</span>}
          </label>
        )} />

        <BusinessUnitSelector value={businessUnitId} onChange={setBusinessUnitId} required={buRequired} />

        <div className="grid grid-cols-2 gap-4">
          <Controller control={control} name="adjustmentSubtotal" render={({ field }) => (
            <label className="form-control">
              <span className="label-text">{t('subtotal')} *</span>
              <AmountInput value={field.value} onValueChange={field.onChange} aria-label="adjustmentSubtotal" />
            </label>
          )} />
          <Controller control={control} name="taxRate" render={({ field }) => (
            <label className="form-control">
              <span className="label-text">{t('taxRate')}</span>
              <AmountInput value={field.value} step={0.01} onValueChange={field.onChange} aria-label="taxRate" />
            </label>
          )} />
        </div>

        <div className="flex items-end justify-between border-t border-base-300 pt-4">
          <div className="text-sm tabular-nums">
            VAT {formatTHB(vat)} · Total <b>{formatTHB(sub + vat)}</b>
          </div>
          <div className="flex gap-2">
            <button type="submit" className="btn btn-ghost" disabled={isSubmitting}>{tc('save')}</button>
            <button type="button" className="btn btn-primary" disabled={isSubmitting}
              onClick={handleSubmit(async (v) => { const id = await saveDraft(v); if (id) setConfirm({ id }); })}>
              {t('post')}
            </button>
          </div>
        </div>
      </form>

      <PostConfirmDialog
        open={confirm !== null}
        busy={post.isPending}
        summary={{ customer: `TI #${watch('originalTaxInvoiceId')}`, total: sub + vat, vat }}
        recipients={[]}
        onClose={() => setConfirm(null)}
        onConfirm={async () => {
          if (!confirm) return;
          try { await post.mutateAsync(confirm.id); toast.success('Posted'); router.push(`${basePath}/${confirm.id}`); }
          catch { toast.error(tc('error')); }
          finally { setConfirm(null); }
        }}
      />
    </>
  );
}
