'use client';

import { useState } from 'react';
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
import { TaxInvoicePicker, type TaxInvoiceLite } from '@/components/forms/TaxInvoicePicker';
import { useCreateBillingNote, useBillingNoteAction, useCompanyBuSetting, useCompanyProfile } from '@/lib/queries';
import { bangkokToday, formatTHB } from '@/lib/utils';
import { onInvalidSubmit, scrollToFirstError } from '@/lib/forms';
import { PaperDocument } from '@/components/paper/PaperDocument';
import { PAPER_DOC, companyToSeller } from '@/lib/paper-doc-config';
import { buildPaperItems, buildPaperSummary } from '@/lib/paper-line-totals';

// Sprint 13h P6.2 — Billing Note (ใบแจ้งหนี้/ใบวางบิล) create form.
// Closely mirrors QuotationForm. Saves Draft; "ออกใบแจ้งหนี้" issues immediately.

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

export function BillingNoteForm() {
  const router = useRouter();
  const t = useTranslations('billingNote');
  const tc = useTranslations('common');
  const tt = useTranslations('toast');
  const create = useCreateBillingNote();
  const action = useBillingNoteAction();
  const company = useCompanyProfile();
  const buSetting = useCompanyBuSetting();
  const buRequired = buSetting.data?.requiresBusinessUnit ?? false;

  const today = bangkokToday();
  const [docDate, setDocDate] = useState(today);
  const [dueDate, setDueDate] = useState(plusDays(today, 30));
  const [businessUnitId, setBusinessUnitId] = useState<number | null>(null);
  const [buError, setBuError] = useState(false);
  const [notes, setNotes] = useState('');
  // Sprint 13i C7 — TaxInvoices grouped into this BN (multi-select via the picker).
  const [selectedTis, setSelectedTis] = useState<TaxInvoiceLite[]>([]);

  const invalid = onInvalidSubmit((m) => toast.error(m), tt('validationFailed'));

  const {
    control,
    handleSubmit,
    watch,
    formState: { isSubmitting },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { customerId: 0, lines: [{ ...EMPTY_LINE }] },
  });

  const customerId = watch('customerId');
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

  async function createBillingNote(v: FormValues): Promise<number | null> {
    if (buRequired && businessUnitId === null) {
      setBuError(true);
      toast.error(tt('validationFailed'));
      requestAnimationFrame(scrollToFirstError);
      return null;
    }
    setBuError(false);
    try {
      const res = (await create.mutateAsync({
        docDate,
        dueDate,
        customerId: v.customerId,
        businessUnitId,
        quotationId: null,
        taxInvoiceIds: selectedTis.length ? selectedTis.map((ti) => ti.taxInvoiceId) : null,
        currencyCode: 'THB',
        exchangeRate: 1,
        notes: notes.trim() || null,
        internalNotes: null,
        lines: v.lines.map((l) => ({
          productId: l.productId ?? null,
          taxInvoiceId: null,
          descriptionTh: l.descriptionTh,
          quantity: l.quantity,
          uomText: l.uomText?.trim() || 'หน่วย',
          unitPrice: l.unitPrice,
          discountPercent: l.discountPercent ?? 0,
          taxCodeId: 1,
          taxCode: l.taxRate > 0 ? 'VAT7' : 'VAT0',
          taxRate: l.taxRate,
          productType: (l as { productType?: string | null }).productType ?? 'GOOD',
        })),
      })) as { billing_note_id: number };
      return res.billing_note_id;
    } catch (e) {
      toast.error((e as { detail?: string })?.detail ?? tc('error'));
      return null;
    }
  }

  const cfg = PAPER_DOC['billing-note'];

  return (
    <>
      <PageHeader title={t('create')} subtitle={docDate} />
      <div className="create-grid">
      <form
        className="space-y-6"
        onSubmit={handleSubmit(async (v) => {
          const id = await createBillingNote(v);
          if (id) {
            toast.success(tc('draftSaved'));
            router.push('/invoices');
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
            <span className="label-text">{t('dueDate')} *</span>
            <input
              type="date"
              className="input input-bordered"
              value={dueDate}
              onChange={(e) => setDueDate(e.target.value)}
              aria-label={t('dueDate')}
            />
          </label>
        </div>

        {/* Sprint 13i C7 — group posted TaxInvoices into this BN (multi-select). */}
        <div className="form-control">
          <span className="label-text">{t('taxInvoices')}</span>
          <TaxInvoicePicker
            value={null}
            customerId={customerId || null}
            status="Posted"
            disabled={!customerId}
            disabledHint={t('pickCustomerFirst')}
            ariaLabel={t('taxInvoices')}
            onChange={(ti) =>
              setSelectedTis((prev) =>
                prev.some((p) => p.taxInvoiceId === ti.taxInvoiceId) ? prev : [...prev, ti],
              )
            }
          />
          {selectedTis.length > 0 && (
            <div className="mt-2 flex flex-wrap gap-2" data-testid="bn-ti-chips">
              {selectedTis.map((ti) => (
                <span key={ti.taxInvoiceId} className="badge badge-outline gap-1">
                  {ti.docNo ?? `#${ti.taxInvoiceId}`}
                  <button
                    type="button"
                    aria-label={`${tc('remove')} ${ti.docNo ?? ti.taxInvoiceId}`}
                    onClick={() =>
                      setSelectedTis((prev) => prev.filter((p) => p.taxInvoiceId !== ti.taxInvoiceId))
                    }
                  >
                    ×
                  </button>
                </span>
              ))}
            </div>
          )}
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
            <button type="submit" data-testid="bn-save-draft" className="btn btn-ghost" disabled={isSubmitting}>
              {t('saveDraft')}
            </button>
            <button
              type="button"
              data-testid="bn-issue"
              className="btn btn-primary"
              disabled={isSubmitting}
              onClick={handleSubmit(async (v) => {
                const id = await createBillingNote(v);
                if (!id) return;
                try {
                  await action.mutateAsync({ id, action: 'issue' });
                  toast.success(t('issued'));
                  router.push(`/invoices/${id}`);
                } catch (e) {
                  toast.error((e as { detail?: string })?.detail ?? tc('error'));
                }
              }, invalid)}
            >
              {t('issue')}
            </button>
          </div>
        </div>
      </form>

      <div className="preview-side">
        <PaperDocument
          docType={cfg.docType}
          docTypeEn={cfg.docTypeEn}
          docNo="(ฉบับร่าง)"
          issueDate={docDate}
          validUntil={dueDate}
          validUntilLabel={cfg.validUntilLabel}
          seller={companyToSeller(company.data)}
          customer={{ name: '—' }}
          items={buildPaperItems(lines)}
          summary={buildPaperSummary(lines)}
          notes={notes || null}
          signRoles={cfg.signRoles}
        />
      </div>
      </div>
    </>
  );
}
