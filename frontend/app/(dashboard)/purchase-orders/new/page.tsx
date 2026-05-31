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
import { useCreatePurchaseOrder, useSystemInfo, useVendor, useCompanyBuSetting, useCompanyProfile } from '@/lib/queries';
import { bangkokToday, formatTaxId } from '@/lib/utils';
import { onInvalidSubmit } from '@/lib/forms';
import { PaperDocument } from '@/components/paper/PaperDocument';
import { PAPER_DOC, companyToSeller } from '@/lib/paper-doc-config';
import { DocumentCreateLayout } from '@/components/create/DocumentCreateLayout';
import { SectionCard } from '@/components/create/SectionCard';
import { PartySelectBox } from '@/components/create/PartySelectBox';
import { TotalsSummaryBox, type TotalRow } from '@/components/create/TotalsSummaryBox';
import { LivePreviewPane } from '@/components/create/LivePreviewPane';

// Sprint 13j-PURCH Phase F — full PO create form (replaces the 1-line MVP stub
// that hardcoded taxCodeId:1/VAT7/0.07 on a single line). Lifts to "VI quality":
// multi-line <LineItemsTable> with per-line <ProductPicker> (free-text fallback
// per Plan §7), real VAT-rate selector (std rate from /system/info, never
// hardcoded — CLAUDE.md §4.6) + per-line discount %, RHF+Zod, specific Thai
// toast errors (no generic-only fallback — BUG #SR9). Submit→detail redirect kept.
// cont.80 — restyled into the shared DocumentCreateLayout + added a live A4 preview
// (PO prints as a buyer-issued doc: seller=our company, customer slot=the vendor),
// mirroring the PO detail page. Fields/validation/payload unchanged.
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

const FORM_ID = 'purchase-order-create-form';

export default function NewPurchaseOrderPage() {
  const t = useTranslations('purchaseOrder');
  const tc = useTranslations('common');
  const tt = useTranslations('toast');
  const tcr = useTranslations('create');
  const router = useRouter();
  const create = useCreatePurchaseOrder();
  const company = useCompanyProfile();
  // Non-VAT companies (ม.86): LineItemsTable hides the VAT column; force the
  // effective rate to 0 so a stale default rate never leaks into totals/payload.
  const vatMode = useSystemInfo().data?.vatMode ?? true;

  // Sprint BU-PURCH — business unit, optional unless the company opted in.
  const buRequired = useCompanyBuSetting().data?.requiresBusinessUnit ?? false;
  const [businessUnitId, setBusinessUnitId] = useState<number | null>(null);
  const [buError, setBuError] = useState(false);

  const today = bangkokToday();
  const [docDate, setDocDate] = useState(today);
  // ITEM 1 — expected-delivery defaults to today (editable), same source as docDate.
  const [expected, setExpected] = useState(today);
  const [notes, setNotes] = useState('');
  const [vendorLabel, setVendorLabel] = useState('');

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
    // Sprint BU-PURCH — block submit when the company requires a BU and none chosen.
    if (buRequired && businessUnitId === null) {
      setBuError(true);
      toast.error(tt('validationFailed'));
      return;
    }
    setBuError(false);
    try {
      const payload = {
        docDate,
        expectedDeliveryDate: expected || null,
        vendorId: v.vendorId,
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

  const cfg = PAPER_DOC['purchase-order'];
  const submitSave = handleSubmit(submit, invalid);

  const totalRows: TotalRow[] = [
    { label: t('subtotal'), value: totals.subtotal },
    { label: t('discount'), value: -totals.discount, muted: true },
    ...(vendorVat ? [{ label: t('vat'), value: totals.vat }] : []),
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
            onClick={() => router.push('/purchase-orders')}
            disabled={isSubmitting || create.isPending}
          >
            {tcr('cancel')}
          </button>
          <button
            type="button"
            className="btn btn-primary btn-sm"
            disabled={isSubmitting || create.isPending}
            onClick={submitSave}
          >
            {tc('save')}
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
            validUntil={expected || undefined}
            validUntilLabel={cfg.validUntilLabel}
            seller={companyToSeller(company.data)}
            partyLabel={{ th: 'ผู้ขาย', en: 'Vendor' }}
            customer={{
              name: vendorLabel || '—',
              taxId: vendor?.taxId ? formatTaxId(vendor.taxId) : null,
              branchCode: vendor?.branchCode ?? null,
              address: vendor?.address ?? null,
              contact: vendor?.contactPerson ?? null,
              phone: vendor?.phone ?? null,
            }}
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
        </LivePreviewPane>
      }
    >
      <form id={FORM_ID} onSubmit={submitSave} className="space-y-6">
        {/* ① ผู้ขาย */}
        <Controller
          control={control}
          name="vendorId"
          render={({ field, fieldState }) => (
            <SectionCard number={1} title={t('vendor')}>
              <PartySelectBox
                kind="vendor"
                party={field.value || null}
                onChange={(id, label) => { field.onChange(id ?? 0); setVendorLabel(label); }}
              />
              {fieldState.error && (
                <span className="mt-2 block text-sm text-error" data-field-error="true">
                  {t('pickVendor')}
                </span>
              )}
            </SectionCard>
          )}
        />

        {/* ② ข้อมูลเอกสาร */}
        <SectionCard number={2} title={tcr('docInfo')}>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
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
                  vatEnabled={vendorVat}
                  hideHeading
                />
                {fieldState.error && (
                  <span className="block text-sm text-error" data-field-error="true">
                    {tt('lineRequired')}
                  </span>
                )}
                <TotalsSummaryBox
                  rows={totalRows}
                  grandLabel={t('total')}
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
