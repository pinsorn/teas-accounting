'use client';

import { useState } from 'react';
import { useRouter } from 'next/navigation';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { BusinessUnitSelector } from '@/components/ui/BusinessUnitSelector';
import { LineItemsTable, EMPTY_LINE, type LineItem } from '@/components/ui/LineItemsTable';
import { TaxInvoicePicker, type TaxInvoiceLite } from '@/components/forms/TaxInvoicePicker';
import { useCreateBillingNote, useBillingNoteAction, useCompanyBuSetting, useCompanyProfile, useSystemInfo } from '@/lib/queries';
import { bangkokToday } from '@/lib/utils';
import { onInvalidSubmit, scrollToFirstError } from '@/lib/forms';
import { PaperDocument } from '@/components/paper/PaperDocument';
import { PAPER_DOC, companyToSeller } from '@/lib/paper-doc-config';
import { buildPaperItems, buildPaperSummary } from '@/lib/paper-line-totals';
import { DocumentCreateLayout } from '@/components/create/DocumentCreateLayout';
import { SectionCard } from '@/components/create/SectionCard';
import { PartySelectBox } from '@/components/create/PartySelectBox';
import { TotalsSummaryBox, type TotalRow } from '@/components/create/TotalsSummaryBox';
import { LivePreviewPane } from '@/components/create/LivePreviewPane';

// Sprint 13h P6.2 — Billing Note (ใบแจ้งหนี้/ใบวางบิล) create form.
// Closely mirrors QuotationForm. Saves Draft; "ออกใบแจ้งหนี้" issues immediately.
// cont.80 — restyled into the shared DocumentCreateLayout (fields/payload unchanged).

