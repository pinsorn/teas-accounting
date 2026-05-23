'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { PageHeader } from '@/components/ui/PageHeader';
import { CustomerSelector } from '@/components/ui/CustomerSelector';
import { BusinessUnitSelector } from '@/components/ui/BusinessUnitSelector';
import { LineItemsTable, EMPTY_LINE, type LineItem } from '@/components/ui/LineItemsTable';
import { useCreateQuotation, useUpdateQuotation, useQuotationAction, useCompanyBuSetting, useCompanyProfile } from '@/lib/queries';
import type { QuotationDetail } from '@/lib/types';
import { bangkokToday, formatTHB } from '@/lib/utils';
import { onInvalidSubmit, scrollToFirstError } from '@/lib/forms';
import { PaperDocument } from '@/components/paper/PaperDocument';
import { PAPER_DOC, companyToSeller } from '@/lib/paper-doc-config';

const lineSchema = z.object({
  descriptionTh: z.string().min(1),
  quantity: z.number().positive(),
  unitPrice: z.number().min(0),
  taxRate: z.number().min(0).max(1),
  productId: z.number().nullable().optional(),
  productCode: z.string().nullable().optional(),
  uomText: z.string().optional(),
  discountPercent: z.number().optional(),
});
const schema = z.object({
  customerId: z.number().int().positive(),
  lines: z.array(lineSchema).min(1),
});
type FormValues = z.infer<typeof schema>;

function plusDays(iso: string, days: number): string {
  const d = new Date(iso + 'T00:00:00');
  d.setDate(d.getDate() + days);
  return d.toISOString().slice(0, 10);
}

