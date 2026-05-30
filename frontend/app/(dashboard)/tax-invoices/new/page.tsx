'use client';

import { useEffect, useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { PostConfirmDialog } from '@/components/ui/PostConfirmDialog';
import { DateInput } from '@/components/ui/DateInput';
import { LineItemsTable, EMPTY_LINE, type LineItem } from '@/components/ui/LineItemsTable';
import { BusinessUnitSelector } from '@/components/ui/BusinessUnitSelector';
import { useCreateTaxInvoice, usePostTaxInvoice, useCompanyBuSetting, useQuotation, useCompanyProfile, useSystemInfo } from '@/lib/queries';
import { NonVatGuard } from '@/components/ui/NonVatGuard';
import { bangkokToday, formatTHB } from '@/lib/utils';
import { onInvalidSubmit, scrollToFirstError } from '@/lib/forms';
import { PaperDocument } from '@/components/paper/PaperDocument';
import { PAPER_DOC, companyToSeller } from '@/lib/paper-doc-config';
import { DocumentCreateLayout } from '@/components/create/DocumentCreateLayout';
import { SectionCard } from '@/components/create/SectionCard';
import { PartySelectBox } from '@/components/create/PartySelectBox';
import { TotalsSummaryBox } from '@/components/create/TotalsSummaryBox';
import { LivePreviewPane } from '@/components/create/LivePreviewPane';

const lineSchema = z.object({
  descriptionTh: z.string().min(1),
  quantity: z.number().positive(),
  unitPrice: z.number().min(0),
  taxRate: z.number().min(0).max(1),
  // Sprint 13j-tail — carry unit + discount + product link so the TI form's
  // (now enabled) หน่วย / ส่วนลด columns + product picker actually persist.
  uomText: z.string().optional(),
  discountPercent: z.number().min(0).max(100).optional(),
  productId: z.number().nullable().optional(),
  productCode: z.string().nullable().optional(),
});
const schema = z.object({
  customerId: z.number().int().positive(),
  lines: z.array(lineSchema).min(1),
});
type FormValues = z.infer<typeof schema>;

const FORM_ID = 'tax-invoice-create-form';

export default function CreateTaxInvoicePage() {
  const router = useRouter();
  const t = useTranslations('ti.form');
  const tc = useTranslations('common');
  const tt = useTranslations('toast');
  const tcr = useTranslations('create');
  const docDate = bangkokToday(); // server is authoritative; UI locked (CLAUDE.md §10)

  const create = useCreateTaxInvoice();
  const post = usePostTaxInvoice();
  const company = useCompanyProfile();
  const buSetting = useCompanyBuSetting();
  const buRequired = buSetting.data?.requiresBusinessUnit ?? false;
  const vatMode = useSystemInfo().data?.vatMode ?? true;
  const [confirm, setConfirm] = useState<{ id: number } | null>(null);
  const [customerLabel, setCustomerLabel] = useState('');
  const [businessUnitId, setBusinessUnitId] = useState<number | null>(null);
  const [buError, setBuError] = useState(false);

  const invalid = onInvalidSubmit((m) => toast.error(m), tt('validationFailed'));

  // Sprint 13h P6.1 — Path B: hydrate from an Accepted Quotation.
  const searchParams = useSearchParams();
  const fromQuotationId = (() => {
    const raw = searchParams.get('fromQuotationId');
    const n = raw ? Number(raw) : NaN;
    return Number.isFinite(n) && n > 0 ? n : null;
  })();
  const quotation = useQuotation(fromQuotationId ?? 0);

  const {
    control,
    handleSubmit,
    watch,
    reset,
    formState: { isSubmitting },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { customerId: 0, lines: [{ ...EMPTY_LINE }] },
  });

  // Prefill once Q arrives. Run once per quotation id by gating on data presence.
  useEffect(() => {
    if (!fromQuotationId || !quotation.data) return;
    const q = quotation.data;
    reset({
      customerId: q.customerId,
      lines: q.lines.map((l) => ({
        descriptionTh: l.descriptionTh,
        quantity: l.quantity,
        unitPrice: l.unitPrice,
        taxRate: 0.07,    // BU default; product type wiring lands in P7
      })),
    });
    setCustomerLabel(q.customerName);
    if (q.businessUnitId != null) setBusinessUnitId(q.businessUnitId);
  }, [fromQuotationId, quotation.data, reset]);

  const lines = watch('lines');
  const customerId = watch('customerId');
  const subtotal = lines.reduce((s, l) => s + l.unitPrice * l.quantity, 0);
  const vat = lines.reduce((s, l) => s + l.unitPrice * l.quantity * l.taxRate, 0);
  const total = subtotal + vat;
  const cfg = PAPER_DOC['tax-invoice'];

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
        docDate,
        customerId: v.customerId,
        businessUnitId,
        quotationId: fromQuotationId,   // Sprint 13h P6.1 — persists the Q reverse-link
        isTaxInclusive: false,
        currencyCode: 'THB',
        exchangeRate: 1,
        notes: null,
        paymentTerms: null,
        dueDate: null,
        lines: v.lines.map((l) => ({
          productId: l.productId ?? null,
          productCode: l.productCode ?? null,
          descriptionTh: l.descriptionTh,
          quantity: l.quantity,
          uomId: 1,
          uomText: l.uomText || 'หน่วย',
          unitPrice: l.unitPrice,
          discountPercent: l.discountPercent ?? 0,
          taxCodeId: 1,
          taxCode: 'V7',
          taxRate: l.taxRate,
        })),
      });
      toast.success(tc('draftSaved'));
      return res.tax_invoice_id;
    } catch {
      toast.error(tc('error'));
      return null;
    }
  }

  if (!vatMode) return <NonVatGuard title={t('post')} />;

  // cont.80 redesign — Save Draft and Post both live in the layout header, which
  // is OUTSIDE the <form>; both fire handleSubmit() explicitly so RHF validation
  // + the exact submit payload are preserved. The <form onSubmit> stays wired for
  // Enter-to-submit and shares the same saveDraft → redirect path.
  const submitDraftAndList = handleSubmit(async (v) => {
    const id = await saveDraft(v);
    if (id) router.push('/tax-invoices');
  }, invalid);
  const submitDraftAndPost = handleSubmit(async (v) => {
    const id = await saveDraft(v);
    if (id) setConfirm({ id });
  }, invalid);

  return (
    <>
      <DocumentCreateLayout
        title={t('post')}
        docMeta={`${t('docDate')}: ${docDate}`}
        actions={
          <>
            <button
              type="button"
              className="btn btn-ghost btn-sm"
              onClick={() => router.push('/tax-invoices')}
              disabled={isSubmitting}
            >
              {tcr('cancel')}
            </button>
            <button
              type="button"
              className="btn btn-outline btn-sm border-ink-200 text-ink-700 hover:bg-ink-75"
              onClick={submitDraftAndList}
              disabled={isSubmitting}
            >
              {t('saveDraft')}
            </button>
            <button
              type="button"
              className="btn btn-primary btn-sm"
              disabled={isSubmitting}
              onClick={submitDraftAndPost}
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
              customer={{ name: customerLabel || '—' }}
              items={lines.map((l) => ({
                description: l.descriptionTh,
                quantity: l.quantity,
                unitPrice: l.unitPrice,
                amount: l.unitPrice * l.quantity,
              }))}
              summary={{ subtotal, vat, total }}
              signRoles={cfg.signRoles}
            />
          </LivePreviewPane>
        }
      >
        <form id={FORM_ID} onSubmit={submitDraftAndList} className="space-y-6">
          {/* ① ลูกค้า */}
          <Controller
            control={control}
            name="customerId"
            render={({ field, fieldState }) => (
              <SectionCard number={1} title={t('customer')}>
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
                    เลือกลูกค้า
                  </span>
                )}
              </SectionCard>
            )}
          />

          {/* ② ข้อมูลเอกสาร — docDate (locked) + BU. The TI page intentionally has
              no terms/dueDate fields (hardcoded null in the payload). */}
          <SectionCard number={2} title="ข้อมูลเอกสาร">
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
              <DateInput value={docDate} locked label={t('docDate')} />
              <BusinessUnitSelector
                value={businessUnitId}
                onChange={(id) => { setBusinessUnitId(id); if (id) setBuError(false); }}
                required={buRequired}
                error={buError}
              />
            </div>
          </SectionCard>

          {/* ③ รายการสินค้า / บริการ + totals */}
          <SectionCard number={3} title={t('lines')} rightMeta={`${lines.length} ${t('lines')}`}>
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
                  />
                  {fieldState.error && (
                    <span className="block text-sm text-error" data-field-error="true">
                      {tt('lineRequired')}
                    </span>
                  )}
                  <TotalsSummaryBox
                    rows={[
                      { label: 'มูลค่าก่อนภาษี', value: subtotal },
                      { label: 'ภาษีมูลค่าเพิ่ม 7%', value: vat },
                    ]}
                    grandLabel="รวมทั้งสิ้น"
                    grandValue={total}
                  />
                </div>
              )}
            />
          </SectionCard>
        </form>
      </DocumentCreateLayout>

      <PostConfirmDialog
        docType="tax_invoice"
        open={confirm !== null}
        busy={post.isPending}
        summary={{ customer: customerLabel || `#${customerId}`, total, vat }}
        recipients={[]}
        onClose={() => setConfirm(null)}
        onConfirm={async () => {
          if (!confirm) return;
          try {
            await post.mutateAsync(confirm.id);
            toast.success(tc('posted'));
            router.push(`/tax-invoices/${confirm.id}`);
          } catch {
            toast.error(tc('error'));
          } finally {
            setConfirm(null);
          }
        }}
      />
    </>
  );
}