const lineSchema = z.object({
  descriptionTh: z.string().min(1),
  quantity: z.number().positive(),
  unitPrice: z.number().min(0),
  taxRate: z.number().min(0).max(1),
  productId: z.number().nullable().optional(),
  productCode: z.string().nullable().optional(),
  // Kept in the schema so zod doesn't strip the picked product's type before
  // submit — it drives WHT classification (SERVICE → withholdable) downstream.
  productType: z.string().optional(),
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

const FORM_ID = 'billing-note-create-form';

export function BillingNoteForm() {
  const router = useRouter();
  const t = useTranslations('billingNote');
  const tc = useTranslations('common');
  const tt = useTranslations('toast');
  const tcr = useTranslations('create');
  const create = useCreateBillingNote();
  const action = useBillingNoteAction();
  const company = useCompanyProfile();
  const buSetting = useCompanyBuSetting();
  const buRequired = buSetting.data?.requiresBusinessUnit ?? false;
  // Non-VAT company (ม.86): no VAT on the Invoice — the hidden line rate must not
  // leak into totals/payload/preview. Mirrors QuotationForm.
  const vatMode = useSystemInfo().data?.vatMode ?? true;

  const today = bangkokToday();
  const [docDate, setDocDate] = useState(today);
  const [dueDate, setDueDate] = useState(plusDays(today, 30));
  const [businessUnitId, setBusinessUnitId] = useState<number | null>(null);
  const [buError, setBuError] = useState(false);
  const [notes, setNotes] = useState('');
  const [customerLabel, setCustomerLabel] = useState('');
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
      const vat = vatMode ? net * l.taxRate : 0;
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
          taxCode: vatMode && l.taxRate > 0 ? 'VAT7' : 'VAT0',
          taxRate: vatMode ? l.taxRate : 0,
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

  const submitSave = handleSubmit(async (v) => {
    const id = await createBillingNote(v);
    if (id) {
      toast.success(tc('draftSaved'));
      router.push('/invoices');
    }
  }, invalid);
  const submitIssue = handleSubmit(async (v) => {
    const id = await createBillingNote(v);
    if (!id) return;
    try {
      await action.mutateAsync({ id, action: 'issue' });
      toast.success(t('issued'));
      router.push(`/invoices/${id}`);
    } catch (e) {
      toast.error((e as { detail?: string })?.detail ?? tc('error'));
    }
  }, invalid);

  const totalRows: TotalRow[] = [
    { label: t('subtotal'), value: totals.subtotal },
    { label: t('discount'), value: -totals.discount, muted: true },
    ...(vatMode ? [{ label: t('vat'), value: totals.vat }] : []),
  ];

  return (
    <DocumentCreateLayout
      title={t('create')}
      docMeta={docDate}
      actions={
        <>
          <button
            type="button"
            className="btn btn-ghost btn-sm"
            onClick={() => router.push('/invoices')}
            disabled={isSubmitting}
          >
            {tcr('cancel')}
          </button>
          <button
            type="button"
            data-testid="bn-save-draft"
            className="btn btn-outline btn-sm border-ink-200 text-ink-700 hover:bg-ink-75"
            onClick={submitSave}
            disabled={isSubmitting}
          >
            {t('saveDraft')}
          </button>
          <button
            type="button"
            data-testid="bn-issue"
            className="btn btn-primary btn-sm"
            onClick={submitIssue}
            disabled={isSubmitting}
          >
            {t('issue')}
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
            validUntil={dueDate}
            validUntilLabel={cfg.validUntilLabel}
            seller={companyToSeller(company.data)}
            customer={{ name: customerLabel || '—' }}
            items={buildPaperItems(lines)}
            summary={buildPaperSummary(lines, vatMode)}
            notes={notes || null}
            signRoles={cfg.signRoles}
          />
        </LivePreviewPane>
      }
    >
      <form id={FORM_ID} onSubmit={submitSave} className="space-y-6">
        {/* ① ลูกค้า */}
        <Controller
          control={control}
          name="customerId"
          render={({ field, fieldState }) => (
            <SectionCard number={1} title={tc('customer')}>
              <PartySelectBox
                kind="customer"
                party={field.value || null}
                onChange={(id, label) => {
                  field.onChange(id);
                  setCustomerLabel(label);
                }}
              />
              {fieldState.error && (
                <span className="mt-2 block text-sm text-error" data-field-error="true">
                  {t('pickCustomer')}
                </span>
              )}
            </SectionCard>
          )}
        />

        {/* ② ข้อมูลเอกสาร + อ้างอิงใบกำกับภาษี */}
        <SectionCard number={2} title={tcr('docInfo')}>
          <div className="space-y-4">
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
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
              <div className="sm:col-span-2">
                <BusinessUnitSelector
                  value={businessUnitId}
                  onChange={(id) => { setBusinessUnitId(id); if (id) setBuError(false); }}
                  required={buRequired}
                  error={buError}
                />
              </div>
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
          </div>
        </SectionCard>

        {/* ③ รายการ + totals */}
        <SectionCard number={3} title={tcr('lines')} rightMeta={tcr('lineCount', { n: lines.length })}>
          <Controller
            control={control}
            name="lines"
            render={({ field, fieldState }) => (
              <div className="space-y-4">
                <LineItemsTable
                  value={field.value as LineItem[]}
                  onChange={field.onChange}
                  enableProduct
                  hideHeading
                  purpose="sale"
                  businessUnitId={businessUnitId}
                />
                {fieldState.error && (
                  <span className="block text-sm text-error" data-field-error="true">
                    {tt('lineRequired')}
                  </span>
                )}
                <TotalsSummaryBox
                  rows={totalRows}
                  grandLabel={t('grandTotal')}
                  grandValue={totals.total}
                />
              </div>
            )}
          />
        </SectionCard>

        {/* ④ หมายเหตุ */}
        <SectionCard number={4} title={t('notes')}>
          <textarea
            className="textarea textarea-bordered w-full"
            rows={2}
            value={notes}
            onChange={(e) => setNotes(e.target.value)}
            aria-label={t('notes')}
          />
        </SectionCard>
      </form>
    </DocumentCreateLayout>
  );
}