// Sprint 13e P2 — full Quotation create form (replaces the 1-line MVP stub).
// Multi-line + product picker + VAT/discount preview + Draft/Issue workflow.
// Sprint 13i C1 — `edit` prop reuses this form for /quotations/[id]/edit
// (Draft-only edit; saves via PUT and returns to the detail page).
export function QuotationForm({ edit }: { edit?: QuotationDetail } = {}) {
  const router = useRouter();
  const t = useTranslations('quotation');
  const tc = useTranslations('common');
  const tt = useTranslations('toast');
  const isEdit = edit != null;
  const create = useCreateQuotation();
  const update = useUpdateQuotation();
  const action = useQuotationAction();
  const company = useCompanyProfile();
  const buSetting = useCompanyBuSetting();
  const buRequired = buSetting.data?.requiresBusinessUnit ?? false;

  const today = bangkokToday();
  const [docDate, setDocDate] = useState(edit?.docDate ?? today);
  const [validUntil, setValidUntil] = useState(edit?.validUntilDate ?? plusDays(today, 30));
  const [businessUnitId, setBusinessUnitId] = useState<number | null>(edit?.businessUnitId ?? null);
  const [buError, setBuError] = useState(false);
  const [notes, setNotes] = useState(edit?.notes ?? '');

  const invalid = onInvalidSubmit((m) => toast.error(m), tt('validationFailed'));

  const toLine = (l: QuotationDetail['lines'][number]): LineItem => ({
    descriptionTh: l.descriptionTh,
    quantity: l.quantity,
    unitPrice: l.unitPrice,
    taxRate: l.lineAmount > 0 ? Math.round((l.taxAmount / l.lineAmount) * 100) / 100 : 0.07,
    productId: l.productId,
    productCode: l.productCode,
    uomText: l.uomText,
  });

  const {
    control,
    handleSubmit,
    watch,
    reset,
    formState: { isSubmitting },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: edit
      ? { customerId: edit.customerId, lines: edit.lines.map(toLine) }
      : { customerId: 0, lines: [{ ...EMPTY_LINE }] },
  });

  // Re-hydrate if the edited quotation arrives/changes after first render.
  useEffect(() => {
    if (!edit) return;
    reset({ customerId: edit.customerId, lines: edit.lines.map(toLine) });
    setDocDate(edit.docDate);
    setValidUntil(edit.validUntilDate);
    setBusinessUnitId(edit.businessUnitId ?? null);
    setNotes(edit.notes ?? '');
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [edit?.quotationId]);

  const lines = watch('lines') as LineItem[];
  const totals = lines.reduce(
    (acc, l) => {
      const gross = l.quantity * l.unitPrice;
      const disc = gross * ((l.discountPercent ?? 0) / 100);
      const net = gross - disc;
      const vat = net * l.taxRate;
      acc.subtotal += gross;
      acc.discount += disc;
      acc.vat += vat;
      acc.total += net + vat;
      return acc;
    },
    { subtotal: 0, discount: 0, vat: 0, total: 0 },
  );

  async function createQuotation(v: FormValues): Promise<number | null> {
    if (buRequired && businessUnitId === null) {
      setBuError(true);
      toast.error(tt('validationFailed'));
      requestAnimationFrame(scrollToFirstError);
      return null;
    }
    setBuError(false);
    const payload = {
      docDate,
      validUntilDate: validUntil,
      customerId: v.customerId,
      businessUnitId,
      currencyCode: 'THB',
      exchangeRate: 1,
      notes: notes.trim() || null,
      internalNotes: null,
      lines: v.lines.map((l) => ({
        productId: l.productId ?? null,
        descriptionTh: l.descriptionTh,
        quantity: l.quantity,
        uomText: l.uomText?.trim() || 'หน่วย',
        unitPrice: l.unitPrice,
        discountPercent: l.discountPercent ?? 0,
        taxCodeId: 1,
        taxCode: l.taxRate > 0 ? 'VAT7' : 'VAT0',
        taxRate: l.taxRate,
      })),
    };
    try {
      if (isEdit && edit) {
        await update.mutateAsync({ id: edit.quotationId, req: payload });
        return edit.quotationId;
      }
      const res = (await create.mutateAsync(payload)) as { quotation_id: number };
      return res.quotation_id;
    } catch (e) {
      toast.error((e as { detail?: string })?.detail ?? tc('error'));
      return null;
    }
  }

  const cfg = PAPER_DOC.quotation;

  return (
    <>
      <PageHeader title={isEdit ? t('editTitle') : t('create')} subtitle={edit?.docNo ?? docDate} />
      <div className="create-grid">
      <form
        className="space-y-6"
        onSubmit={handleSubmit(async (v) => {
          const id = await createQuotation(v);
          if (id) {
            toast.success(tc('save'));
            router.push(isEdit ? `/quotations/${id}` : '/quotations');
          }
        }, invalid)}
      >
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
          <Controller
            control={control}
            name="customerId"
            render={({ field, fieldState }) => (
              <div>
                <CustomerSelector
                  value={field.value || null}
                  onChange={(id) => field.onChange(id)}
                />
                {fieldState.error && (
                  <span className="text-error text-sm" data-field-error="true">
                    {t('pickCustomer')}
                  </span>
                )}
              </div>
            )}
          />
          <BusinessUnitSelector
            value={businessUnitId}
            onChange={(id) => { setBusinessUnitId(id); if (id) setBuError(false); }}
            required={buRequired}
            error={buError}
          />
          <label className="form-control">
            <span className="label-text">{t('docDate')} *</span>
            <input
              type="date"
              className="input input-bordered"
              value={docDate}
              onChange={(e) => setDocDate(e.target.value)}
              aria-label={t('docDate')}
            />
          </label>
          <label className="form-control">
            <span className="label-text">{t('validUntil')} *</span>
            <input
              type="date"
              className="input input-bordered"
              value={validUntil}
              onChange={(e) => setValidUntil(e.target.value)}
              aria-label={t('validUntil')}
            />
          </label>
        </div>

        <Controller
          control={control}
          name="lines"
          render={({ field, fieldState }) => (
            <div>
              <LineItemsTable
                value={field.value as LineItem[]}
                onChange={field.onChange}
                enableProduct
              />
              {fieldState.error && (
                <span className="text-error text-sm" data-field-error="true">
                  {tt('lineRequired')}
                </span>
              )}
            </div>
          )}
        />

        <label className="form-control">
          <span className="label-text">{t('notes')}</span>
          <textarea
            className="textarea textarea-bordered"
            rows={2}
            value={notes}
            onChange={(e) => setNotes(e.target.value)}
            aria-label={t('notes')}
          />
        </label>

        <div className="flex items-end justify-between">
          <dl className="w-64 space-y-1 text-sm">
            <div className="flex justify-between">
              <dt className="text-base-content/60">{t('subtotal')}</dt>
              <dd className="tabular-nums">{formatTHB(totals.subtotal)}</dd>
            </div>
            <div className="flex justify-between">
              <dt className="text-base-content/60">{t('discount')}</dt>
              <dd className="tabular-nums">-{formatTHB(totals.discount)}</dd>
            </div>
            <div className="flex justify-between">
              <dt className="text-base-content/60">{t('vat')}</dt>
              <dd className="tabular-nums">{formatTHB(totals.vat)}</dd>
            </div>
            <div className="flex justify-between font-bold">
              <dt>{t('grandTotal')}</dt>
              <dd className="tabular-nums">{formatTHB(totals.total)}</dd>
            </div>
          </dl>
          <div className="flex gap-2">
            {isEdit ? (
              <button type="submit" className="btn btn-primary" disabled={isSubmitting}>
                {tc('save')}
              </button>
            ) : (
              <>
                <button type="submit" className="btn btn-ghost" disabled={isSubmitting}>
                  {t('saveDraft')}
                </button>
                <button
                  type="button"
                  className="btn btn-primary"
                  disabled={isSubmitting}
                  onClick={handleSubmit(async (v) => {
                    const id = await createQuotation(v);
                    if (!id) return;
                    try {
                      await action.mutateAsync({ id, action: 'send' });
                      toast.success(t('issued'));
                      router.push(`/quotations/${id}`);
                    } catch (e) {
                      toast.error((e as { detail?: string })?.detail ?? tc('error'));
                    }
                  }, invalid)}
                >
                  {t('issue')}
                </button>
              </>
            )}
          </div>
        </div>
      </form>

      <div className="preview-side">
        <PaperDocument
          docType={cfg.docType}
          docTypeEn={cfg.docTypeEn}
          docNo={edit?.docNo ?? '(ฉบับร่าง)'}
          issueDate={docDate}
          validUntil={validUntil}
          validUntilLabel={cfg.validUntilLabel}
          seller={companyToSeller(company.data)}
          customer={{ name: '—' }}
          items={lines.map((l) => ({
            description: l.descriptionTh,
            quantity: l.quantity,
            unit: l.uomText,
            unitPrice: l.unitPrice,
            discountPercent: l.discountPercent,
            amount: l.quantity * l.unitPrice * (1 - (l.discountPercent ?? 0) / 100),
          }))}
          summary={{
            subtotal: totals.subtotal,
            discount: totals.discount,
            vat: totals.vat,
            total: totals.total,
          }}
          notes={notes || null}
          signRoles={cfg.signRoles}
        />
      </div>
      </div>
    </>
  );
}
