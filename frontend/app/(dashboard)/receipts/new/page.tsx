'use client';

import { useState } from 'react';
import { useRouter } from 'next/navigation';
import { Controller, useForm, useFieldArray } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useTranslations } from 'next-intl';
import { Plus, Trash2 } from 'lucide-react';
import { toast } from 'sonner';
import { PageHeader } from '@/components/ui/PageHeader';
import { PostConfirmDialog } from '@/components/ui/PostConfirmDialog';
import { CustomerSelector } from '@/components/ui/CustomerSelector';
import { AmountInput } from '@/components/ui/AmountInput';
import { DateInput } from '@/components/ui/DateInput';
import { BusinessUnitSelector } from '@/components/ui/BusinessUnitSelector';
import { useCreateReceipt, usePostReceipt, useCompanyBuSetting, useWhtBaseSuggest, useWhtTypes } from '@/lib/queries';
import { bangkokToday, formatTHB } from '@/lib/utils';

const schema = z.object({
  customerId: z.number().int().positive(),
  applications: z.array(z.object({
    taxInvoiceId: z.number().int().positive(),
    appliedAmount: z.number().positive(),
  })).min(1),
});
type FormValues = z.infer<typeof schema>;

export default function NewReceiptPage() {
  const router = useRouter();
  const t = useTranslations('rc');
  const tc = useTranslations('common');
  const docDate = bangkokToday();
  const create = useCreateReceipt();
  const post = usePostReceipt();
  const buSetting = useCompanyBuSetting();
  const buRequired = buSetting.data?.requiresBusinessUnit ?? false;
  const [businessUnitId, setBusinessUnitId] = useState<number | null>(null);
  const [confirm, setConfirm] = useState<{ id: number } | null>(null);

  const { control, handleSubmit, watch, formState: { isSubmitting } } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { customerId: 0, applications: [{ taxInvoiceId: 0, appliedAmount: 0 }] },
  });
  const { fields, append, remove } = useFieldArray({ control, name: 'applications' });
  const apps = watch('applications');
  const total = apps.reduce((s, a) => s + (a.appliedAmount || 0), 0);

  // Sprint 8.6 — AR-side WHT (manual; auto-suggest per R-B1a/R-B4).
  const tw = useTranslations('rc.wht');
  const [whtOn, setWhtOn] = useState(false);
  const [whtTypeId, setWhtTypeId] = useState<number | null>(null);
  const [whtRate, setWhtRate] = useState(0);          // fraction (0.03)
  const [whtBase, setWhtBase] = useState(0);
  const [whtCertNo, setWhtCertNo] = useState('');
  const [whtCertDate, setWhtCertDate] = useState('');
  const whtTypes = useWhtTypes().data ?? [];
  const customerId = watch('customerId');
  const tiIds = apps.map((a) => a.taxInvoiceId).filter((v) => v > 0);
  const suggest = useWhtBaseSuggest(whtOn ? tiIds : [], whtOn ? customerId : 0);
  const whtAmount = Math.round(whtBase * whtRate * 100) / 100;
  const cashReceived = total - whtAmount;

  function applySuggestion() {
    const s = suggest.data;
    if (!s) return;
    setWhtTypeId(s.suggestedWhtTypeId);
    setWhtRate(s.suggestedWhtRate);
    setWhtBase(s.suggestedWhtBase);
  }

  async function saveDraft(v: FormValues): Promise<number | null> {
    if (buRequired && businessUnitId === null) { toast.error(tc('error')); return null; }
    if (whtOn && (whtAmount <= 0 || whtTypeId === null || whtCertNo.trim() === '')) {
      toast.error(tc('error')); return null;
    }
    try {
      const res = await create.mutateAsync({
        docDate, customerId: v.customerId, paymentMethod: 'Transfer',
        chequeNo: null, chequeDate: null, bankAccountId: null,
        currencyCode: 'THB', exchangeRate: 1, notes: null,
        applications: v.applications,
        businessUnitId,
        whtAmount: whtOn ? whtAmount : 0,
        whtTypeId: whtOn ? whtTypeId : null,
        customerWhtCertNo: whtOn ? whtCertNo : null,
        customerWhtCertDate: whtOn && whtCertDate ? whtCertDate : null,
      });
      toast.success('Draft saved');
      return res.receipt_id;
    } catch { toast.error(tc('error')); return null; }
  }

  return (
    <>
      <PageHeader title={t('create')} subtitle={docDate} />
      <form className="space-y-6" onSubmit={handleSubmit(async (v) => { const id = await saveDraft(v); if (id) router.push('/receipts'); })}>
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
          <Controller control={control} name="customerId" render={({ field, fieldState }) => (
            <div>
              <CustomerSelector value={field.value || null} onChange={(id) => field.onChange(id)} />
              {fieldState.error && <span className="text-error text-sm">เลือกลูกค้า</span>}
            </div>
          )} />
          <DateInput value={docDate} locked label="Date" />
          <BusinessUnitSelector value={businessUnitId} onChange={setBusinessUnitId} required={buRequired} />
        </div>

        <div>
          <div className="mb-2 flex items-center justify-between">
            <h2 className="font-semibold">{t('applyTo')}</h2>
            <button type="button" className="btn btn-ghost btn-sm gap-1" onClick={() => append({ taxInvoiceId: 0, appliedAmount: 0 })}>
              <Plus className="h-4 w-4" aria-hidden /> {t('addApply')}
            </button>
          </div>
          <div className="overflow-x-auto rounded-lg border border-base-300">
            <table className="table">
              <thead><tr><th>taxInvoiceId</th><th className="text-right">{t('amount')}</th><th className="w-10" /></tr></thead>
              <tbody>
                {fields.map((f, i) => (
                  <tr key={f.id}>
                    <td>
                      <Controller control={control} name={`applications.${i}.taxInvoiceId`} render={({ field }) => (
                        <AmountInput value={field.value} step={1} onValueChange={field.onChange} aria-label={`taxInvoiceId ${i + 1}`} />
                      )} />
                    </td>
                    <td>
                      <Controller control={control} name={`applications.${i}.appliedAmount`} render={({ field }) => (
                        <AmountInput value={field.value} onValueChange={field.onChange} aria-label={`appliedAmount ${i + 1}`} />
                      )} />
                    </td>
                    <td>
                      <button type="button" className="btn btn-ghost btn-xs text-error" disabled={fields.length === 1} onClick={() => remove(i)}>
                        <Trash2 className="h-4 w-4" aria-hidden />
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>

        {/* Sprint 8.6 — AR-side WHT (collapsible). Manual base per R-B1a. */}
        <div className="rounded-lg border border-base-300 p-4">
          <label className="label cursor-pointer justify-start gap-3">
            <input type="checkbox" className="toggle toggle-primary" checked={whtOn}
              onChange={(e) => setWhtOn(e.target.checked)} />
            <span className="font-semibold">{tw('toggleEnable')}</span>
          </label>
          {whtOn && (
            <div className="mt-3 grid grid-cols-1 gap-3 md:grid-cols-2">
              {suggest.data && (
                <div className="md:col-span-2 flex items-center justify-between gap-2 rounded bg-base-200 p-2 text-xs">
                  <span>{suggest.data.explanation}</span>
                  <button type="button" className="btn btn-xs" onClick={applySuggestion}>
                    {tw('title')} ▸
                  </button>
                </div>
              )}
              <label className="form-control md:col-span-2">
                <span className="label-text">{tw('type')} *</span>
                <select className="select select-bordered select-sm"
                  aria-label={tw('type')}
                  value={whtTypeId ?? ''}
                  onChange={(e) => {
                    const id = e.target.value ? Number(e.target.value) : null;
                    setWhtTypeId(id);
                    const wt = whtTypes.find((w) => w.whtTypeId === id);
                    if (wt) setWhtRate(wt.rate);
                  }}>
                  <option value="">—</option>
                  {whtTypes.filter((w) => w.effectiveTo === null).map((w) => (
                    <option key={w.whtTypeId} value={w.whtTypeId}>
                      {w.code} — {w.nameTh} ({(w.rate * 100).toFixed(2)}%)
                    </option>
                  ))}
                </select>
              </label>
              <label className="form-control">
                <span className="label-text">{tw('rate')}</span>
                <input type="number" step="0.01" className="input input-bordered input-sm"
                  value={whtRate * 100}
                  onChange={(e) => setWhtRate(Number(e.target.value) / 100)} />
              </label>
              <label className="form-control">
                <span className="label-text">{tw('base')}</span>
                <input type="number" step="0.01" className="input input-bordered input-sm"
                  value={whtBase}
                  onChange={(e) => setWhtBase(Number(e.target.value))} />
              </label>
              <label className="form-control">
                <span className="label-text">{tw('amount')}</span>
                <input className="input input-bordered input-sm tabular-nums" readOnly
                  value={whtAmount.toFixed(2)} />
              </label>
              <label className="form-control">
                <span className="label-text">{tw('cashReceived')}</span>
                <input className="input input-bordered input-sm tabular-nums" readOnly
                  value={cashReceived.toFixed(2)} />
              </label>
              <label className="form-control">
                <span className="label-text">{tw('certNo')} *</span>
                <input className="input input-bordered input-sm" value={whtCertNo}
                  onChange={(e) => setWhtCertNo(e.target.value)} />
              </label>
              <label className="form-control">
                <span className="label-text">{tw('certDate')}</span>
                <input type="date" className="input input-bordered input-sm" value={whtCertDate}
                  onChange={(e) => setWhtCertDate(e.target.value)} />
              </label>
              <p className="md:col-span-2 text-xs text-warning">{tw('serviceOnlyExplain')}</p>
            </div>
          )}
        </div>

        <div className="flex items-end justify-between">
          <div className="text-lg font-bold tabular-nums">{t('amount')}: {formatTHB(total)}</div>
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
        summary={{ customer: `#${watch('customerId')}`, total, vat: 0 }}
        recipients={[]}
        onClose={() => setConfirm(null)}
        onConfirm={async () => {
          if (!confirm) return;
          try { await post.mutateAsync(confirm.id); toast.success('Posted'); router.push(`/receipts/${confirm.id}`); }
          catch { toast.error(tc('error')); }
          finally { setConfirm(null); }
        }}
      />
    </>
  );
}
