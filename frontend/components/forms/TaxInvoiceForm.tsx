'use client';

import { useEffect, useRef, useState } from 'react';
import { useRouter } from 'next/navigation';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { ShieldAlert } from 'lucide-react';
import { errorToToast } from '@/lib/api/errors';
import { PostConfirmDialog } from '@/components/ui/PostConfirmDialog';
import { DateInput } from '@/components/ui/DateInput';
import { LineItemsTable, EMPTY_LINE, type LineItem } from '@/components/ui/LineItemsTable';
import { BusinessUnitSelector } from '@/components/ui/BusinessUnitSelector';
import { useCreateTaxInvoice, useUpdateTaxInvoice, usePostTaxInvoice, useCompanyBuSetting, useCompanyProfile, useSystemInfo, useMePermissions, useDefaultDocNote } from '@/lib/queries';
import { NonVatGuard } from '@/components/ui/NonVatGuard';
import type { CreateTaxInvoiceRequest, CreateTaxInvoiceLineInput } from '@/lib/types';
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
  // fix-chain-conversion-integrity (F14) — kept in the schema so zod doesn't strip the
  // picker's selection before submit (mirrors productId/productCode above). null = user
  // hasn't picked a real tax code; the server resolves it.
  taxCode: z.string().nullable().optional(),
  taxCodeId: z.number().nullable().optional(),
  // draft-edit-receipt-taxinvoice — kept so an edited draft's product type / UoM id survive
  // the form round-trip (zod strips unknown keys). Absent on create → server defaults.
  productType: z.enum(['GOOD', 'SERVICE', 'EXEMPT_GOOD', 'EXEMPT_SERVICE']).nullable().optional(),
  uomId: z.number().int().positive().optional(),
});
const schema = z.object({
  customerId: z.number().int().positive(),
  lines: z.array(lineSchema).min(1),
});
type FormValues = z.infer<typeof schema>;

const FORM_ID = 'tax-invoice-create-form';
const SCOPE = 'sales.tax_invoice.create';

// draft-edit-receipt-taxinvoice — edit mode. `input` is GET /tax-invoices/{id}/draft-input (the exact
// create-request that reproduces the draft). Save sends `{...input, customerId, businessUnitId, lines}`:
// the form manages only those three, so quotationId / paymentTerms / dueDate / isTaxInclusive /
// currency / docDate pass through untouched (a missing quotationId would UNLINK the quotation).
export type TaxInvoiceEditProps = { id: number; input: CreateTaxInvoiceRequest; customerName: string };

const toLine = (l: CreateTaxInvoiceLineInput): FormValues['lines'][number] => ({
  descriptionTh: l.descriptionTh,
  quantity: l.quantity,
  unitPrice: l.unitPrice,
  taxRate: l.taxRate,
  uomText: l.uomText,
  discountPercent: l.discountPercent,
  productId: l.productId,
  productCode: l.productCode,
  taxCode: l.taxCode,
  taxCodeId: l.taxCodeId,
  productType: (l.productType ?? undefined) as FormValues['lines'][number]['productType'],
  uomId: l.uomId,
});

