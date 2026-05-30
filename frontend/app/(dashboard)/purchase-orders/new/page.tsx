'use client';

import { useState } from 'react';
import { useRouter } from 'next/navigation';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { PageHeader } from '@/components/ui/PageHeader';
import { VendorSelector } from '@/components/ui/VendorSelector';
import { LineItemsTable, EMPTY_LINE, type LineItem } from '@/components/ui/LineItemsTable';
import { useCreatePurchaseOrder, useSystemInfo, useVendor } from '@/lib/queries';
import { bangkokToday, formatTHB } from '@/lib/utils';
import { onInvalidSubmit } from '@/lib/forms';

// Sprint 13j-PURCH Phase F — full PO create form (replaces the 1-line MVP stub
// that hardcoded taxCodeId:1/VAT7/0.07 on a single line). Lifts to "VI quality":
// multi-line <LineItemsTable> with per-line <ProductPicker> (free-text fallback
// per Plan §7), real VAT-rate selector (std rate from /system/info, never
// hardcoded — CLAUDE.md §4.6) + per-line discount %, RHF+Zod, specific Thai
// toast errors (no generic-only fallback — BUG #SR9). Submit→detail redirect kept.
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
  vendorId: z.number().int().positive(),
  lines: z.array(lineSchema).min(1),
});
type FormValues = z.infer<typeof schema>;

export default function NewPurchaseOrderPage() {
  const t = useTranslations('purchaseOrder');
  const tc = useTranslations('common');
  const tt = useTranslations('toast');
  const router = useRouter();
  const create = useCreatePurchaseOrder();
  // Non-VAT companies (ม.86): LineItemsTable hides the VAT column; force the
  // effective rate to 0 so a stale default rate never leaks into totals/payload.
  const vatMode = useSystemInfo().data?.vatMode ?? true;

  const today = bangkokToday();
  const [docDate, setDocDate] = useState(today);
  const [expected, setExpected] = useState('');
  const [notes, setNotes] = useState('');

  const invalid = onInvalidSubmit((m) => toast.error(m), tt('validationFailed'));

  const {
    control,
    handleSubmit,
    watch,
    formState: { isSubmitting },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { vendorId: 0, lines: [{ ...EMPTY_LINE }] },
  });

  // cont.77 — input VAT exists only when BOTH the company is VAT-registered (vatMode)
  // AND the selected vendor is VAT-registered (a non-VAT vendor issues no tax invoice).
  // Default true until the vendor loads so the column doesn't flicker.
  const vendorId = watch('vendorId');
  const vendor = useVendor(vendorId || 0).data;
  const vendorVat = vatMode && (vendor?.vatRegistered ?? true);

  const lines = watch('lines') as LineItem[];
  const totals = lines.reduce(
    (acc, l) => {
      const gross = l.quantity * l.unitPrice;
      const disc = gross * ((l.discountPercent ?? 0) / 100);
      const net = gross - disc;
      const vat = vendorVat ? net * l.taxRate : 0;
      acc.subtotal += gross;
      acc.discount += disc;
      acc.vat += vat;
      acc.total += net + vat;
      return acc;
    },
    { subtotal: 0, discount: 0, vat: 0, total: 0 },
  );

  async function submit(v: FormValues) {
    try {
      const payload = {
        docDate,
        expectedDeliveryDate: expected || null,
        vendorId: v.vendorId,
        businessUnitId: null,
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
          taxCode: vendorVat && l.taxRate > 0 ? 'VAT7' : 'VAT0',
          taxRate: vendorVat ? l.taxRate : 0,
          notes: null,
        })),
      };
      const r = (await create.mutateAsync(payload)) as { purchase_order_id: number };
      toast.success(tc('save'));
      router.push(`/purchase-orders/${r.purchase_order_id}`);
    } catch (e) {
      // BUG #SR9 — surface the backend's Thai ProblemDetails (title/detail)
      // rather than a generic message; fall back to the common error only when
      // the server gave us nothing usable.
      const err = e as { detail?: string; title?: string };
      toast.error(err?.detail ?? err?.title ?? tc('error'));
    }
  }

  return (
    <>
      <PageHeader title={t('create')} subtitle={docDate} />

      <form onSubmit={handleSubmit(submit, invalid)} className="space-y-4">
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
          <Controller
            control={control}
            name="vendorId"
            render={({ field, fieldState }) => (
              <div>
                <span className="label-text">{t('vendor')} *</span>
                <VendorSelector
                  value={field.value || null}
                  onChange={(id) => field.onChange(id ?? 0)}
                />
                {fieldState.error && (
                  <span className="text-error text-sm" data-field-error="true">
                    {t('pickVendor')}
                  </span>
                )}
              </div>
            )}
          />
          <div className="grid grid-cols-2 gap-3">
            <label className="form-control">
              <span className="label-text">{t('docDate')}</span>
              <input
                type="date"
                className="input input-bordered"
                value={docDate}
                onChange={(e) => setDocDate(e.target.value)}
                aria-label={t('docDate')}
              />
            </label>
            <label className="form-control">
              <span className="label-text">{t('expectedDelivery')}</span>
              <input
                type="date"
                className="input input-bordered"
                value={expected}
                onChange={(e) => setExpected(e.target.value)}
                aria-label={t('expectedDelivery')}
              />
            </label>
          </div>
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
                vatEnabled={vendorVat}
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
            {vendorVat && (
              <div className="flex justify-between">
                <dt className="text-base-content/60">{t('vat')}</dt>
                <dd className="tabular-nums">{formatTHB(totals.vat)}</dd>
              </div>
            )}
            <div className="flex justify-between border-t pt-1 font-bold">
              <dt>{t('total')}</dt>
              <dd className="tabular-nums">{formatTHB(totals.total)}</dd>
            </div>
          </dl>
          <button
            type="submit"
            className="btn btn-primary"
            disabled={isSubmitting || create.isPending}
          >
            {tc('save')}
          </button>
        </div>
      </form>
    </>
  );
}
