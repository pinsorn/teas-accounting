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
import { useCreateSalesOrder, usePostSalesOrder, useCompanyBuSetting, useCompanyProfile, useSystemInfo } from '@/lib/queries';
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

const FORM_ID = 'sales-order-create-form';

// Sprint 13e P4 — Sales Order create form (replaces the P1 routing stub).
// Same shape as QuotationForm; an SO is a "confirmed quotation". Manual
// /sales-orders/new leaves fromQuotationId null — Q→SO conversion is the
// QuotationService.ConvertToSalesOrder path (detail page action).
// cont.80 — restyled into the shared DocumentCreateLayout (fields/payload unchanged).
export function SalesOrderForm() {
  const router = useRouter();
  const t = useTranslations('salesOrder');
  const tc = useTranslations('common');
  const tt = useTranslations('toast');
  const tcr = useTranslations('create');
  const create = useCreateSalesOrder();
  const post = usePostSalesOrder();
  const company = useCompanyProfile();
  const buSetting = useCompanyBuSetting();
  const buRequired = buSetting.data?.requiresBusinessUnit ?? false;
  // Non-VAT company (ม.86): no VAT on the SO. Don't let the hidden line rate leak.
  const vatMode = useSystemInfo().data?.vatMode ?? true;

  const [docDate, setDocDate] = useState(bangkokToday());
  const [expectedDelivery, setExpectedDelivery] = useState('');
  const [businessUnitId, setBusinessUnitId] = useState<number | null>(null);
  const [buError, setBuError] = useState(false);
  const [notes, setNotes] = useState('');
  const [customerLabel, setCustomerLabel] = useState('');

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

  const lines = watch('lines') as LineItem[];
  const summary = buildPaperSummary(lines, vatMode);
  const cfg = PAPER_DOC['sales-order'];

  async function createSalesOrder(v: FormValues): Promise<number | null> {
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
        expectedDeliveryDate: expectedDelivery || null,
        customerId: v.customerId,
        businessUnitId,
        currencyCode: 'THB',
        exchangeRate: 1,
        notes: notes.trim() || null,
        fromQuotationId: null,
        lines: v.lines.map((l) => ({
          productId: l.productId ?? null,
          descriptionTh: l.descriptionTh,
          quantity: l.quantity,
          uomText: l.uomText?.trim() || 'หน่วย',
          unitPrice: l.unitPrice,
          discountPercent: l.discountPercent ?? 0,
          taxCodeId: 1,
          taxCode: vatMode && l.taxRate > 0 ? 'VAT7' : 'VAT0',
          taxRate: vatMode ? l.taxRate : 0,
        })),
      })) as { sales_order_id: number };
      return res.sales_order_id;
    } catch (e) {
      toast.error((e as { detail?: string })?.detail ?? tc('error'));
      return null;
    }
  }

  const submitSave = handleSubmit(async (v) => {
    const id = await createSalesOrder(v);
    if (id) {
      toast.success(tc('save'));
      router.push('/sales-orders');
    }
  }, invalid);
  const submitConfirm = handleSubmit(async (v) => {
    const id = await createSalesOrder(v);
    if (!id) return;
    try {
      await post.mutateAsync(id);
      toast.success(t('confirmed'));
      router.push('/sales-orders');
    } catch (e) {
      toast.error((e as { detail?: string })?.detail ?? tc('error'));
    }
  }, invalid);

  const totalRows: TotalRow[] = [
    { label: t('subtotal'), value: summary.subtotal },
    ...(vatMode ? [{ label: t('vat'), value: summary.vat }] : []),
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
            onClick={() => router.push('/sales-orders')}
            disabled={isSubmitting}
          >
            {tcr('cancel')}
          </button>
          <button
            type="button"
            className="btn btn-outline btn-sm border-ink-200 text-ink-700 hover:bg-ink-75"
            onClick={submitSave}
            disabled={isSubmitting}
          >
            {t('saveDraft')}
          </button>
          <button
            type="button"
            className="btn btn-primary btn-sm"
            onClick={submitConfirm}
            disabled={isSubmitting}
          >
            {t('confirm')}
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
            customer={{ name: customerLabel || '—' }}
            items={buildPaperItems(lines)}
            summary={summary}
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

        {/* ② ข้อมูลเอกสาร */}
        <SectionCard number={2} title={tcr('docInfo')}>
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
              <span className="label-text">{t('expectedDelivery')}</span>
              <input
                type="date"
                className="input input-bordered"
                value={expectedDelivery}
                onChange={(e) => setExpectedDelivery(e.target.value)}
                aria-label={t('expectedDelivery')}
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
                  grandValue={summary.total}
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