export function TaxInvoiceForm({ edit }: { edit?: TaxInvoiceEditProps } = {}) {
  const isEdit = !!edit;
  const router = useRouter();
  const t = useTranslations('ti.form');
  const tc = useTranslations('common');
  const tt = useTranslations('toast');
  const perms = useMePermissions();
  const tcr = useTranslations('create');
  const docDate = bangkokToday(); // server is authoritative; UI locked (CLAUDE.md §10)

  const create = useCreateTaxInvoice();
  const update = useUpdateTaxInvoice();
  const post = usePostTaxInvoice();
  const company = useCompanyProfile();
  const buSetting = useCompanyBuSetting();
  const buRequired = buSetting.data?.requiresBusinessUnit ?? false;
  const vatMode = useSystemInfo().data?.vatMode ?? true;
  const [confirm, setConfirm] = useState<{ id: number } | null>(null);
  const [customerLabel, setCustomerLabel] = useState(edit?.customerName ?? '');
  const [businessUnitId, setBusinessUnitId] = useState<number | null>(edit?.input.businessUnitId ?? null);
  const [buError, setBuError] = useState(false);
  const [notes, setNotes] = useState(edit?.input.notes ?? '');

  // Create-time default หมายเหตุ prefill (one-shot, never over an edited draft's own value).
  const defaultNote = useDefaultDocNote('taxInvoice');
  const notesSeeded = useRef(false);
  useEffect(() => {
    if (isEdit || notesSeeded.current || defaultNote === undefined) return;
    notesSeeded.current = true;
    if (!notes.trim()) setNotes(defaultNote);
  }, [isEdit, defaultNote, notes]);

  const invalid = onInvalidSubmit((m) => toast.error(m), tt('validationFailed'));

  const {
    control,
    handleSubmit,
    watch,
    reset,
    formState: { isSubmitting },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: edit
      ? { customerId: edit.input.customerId, lines: edit.input.lines.map(toLine) }
      : { customerId: 0, lines: [{ ...EMPTY_LINE }] },
  });

  // Re-hydrate if the edited draft arrives/changes after first render.
  useEffect(() => {
    if (!edit) return;
    reset({ customerId: edit.input.customerId, lines: edit.input.lines.map(toLine) });
    setBusinessUnitId(edit.input.businessUnitId ?? null);
    setCustomerLabel(edit.customerName);
    setNotes(edit.input.notes ?? '');
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [edit?.id]);

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
      const mappedLines: CreateTaxInvoiceLineInput[] = v.lines.map((l) => ({
        productId: l.productId ?? null,
        productCode: l.productCode ?? null,
        descriptionTh: l.descriptionTh,
        quantity: l.quantity,
        uomId: l.uomId ?? 1,
        uomText: l.uomText || 'หน่วย',
        unitPrice: l.unitPrice,
        discountPercent: l.discountPercent ?? 0,
        // fix-chain-conversion-integrity (F14/WP-5) — the line editor's real tax-code
        // picker sets these; null (untouched line) lets the server resolve the pair.
        taxCodeId: l.taxCodeId ?? null,
        taxCode: l.taxCode ?? null,
        taxRate: l.taxRate,
        productType: l.productType ?? null,
      }));
      if (edit) {
        await update.mutateAsync({
          id: edit.id,
          req: { ...edit.input, customerId: v.customerId, businessUnitId, notes: notes.trim() || null, lines: mappedLines },
        });
        toast.success(tc('draftSaved'));
        return edit.id;
      }
      const res = await create.mutateAsync({
        docDate,
        customerId: v.customerId,
        businessUnitId,
        // F8b (specs/fix-chain-conversion-integrity.md) — Q→TI is now a server-side
        // conversion (q-create-ti on the quotation detail page); this form is pure
        // hand-entry and never carries a quotation link.
        quotationId: null,
        isTaxInclusive: false,
        currencyCode: 'THB',
        exchangeRate: 1,
        notes: notes.trim() || null,
        paymentTerms: null,
        dueDate: null,
        lines: mappedLines,
      });
      toast.success(tc('draftSaved'));
      return res.tax_invoice_id;
    } catch (e) {
      toast.error(errorToToast(e));
      return null;
    }
  }

  const canCreate = perms.data?.isSuperAdmin || (perms.data?.permissions.includes(SCOPE) ?? false);
  if (perms.data && !canCreate) {
    return (
      <div className="flex flex-col items-center gap-2 py-12 text-center" data-testid="state-no-access">
        <ShieldAlert className="h-10 w-10 text-warning" aria-hidden />
        <div className="font-semibold">{tc('noAccessTitle')}</div>
        <div className="max-w-md text-sm text-base-content/60">{tc('noAccessBody', { perm: SCOPE })}</div>
      </div>
    );
  }

  if (!vatMode) return <NonVatGuard title={t('post')} />;

  // cont.80 redesign — Save Draft and Post both live in the layout header, which
  // is OUTSIDE the <form>; both fire handleSubmit() explicitly so RHF validation
  // + the exact submit payload are preserved. The <form onSubmit> stays wired for
  // Enter-to-submit and shares the same saveDraft → redirect path.
  const submitDraftAndList = handleSubmit(async (v) => {
    const id = await saveDraft(v);
    if (id) router.push(edit ? `/tax-invoices/${id}` : '/tax-invoices');
  }, invalid);
  const submitDraftAndPost = handleSubmit(async (v) => {
    const id = await saveDraft(v);
    if (id) setConfirm({ id });
  }, invalid);

  return (
    <>
      <DocumentCreateLayout
        title={isEdit ? t('editTitle') : t('post')}
        docMeta={`${t('docDate')}: ${docDate}`}
        actions={
          <>
            <button
              type="button"
              className="btn btn-ghost btn-sm"
              onClick={() => router.push(edit ? `/tax-invoices/${edit.id}` : '/tax-invoices')}
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
              docNo={tc('draftPreview')}
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
                    {tc('selectCustomer')}
                  </span>
                )}
              </SectionCard>
            )}
          />

          {/* ② ข้อมูลเอกสาร — docDate (locked) + BU. The TI page intentionally has
              no terms/dueDate fields (hardcoded null in the payload). */}
          <SectionCard number={2} title={tcr('docInfo')}>
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
                    purpose="sale"
                    businessUnitId={businessUnitId}
                  />
                  {fieldState.error && (
                    <span className="block text-sm text-error" data-field-error="true">
                      {tt('lineRequired')}
                    </span>
                  )}
                  <TotalsSummaryBox
                    rows={[
                      { label: t('beforeVat'), value: subtotal },
                      { label: t('vatLabel'), value: vat },
                    ]}
                    grandLabel={t('grandTotal')}
                    grandValue={total}
                  />
                </div>
              )}
            />
          </SectionCard>

        {/* ④ หมายเหตุ */}
        <SectionCard number={4} title={tcr('notes')}>
          <textarea
            className="textarea textarea-bordered w-full"
            rows={2}
            value={notes}
            onChange={(e) => setNotes(e.target.value)}
            aria-label={tcr('notes')}
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
          } catch (e) {
            toast.error(errorToToast(e));
          } finally {
            setConfirm(null);
          }
        }}
      />
    </>
  );
}
