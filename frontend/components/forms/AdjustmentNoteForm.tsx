'use client';

import { useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { PostConfirmDialog } from '@/components/ui/PostConfirmDialog';
import { AmountInput } from '@/components/ui/AmountInput';
import { DateInput } from '@/components/ui/DateInput';
import { BusinessUnitSelector } from '@/components/ui/BusinessUnitSelector';
import { TaxInvoicePicker } from '@/components/forms/TaxInvoicePicker';
import { useCreateAdjustmentNote, usePostAdjustmentNote, useCompanyBuSetting, useCompanyProfile, useSystemInfo } from '@/lib/queries';
import { NonVatGuard } from '@/components/ui/NonVatGuard';
import { CREDIT_NOTE_REASONS, DEBIT_NOTE_REASONS, type AdjustmentNoteType } from '@/lib/types';
import { bangkokToday } from '@/lib/utils';
import { onInvalidSubmit, scrollToFirstError } from '@/lib/forms';
import { PaperDocument } from '@/components/paper/PaperDocument';
import { PAPER_DOC, companyToSeller } from '@/lib/paper-doc-config';
import { DocumentCreateLayout } from '@/components/create/DocumentCreateLayout';
import { SectionCard } from '@/components/create/SectionCard';
import { TotalsSummaryBox, type TotalRow } from '@/components/create/TotalsSummaryBox';
import { LivePreviewPane } from '@/components/create/LivePreviewPane';

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
  const tt = useTranslations('toast');
  const tcr = useTranslations('create');
  const docDate = bangkokToday();
  const isCredit = noteType === 'Credit';
  const reasons = isCredit ? CREDIT_NOTE_REASONS : DEBIT_NOTE_REASONS;
  const basePath = isCredit ? '/credit-notes' : '/debit-notes';

  const create = useCreateAdjustmentNote();
  const post = usePostAdjustmentNote();
  const company = useCompanyProfile();
  const buSetting = useCompanyBuSetting();
  const buRequired = buSetting.data?.requiresBusinessUnit ?? false;
  const [businessUnitId, setBusinessUnitId] = useState<number | null>(null);
  const [buError, setBuError] = useState(false);
  const [confirm, setConfirm] = useState<{ id: number } | null>(null);

  const invalid = onInvalidSubmit((m) => toast.error(m), tt('validationFailed'));

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
  const reasonText = watch('reason');
  const vat = sub * rate;
  const cfg = PAPER_DOC[isCredit ? 'credit-note' : 'debit-note'];
  const vatMode = useSystemInfo().data?.vatMode ?? true;

  async function saveDraft(v: FormValues): Promise<number | null> {
    if (buRequired && businessUnitId === null) {
      setBuError(true);
      toast.error(tt('validationFailed'));
      requestAnimationFrame(scrollToFirstError);
      return null;
    }
    setBuError(false);
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
      toast.success(tc('draftSaved'));
      return res.note_id;
    } catch { toast.error(tc('error')); return null; }
  }

  if (!vatMode) return <NonVatGuard title={isCredit ? t('cnTitle') : t('dnTitle')} />;

  const totalRows: TotalRow[] = [
    { label: t('subtotal'), value: sub },
    { label: 'ภาษีมูลค่าเพิ่ม', value: vat },
  ];

  return (
    <>
      <DocumentCreateLayout
        title={isCredit ? t('cnTitle') : t('dnTitle')}
        docMeta={docDate}
        actions={
          <>
            <button
              type="button"
              className="btn btn-ghost btn-sm"
              onClick={() => router.push(basePath)}
              disabled={isSubmitting}
            >
              {tcr('cancel')}
            </button>
            <button
              type="button"
              className="btn btn-outline btn-sm border-ink-200 text-ink-700 hover:bg-ink-75"
              disabled={isSubmitting}
              onClick={handleSubmit(async (v) => { const id = await saveDraft(v); if (id) router.push(basePath); }, invalid)}
            >
              {tc('save')}
            </button>
            <button
              type="button"
              className="btn btn-primary btn-sm"
              disabled={isSubmitting}
              onClick={handleSubmit(async (v) => { const id = await saveDraft(v); if (id) setConfirm({ id }); }, invalid)}
            >
              {t('post')}
            </button>
          </>
        }
        preview={
          <LivePreviewPane>
            <PaperDocument
              docType={cfg.docType}
              docTypeEn={cfg.docTypeEn}
              docNo="(ฉบับร่าง)"
              issueDate={docDate}
              seller={companyToSeller(company.data)}
              customer={{ name: '—' }}
              items={[{ description: reasonText?.trim() || t('reasonText'), amount: sub }]}
              summary={{ subtotal: sub, vat, total: sub + vat, vatRate: rate * 100 }}
              signRoles={cfg.signRoles}
            />
          </LivePreviewPane>
        }
      >
      <form className="space-y-6"
        onSubmit={handleSubmit(async (v) => { const id = await saveDraft(v); if (id) router.push(basePath); }, invalid)}>
        {/* ① อ้างอิงใบกำกับภาษีเดิม */}
        <SectionCard number={1} title={t('originalTi')}>
          <Controller control={control} name="originalTaxInvoiceId" render={({ field, fieldState }) => (
            <div>
              <TaxInvoicePicker
                value={field.value || null}
                status="Posted"
                ariaLabel="originalTaxInvoiceId"
                onChange={(ti) => field.onChange(ti.taxInvoiceId)}
              />
              {fieldState.error && <span className="mt-2 block text-sm text-error" data-field-error="true">ระบุใบกำกับภาษีเดิม</span>}
            </div>
          )} />
        </SectionCard>

        {/* ② ข้อมูลเอกสาร — เหตุผล + BU + วันที่ */}
        <SectionCard number={2} title={tcr('docInfo')}>
          <div className="space-y-4">
            <DateInput value={docDate} locked label={tc('date')} />
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
                {fieldState.error && <span className="text-error text-sm" data-field-error="true">{fieldState.error.message}</span>}
              </label>
            )} />
            <BusinessUnitSelector
              value={businessUnitId}
              onChange={(id) => { setBusinessUnitId(id); if (id) setBuError(false); }}
              required={buRequired}
              error={buError}
            />
          </div>
        </SectionCard>

        {/* ③ มูลค่าที่ปรับ + totals */}
        <SectionCard number={3} title={t('subtotal')}>
          <div className="space-y-4">
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
              <Controller control={control} name="adjustmentSubtotal" render={({ field }) => (
                <label className="form-control">
                  <span className="label-text">{t('subtotal')} *</span>
                  <AmountInput value={field.value} onValueChange={field.onChange} aria-label="adjustmentSubtotal" />
                </label>
              )} />
              <Controller control={control} name="taxRate" render={({ field }) => (
                <label className="form-control">
                  <span className="label-text">{t('taxRate')}</span>
                  {/* Sprint 13i C2 — locked to the referenced Posted TI's VAT rate. */}
                  {watch('originalTaxInvoiceId') > 0 ? (
                    <input
                      className="input input-bordered bg-base-200 tabular-nums"
                      value={field.value}
                      readOnly
                      disabled
                      aria-label="taxRate"
                    />
                  ) : (
                    <AmountInput value={field.value} step={0.01} onValueChange={field.onChange} aria-label="taxRate" />
                  )}
                </label>
              )} />
            </div>
            <TotalsSummaryBox
              rows={totalRows}
              grandLabel="รวมทั้งสิ้น"
              grandValue={sub + vat}
            />
          </div>
        </SectionCard>
      </form>
      </DocumentCreateLayout>

      <PostConfirmDialog
        docType={isCredit ? 'credit_note' : 'debit_note'}
        open={confirm !== null}
        busy={post.isPending}
        summary={{ customer: `TI #${watch('originalTaxInvoiceId')}`, total: sub + vat, vat }}
        recipients={[]}
        onClose={() => setConfirm(null)}
        onConfirm={async () => {
          if (!confirm) return;
          try { await post.mutateAsync(confirm.id); toast.success(tc('posted')); router.push(`${basePath}/${confirm.id}`); }
          catch { toast.error(tc('error')); }
          finally { setConfirm(null); }
        }}
      />
    </>
  );
}
