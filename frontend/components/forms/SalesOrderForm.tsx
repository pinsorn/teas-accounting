'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { BusinessUnitSelector } from '@/components/ui/BusinessUnitSelector';
import { LineItemsTable, EMPTY_LINE, type LineItem } from '@/components/ui/LineItemsTable';
import { useCreateSalesOrder, useUpdateSalesOrder, usePostSalesOrder, useCompanyBuSetting, useCompanyProfile, useSystemInfo } from '@/lib/queries';
import type { SalesOrderDetail } from '@/lib/types';
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
// S15 — `edit` prop reuses this form for /sales-orders/[id]/edit (Draft-only edit;
// saves via PUT and returns to the detail page), mirroring QuotationForm's `edit`
// prop. fromQuotationId is preserved as-is on edit (the backend UpdateDraftAsync
// doesn't touch it either — see SalesOrderDeliveryServices.cs).
// KNOWN GAP (flagged to Fable, not fixed here — FE-only blast radius): unlike
// QuotationDetail/BillingNoteDetail, `SalesOrderDetail` carries no
// ExpectedDeliveryDate/Notes at all (SalesChainDtos.cs:94-102), so the edit form
// can't know the SO's current values for those two fields — they start EMPTY in
// edit mode and a PUT always fully replaces them (no partial-patch API). Rather
// than ship a silent wipe, both fields carry an explicit "will replace, not
// preserve" hint in edit mode. Needs a backend DTO fix to truly round-trip.
export function SalesOrderForm({ edit }: { edit?: SalesOrderDetail } = {}) {
  const router = useRouter();
  const t = useTranslations('salesOrder');
  const tc = useTranslations('common');
  const tt = useTranslations('toast');
  const tcr = useTranslations('create');
  const isEdit = edit != null;
  const create = useCreateSalesOrder();
  const update = useUpdateSalesOrder();
  const post = usePostSalesOrder();
  const company = useCompanyProfile();
  const buSetting = useCompanyBuSetting();
  const buRequired = buSetting.data?.requiresBusinessUnit ?? false;
  // Non-VAT company (ม.86): no VAT on the SO. Don't let the hidden line rate leak.
  const vatMode = useSystemInfo().data?.vatMode ?? true;

  const [docDate, setDocDate] = useState(edit?.docDate ?? bangkokToday());
  // expectedDeliveryDate/notes: SalesOrderDetail doesn't return either (see KNOWN
  // GAP note above) — edit mode necessarily starts these empty, not preserved.
  const [expectedDelivery, setExpectedDelivery] = useState('');
  const [businessUnitId, setBusinessUnitId] = useState<number | null>(edit?.businessUnitId ?? null);
  const [buError, setBuError] = useState(false);
  const [notes, setNotes] = useState('');
  const [customerLabel, setCustomerLabel] = useState(edit?.customerName ?? '');

  const invalid = onInvalidSubmit((m) => toast.error(m), tt('validationFailed'));

  const toLine = (l: SalesOrderDetail['lines'][number]): LineItem => ({
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

  // Re-hydrate if the edited sales order arrives/changes after first render.
  // expectedDeliveryDate/notes intentionally NOT re-seeded — see KNOWN GAP note above.
  useEffect(() => {
    if (!edit) return;
    reset({ customerId: edit.customerId, lines: edit.lines.map(toLine) });
    setDocDate(edit.docDate);
    setBusinessUnitId(edit.businessUnitId ?? null);
    setCustomerLabel(edit.customerName ?? '');
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [edit?.salesOrderId]);

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
    const payload = {
      docDate,
      expectedDeliveryDate: expectedDelivery || null,
      customerId: v.customerId,
      businessUnitId,
      currencyCode: 'THB',
      exchangeRate: 1,
      notes: notes.trim() || null,
      fromQuotationId: isEdit ? (edit?.quotationId ?? null) : null,
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
    };
    try {
      if (isEdit && edit) {
        await update.mutateAsync({ id: edit.salesOrderId, req: payload });
        return edit.salesOrderId;
      }
      const res = (await create.mutateAsync(payload)) as { sales_order_id: number };
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
      router.push(isEdit ? `/sales-orders/${id}` : '/sales-orders');
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
      title={isEdit ? t('editTitle') : t('create')}
      docMeta={edit?.docNo ?? docDate}
      actions={
        <>
          <button
            type="button"
            className="btn btn-ghost btn-sm"
            onClick={() => router.push(isEdit && edit ? `/sales-orders/${edit.salesOrderId}` : '/sales-orders')}
            disabled={isSubmitting}
          >
            {tcr('cancel')}
          </button>
          {isEdit ? (
            <button
              type="button"
              className="btn btn-primary btn-sm"
              onClick={submitSave}
              disabled={isSubmitting}
            >
              {tc('save')}
            </button>
          ) : (
            <>
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
          )}
        </>
      }
      preview={
        <LivePreviewPane>
          <PaperDocument
            docType={cfg.docType}
            docTypeEn={cfg.docTypeEn}
            docNo={edit?.docNo ?? '(ฉบับร่าง)'}
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
              {isEdit && (
                <span className="label-text-alt text-warning">
                  ระบบยังไม่ส่งค่ากำหนดส่งเดิมกลับมา — เว้นว่างจะล้างค่าเดิม (ถ้ามี), กรอกใหม่จะแทนที่
                </span>
              )}
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
          {isEdit && (
            <span className="label-text-alt text-warning">
              ระบบยังไม่ส่งค่าหมายเหตุเดิมกลับมา — เว้นว่างจะล้างค่าเดิม (ถ้ามี), กรอกใหม่จะแทนที่
            </span>
          )}
        </SectionCard>
      </form>
    </DocumentCreateLayout>
  );
}
