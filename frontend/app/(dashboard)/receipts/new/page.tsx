'use client';

import { useEffect, useRef, useState } from 'react';
import { useQueries } from '@tanstack/react-query';
import { useRouter, useSearchParams } from 'next/navigation';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useTranslations } from 'next-intl';
import { Plus, Trash2 } from 'lucide-react';
import { toast } from 'sonner';
import { PostConfirmDialog } from '@/components/ui/PostConfirmDialog';
import { AmountInput } from '@/components/ui/AmountInput';
import { DateInput } from '@/components/ui/DateInput';
import { BusinessUnitSelector } from '@/components/ui/BusinessUnitSelector';
import { WhtTypeSelect } from '@/components/ui/WhtTypeSelect';
import { TaxInvoicePicker } from '@/components/forms/TaxInvoicePicker';
import { InvoicePicker } from '@/components/forms/InvoicePicker';
import { ProductPicker } from '@/components/forms/ProductPicker';
import {
  useCreateReceipt, usePostReceipt, useCompanyBuSetting, useWhtBaseSuggest,
  useCompanyProfile, useSystemInfo,
} from '@/lib/queries';
import { apiGet } from '@/lib/api';
import type { TaxInvoiceDetail, BillingNoteDetail, ProductTypeStr } from '@/lib/types';
import { bangkokToday, formatTHB } from '@/lib/utils';
import { scrollToFirstError } from '@/lib/forms';
import { PaperDocument } from '@/components/paper/PaperDocument';
import { PAPER_DOC, companyToSeller } from '@/lib/paper-doc-config';
import { DocumentCreateLayout } from '@/components/create/DocumentCreateLayout';
import { SectionCard } from '@/components/create/SectionCard';
import { PartySelectBox } from '@/components/create/PartySelectBox';
import { TotalsSummaryBox, type TotalRow } from '@/components/create/TotalsSummaryBox';
import { LivePreviewPane } from '@/components/create/LivePreviewPane';

// Receipt billing modes (cont. 68 → Phase 2a — non-VAT support):
//   ti          — apply to one or more Tax Invoices (VAT path; the only mode when vatMode=true).
//   invoice     — apply to an Invoice (ใบแจ้งหนี้; BE BillingNote). Non-VAT credit: a non-VAT
//                 company issues no TI (ม.86/4) and the flow is now DO → Invoice → Receipt.
//   standalone  — own cash-bill line items, no source document (non-VAT cash receipt).
type ReceiptMode = 'ti' | 'invoice' | 'standalone';

const schema = z.object({ customerId: z.number().int().positive() });
type FormValues = z.infer<typeof schema>;

type AppRow = { docId: number; appliedAmount: number };
type LineRow = {
  description: string; quantity: number; unitPrice: number; amount: number;
  productType: ProductTypeStr; productId: number | null; uomText: string | null;
};
const emptyLine = (): LineRow => ({
  description: '', quantity: 1, unitPrice: 0, amount: 0,
  productType: 'GOOD', productId: null, uomText: null,
});

export default function NewReceiptPage() {
  const router = useRouter();
  // Sprint 13j-tail — prefill when arriving from a Tax Invoice detail
  // ("สร้างใบเสร็จ"): ?ti & ?customer & ?amount → first application + customer.
  const sp = useSearchParams();
  const preTi = Number(sp.get('ti')) || 0;
  // Non-VAT: arriving from an Invoice (BillingNote) detail ("สร้างใบเสร็จ").
  const preBn = Number(sp.get('bn')) || 0;
  const preCustomer = Number(sp.get('customer')) || 0;
  const preAmount = Number(sp.get('amount')) || 0;
  const t = useTranslations('rc');
  const tc = useTranslations('common');
  const tt = useTranslations('toast');
  const tcr = useTranslations('create');
  const tw = useTranslations('rc.wht');
  const [customerLabel, setCustomerLabel] = useState('');
  const docDate = bangkokToday();
  const create = useCreateReceipt();
  const post = usePostReceipt();
  const company = useCompanyProfile();
  const sysInfo = useSystemInfo();
  const vatMode = sysInfo.data?.vatMode ?? true;
  const buSetting = useCompanyBuSetting();
  const buRequired = buSetting.data?.requiresBusinessUnit ?? false;
  const [businessUnitId, setBusinessUnitId] = useState<number | null>(null);
  const [buError, setBuError] = useState(false);
  const [confirm, setConfirm] = useState<{ id: number } | null>(null);

  // Mode: forced 'ti' when arriving from a TI or when VAT mode is on. For a non-VAT
  // company default to a standalone cash bill; the user can switch to apply-to-Invoice.
  // Seed synchronously from the URL so the first render already knows the mode —
  // otherwise a bn-sourced receipt briefly defaults to 'ti' and fetches the id as a
  // Tax Invoice (404). vatMode-driven default (no pre-doc) is set by the effect below.
  const [mode, setMode] = useState<ReceiptMode>(preBn ? 'invoice' : 'ti');
  const modeInit = useRef(false);
  useEffect(() => {
    if (sysInfo.isLoading || modeInit.current) return;
    modeInit.current = true;
    setMode(preTi ? 'ti' : preBn ? 'invoice' : vatMode ? 'ti' : 'standalone');
  }, [sysInfo.isLoading, vatMode, preTi, preBn]);

  const { control, handleSubmit, watch, formState: { isSubmitting } } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { customerId: preCustomer },
  });
  const customerId = watch('customerId');

  // Source rows. `apps` backs both TI- and Invoice-apply (docId = TI id or Invoice id);
  // `lines` backs the standalone cash bill.
  const preDoc = preTi || preBn;
  const [apps, setApps] = useState<AppRow[]>([{ docId: preDoc, appliedAmount: preDoc ? preAmount : 0 }]);
  const [lines, setLines] = useState<LineRow[]>([emptyLine()]);
  const setApp = (i: number, patch: Partial<AppRow>) =>
    setApps((rs) => rs.map((r, idx) => (idx === i ? { ...r, ...patch } : r)));
  const setLineRow = (i: number, patch: Partial<LineRow>) =>
    setLines((rs) => rs.map((r, idx) => (idx === i ? { ...r, ...patch } : r)));

  const total = mode === 'standalone'
    ? lines.reduce((s, l) => s + (l.amount || 0), 0)
    : apps.reduce((s, a) => s + (a.appliedAmount || 0), 0);

  // Live preview line items. TI mode pulls the goods/service lines from the applied
  // (immutable) Tax Invoices; standalone shows the entered lines; Invoice shows the doc ref.
  const previewTiIds = mode === 'ti'
    ? Array.from(new Set(apps.map((a) => a.docId).filter((v) => v > 0)))
    : [];
  const tiDetailQueries = useQueries({
    queries: previewTiIds.map((tid) => ({
      queryKey: ['tax-invoice', tid],
      queryFn: () => apiGet<TaxInvoiceDetail>(`tax-invoices/${tid}`),
      enabled: tid > 0,
    })),
  });
  const derivedItems = tiDetailQueries.flatMap((q) =>
    (q.data?.lines ?? []).map((l) => ({
      description: l.descriptionTh,
      descriptionSub: q.data?.docNo ?? undefined,
      quantity: l.quantity,
      unit: l.uomText,
      unitPrice: l.unitPrice,
      amount: l.lineAmount,
    })));

  // Invoice (BillingNote) mode: pull the Invoice's line items for the preview, mirroring
  // the BE which itemizes the posted receipt from the applied Invoice (cont.69 P1).
  const previewBnIds = mode === 'invoice'
    ? Array.from(new Set(apps.map((a) => a.docId).filter((v) => v > 0)))
    : [];
  const bnDetailQueries = useQueries({
    queries: previewBnIds.map((bid) => ({
      queryKey: ['billing-note', bid],
      queryFn: () => apiGet<BillingNoteDetail>(`billing-notes/${bid}`),
      enabled: bid > 0,
    })),
  });
  const derivedInvoiceItems = bnDetailQueries.flatMap((q) =>
    (q.data?.lines ?? []).map((l) => ({
      description: l.descriptionTh,
      descriptionSub: q.data?.docNo ?? undefined,
      quantity: l.quantity,
      unit: l.uomText,
      unitPrice: l.unitPrice,
      amount: l.lineAmount,
    })));

  // Standalone non-VAT cash bill: the WHT table mirrors the receipt's OWN line items —
  // each line offered for category selection, base = its amount. Goods default to "ไม่หัก".
  // (Invoice and TI modes both auto-categorize server-side via the suggestion below.)
  const nonVatWhtSource: { description: string; productType: string; base: number }[] =
    mode === 'standalone'
      ? lines.filter((l) => l.description.trim() !== '')
          .map((l) => ({ description: l.description, productType: l.productType, base: l.amount }))
      : [];

  // ── AR-side WHT ──────────────────────────────────────────────────────────
  // TI mode: classified PER applied line, auto-resolved from the product (service →
  // its DefaultWhtType; goods → none), seeded from the server suggestion.
  // Non-VAT (DO/standalone): no TI to derive from → the user adds WHT rows manually
  // (a non-VAT customer can still withhold). Both aggregate by income type on save.
  const [whtOn, setWhtOn] = useState(false);
  type LineWhtDraft = { description: string; productType: string; base: number; whtTypeId: number | null; rate: number };
  const [lineWht, setLineWht] = useState<LineWhtDraft[]>([]);
  const [whtCertNo, setWhtCertNo] = useState('');
  const [whtCertDate, setWhtCertDate] = useState('');
  const [whtSeeded, setWhtSeeded] = useState(false);
  // Server-side WHT auto-categorization: TI lines (VAT) or BillingNote lines (non-VAT).
  // Standalone has no source doc → no server suggestion (manual / client-derived).
  const appsForSuggest =
    mode === 'ti'
      ? apps.filter((a) => a.docId > 0 && a.appliedAmount > 0)
          .map((a) => ({ taxInvoiceId: a.docId, appliedAmount: a.appliedAmount }))
      : mode === 'invoice'
        ? apps.filter((a) => a.docId > 0 && a.appliedAmount > 0)
            .map((a) => ({ billingNoteId: a.docId, appliedAmount: a.appliedAmount }))
        : [];
  const suggest = useWhtBaseSuggest(whtOn ? appsForSuggest : [], whtOn ? customerId : 0);
  const rowWht = (l: LineWhtDraft) => (l.whtTypeId ? Math.round(l.base * l.rate * 100) / 100 : 0);
  const whtAmount = lineWht.reduce((s, l) => s + rowWht(l), 0);
  const cashReceived = total - whtAmount;
  const rcCfg = PAPER_DOC.receipt;

  function applySuggestion() {
    const ls = suggest.data?.lines;
    if (!ls) return;
    setLineWht(ls.map((l) => ({
      description: l.description, productType: l.productType, base: l.lineAmount,
      whtTypeId: l.suggestedWhtTypeId, rate: l.suggestedRate,
    })));
    setWhtSeeded(true);
  }
  const setLine = (i: number, patch: Partial<LineWhtDraft>) =>
    setLineWht((ls) => ls.map((l, idx) => (idx === i ? { ...l, ...patch } : l)));

  // Aggregate the per-line WHT into receipt WhtLines (one row per income type).
  function aggregatedWhtLines() {
    const byType = new Map<number, number>();
    for (const l of lineWht) {
      if (l.whtTypeId == null || l.base <= 0) continue;
      byType.set(l.whtTypeId, (byType.get(l.whtTypeId) ?? 0) + l.base);
    }
    return [...byType.entries()].map(([whtTypeId, baseAmount]) => ({ whtTypeId, baseAmount }));
  }

  // Auto-seed the per-line table from the server suggestion once (TI mode only),
  // while the user hasn't edited it yet. "ดึงค่าที่แนะนำ" re-seeds.
  useEffect(() => {
    if (mode !== 'standalone' && whtOn && !whtSeeded && (suggest.data?.lines?.length ?? 0) > 0) applySuggestion();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [whtOn, suggest.data, mode]);

  // Non-VAT: keep the WHT table in sync with the line items. Re-derive on every line
  // change but PRESERVE the category/rate the user already picked (matched by description),
  // so editing an amount doesn't reset the chosen income type.
  useEffect(() => {
    if (mode !== 'standalone' || !whtOn) return;
    setLineWht((prev) => nonVatWhtSource.map((src) => {
      const kept = prev.find((p) => p.description === src.description);
      return {
        description: src.description, productType: src.productType, base: src.base,
        whtTypeId: kept?.whtTypeId ?? null, rate: kept?.rate ?? 0,
      };
    }));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [whtOn, mode, JSON.stringify(nonVatWhtSource)]);

  function validateSource(): boolean {
    if (mode === 'standalone') {
      return lines.some((l) => l.description.trim() !== '' && l.amount > 0);
    }
    return apps.some((a) => a.docId > 0 && a.appliedAmount > 0);
  }

  async function saveDraft(v: FormValues): Promise<number | null> {
    if (buRequired && businessUnitId === null) {
      setBuError(true);
      toast.error(tt('validationFailed'));
      requestAnimationFrame(scrollToFirstError);
      return null;
    }
    setBuError(false);
    if (!validateSource()) { toast.error(tt('validationFailed')); return null; }
    const aggWht = aggregatedWhtLines();
    if (whtOn && aggWht.length === 0) { toast.error(tt('validationFailed')); return null; }

    const applications =
      mode === 'ti'
        ? apps.filter((a) => a.docId > 0).map((a) => ({ taxInvoiceId: a.docId, appliedAmount: a.appliedAmount }))
        : mode === 'invoice'
          ? apps.filter((a) => a.docId > 0).map((a) => ({ billingNoteId: a.docId, appliedAmount: a.appliedAmount }))
          : [];
    const reqLines =
      mode === 'standalone'
        ? lines.filter((l) => l.description.trim() !== '' && l.amount > 0).map((l) => ({
            descriptionTh: l.description, quantity: l.quantity, unitPrice: l.unitPrice,
            amount: l.amount, productType: l.productType, productId: l.productId, uomText: l.uomText,
          }))
        : undefined;

    try {
      const res = await create.mutateAsync({
        docDate, customerId: v.customerId, paymentMethod: 'Transfer',
        chequeNo: null, chequeDate: null, bankAccountId: null,
        currencyCode: 'THB', exchangeRate: 1, notes: null,
        applications,
        lines: reqLines,
        businessUnitId,
        whtLines: whtOn ? aggWht : [],
        customerWhtCertNo: whtOn ? whtCertNo : null,
        customerWhtCertDate: whtOn && whtCertDate ? whtCertDate : null,
      });
      toast.success(tc('draftSaved'));
      return res.receipt_id;
    } catch { toast.error(tc('error')); return null; }
  }

  // Preview items per mode.
  const previewItems =
    mode === 'standalone'
      ? lines.filter((l) => l.description.trim() !== '').map((l) => ({
          description: l.description, quantity: l.quantity, unit: l.uomText ?? undefined,
          unitPrice: l.unitPrice, amount: l.amount,
        }))
      : mode === 'invoice'
        ? derivedInvoiceItems.length > 0
          ? derivedInvoiceItems
          : apps.map((a) => ({ description: `ใบแจ้งหนี้ #${a.docId || '—'}`, amount: a.appliedAmount || 0 }))
        : derivedItems.length > 0
          ? derivedItems
          : apps.map((a) => ({ description: `ใบกำกับภาษี #${a.docId || '—'}`, amount: a.appliedAmount || 0 }));

  const totalRows: TotalRow[] = whtOn && whtAmount > 0
    ? [
        { label: t('amount'), value: total },
        { label: tw('amount'), value: -whtAmount, muted: true },
      ]
    : [];

  return (
    <>
      <DocumentCreateLayout
        title={t('create')}
        docMeta={docDate}
        actions={
          <>
            <button
              type="button"
              className="btn btn-ghost btn-sm"
              onClick={() => router.push('/receipts')}
              disabled={isSubmitting}
            >
              {tcr('cancel')}
            </button>
            <button
              type="button"
              className="btn btn-outline btn-sm border-ink-200 text-ink-700 hover:bg-ink-75"
              disabled={isSubmitting}
              onClick={handleSubmit(async (v) => { const id = await saveDraft(v); if (id) router.push('/receipts'); })}
            >
              {tc('save')}
            </button>
            <button
              type="button"
              className="btn btn-primary btn-sm"
              disabled={isSubmitting}
              onClick={handleSubmit(async (v) => { const id = await saveDraft(v); if (id) setConfirm({ id }); })}
            >
              {t('post')}
            </button>
          </>
        }
        preview={
          <LivePreviewPane>
            <PaperDocument
              docType={rcCfg.docType}
              docTypeEn={rcCfg.docTypeEn}
              docNo="(ฉบับร่าง)"
              issueDate={docDate}
              seller={companyToSeller(company.data)}
              customer={{ name: customerLabel || '—' }}
              items={previewItems}
              summary={{ subtotal: total, vat: 0, total, showVat: vatMode }}
              signRoles={rcCfg.signRoles}
              extraMetaBlock={
                <>
                  <dt>วิธีชำระ</dt>
                  <dd>Transfer</dd>
                  {whtOn && whtAmount > 0 && (
                    <>
                      <dt>{tw('amount')}</dt>
                      <dd>({formatTHB(whtAmount)})</dd>
                    </>
                  )}
                </>
              }
            />
          </LivePreviewPane>
        }
      >
      <form className="space-y-6" onSubmit={handleSubmit(async (v) => { const id = await saveDraft(v); if (id) router.push('/receipts'); })}>
        {/* ① ลูกค้า */}
        <Controller control={control} name="customerId" render={({ field, fieldState }) => (
          <SectionCard number={1} title={tc('customer')}>
            <PartySelectBox
              kind="customer"
              party={field.value || null}
              onChange={(id, label) => { field.onChange(id); setCustomerLabel(label); }}
            />
            {fieldState.error && <span className="mt-2 block text-sm text-error" data-field-error="true">เลือกลูกค้า</span>}
          </SectionCard>
        )} />

        {/* ② ข้อมูลเอกสาร */}
        <SectionCard number={2} title={tcr('docInfo')}>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <DateInput value={docDate} locked label={t('date')} />
            <BusinessUnitSelector
              value={businessUnitId}
              onChange={(id) => { setBusinessUnitId(id); if (id) setBuError(false); }}
              required={buRequired}
              error={buError}
            />
          </div>
          {/* Mode selector — only meaningful for a non-VAT company (no TI to apply to).
              In VAT mode the receipt always settles a Tax Invoice, so the selector is hidden. */}
          {!vatMode && !preTi && (
            <div className="mt-4 flex flex-wrap gap-2" role="tablist" aria-label={t('nonVat.modeLabel')}>
              {(['standalone', 'invoice'] as ReceiptMode[]).map((m) => (
                <button
                  key={m}
                  type="button"
                  role="tab"
                  aria-selected={mode === m}
                  className={`btn btn-sm ${mode === m ? 'btn-primary' : 'btn-ghost'}`}
                  onClick={() => setMode(m)}
                >
                  {m === 'standalone' ? t('nonVat.modeStandalone') : t('nonVat.modeApplyInvoice')}
                </button>
              ))}
            </div>
          )}
        </SectionCard>

        {/* ③ แหล่งที่มา / รายการ */}
        <SectionCard number={3} title={mode === 'standalone' ? t('nonVat.lineItems') : mode === 'invoice' ? t('nonVat.applyInvoiceHeader') : t('applyTo')}>
          <div className="space-y-4">
        {/* ── Source: standalone cash-bill line items (non-VAT) ── */}
        {mode === 'standalone' && (
          <div>
            <div className="overflow-x-auto rounded-lg border border-base-300">
              <table className="table table-sm">
                <thead><tr>
                  <th>{t('nonVat.lineDesc')}</th>
                  <th className="w-24 text-right">{t('nonVat.qty')}</th>
                  <th className="w-32 text-right">{t('nonVat.unitPrice')}</th>
                  <th className="w-32 text-right">{t('amount')}</th>
                  <th className="w-10" />
                </tr></thead>
                <tbody>
                  {lines.map((l, i) => (
                    <tr key={i}>
                      <td className="min-w-[16rem]">
                        <ProductPicker
                          description={l.description}
                          ariaLabel={`lineDesc ${i + 1}`}
                          onDescriptionChange={(text) => setLineRow(i, { description: text })}
                          onSelectProduct={(p) => setLineRow(i, {
                            description: p.nameTh, productId: p.productId, productType: p.productType,
                            unitPrice: p.defaultUnitPrice ?? l.unitPrice,
                            amount: (p.defaultUnitPrice ?? l.unitPrice) * (l.quantity || 1),
                          })}
                        />
                      </td>
                      <td>
                        <AmountInput value={l.quantity} step={1} aria-label={`qty ${i + 1}`}
                          onValueChange={(q) => setLineRow(i, { quantity: q, amount: q * l.unitPrice })} />
                      </td>
                      <td>
                        <AmountInput value={l.unitPrice} aria-label={`unitPrice ${i + 1}`}
                          onValueChange={(up) => setLineRow(i, { unitPrice: up, amount: l.quantity * up })} />
                      </td>
                      <td>
                        <AmountInput value={l.amount} aria-label={`lineAmount ${i + 1}`}
                          onValueChange={(amt) => setLineRow(i, { amount: amt })} />
                      </td>
                      <td>
                        <button type="button" className="btn btn-ghost btn-xs text-error" disabled={lines.length === 1}
                          onClick={() => setLines((ls) => ls.filter((_, idx) => idx !== i))}>
                          <Trash2 className="h-4 w-4" aria-hidden />
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <button type="button"
              className="mt-3 flex w-full items-center justify-center gap-2 rounded-field border border-dashed border-ink-200 bg-base-100 py-3 text-sm font-medium text-peach-700 hover:border-peach-300 hover:bg-peach-50"
              onClick={() => setLines((ls) => [...ls, emptyLine()])}>
              <Plus className="h-4 w-4" aria-hidden /> {t('nonVat.addLine')}
            </button>
          </div>
        )}

        {/* ── Source: apply to a Tax Invoice (VAT) or Invoice/ใบแจ้งหนี้ (non-VAT credit) ── */}
        {mode !== 'standalone' && (
          <div>
            <div className="overflow-x-auto rounded-lg border border-base-300">
              <table className="table">
                <thead><tr>
                  <th>{mode === 'invoice' ? t('nonVat.invoice') : 'ใบกำกับภาษี'}</th>
                  <th className="text-right">{t('amount')}</th>
                  <th className="w-10" />
                </tr></thead>
                <tbody>
                  {apps.map((a, i) => (
                    <tr key={i}>
                      <td className="min-w-[16rem]">
                        {mode === 'invoice' ? (
                          <InvoicePicker
                            value={a.docId || null}
                            customerId={customerId || null}
                            disabled={!customerId}
                            ariaLabel={`billingNoteId ${i + 1}`}
                            onChange={(d) => setApp(i, { docId: d.billingNoteId, appliedAmount: d.totalAmount })}
                          />
                        ) : (
                          <TaxInvoicePicker
                            value={a.docId || null}
                            customerId={customerId || null}
                            unpaid
                            disabled={!customerId}
                            ariaLabel={`taxInvoiceId ${i + 1}`}
                            onChange={(ti) => setApp(i, { docId: ti.taxInvoiceId, appliedAmount: ti.totalAmount })}
                          />
                        )}
                      </td>
                      <td>
                        <AmountInput value={a.appliedAmount} aria-label={`appliedAmount ${i + 1}`}
                          onValueChange={(amt) => setApp(i, { appliedAmount: amt })} />
                      </td>
                      <td>
                        <button type="button" className="btn btn-ghost btn-xs text-error" disabled={apps.length === 1}
                          onClick={() => setApps((rs) => rs.filter((_, idx) => idx !== i))}>
                          <Trash2 className="h-4 w-4" aria-hidden />
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <button type="button"
              className="mt-3 flex w-full items-center justify-center gap-2 rounded-field border border-dashed border-ink-200 bg-base-100 py-3 text-sm font-medium text-peach-700 hover:border-peach-300 hover:bg-peach-50"
              onClick={() => setApps((rs) => [...rs, { docId: 0, appliedAmount: 0 }])}>
              <Plus className="h-4 w-4" aria-hidden /> {t('addApply')}
            </button>
          </div>
        )}

            <TotalsSummaryBox
              rows={totalRows}
              grandLabel={whtOn && whtAmount > 0 ? tw('cashReceived') : t('amount')}
              grandValue={whtOn && whtAmount > 0 ? cashReceived : total}
            />
          </div>
        </SectionCard>

        {/* ④ AR-side WHT (collapsible). */}
        <SectionCard number={4} title={tcr('whtSection')}>
          <label className="label cursor-pointer justify-start gap-3">
            <input type="checkbox" className="toggle toggle-primary" checked={whtOn}
              onChange={(e) => setWhtOn(e.target.checked)} />
            <span className="font-semibold">{tw('toggleEnable')}</span>
          </label>
          {whtOn && (
            <div className="mt-3 space-y-3">
              {mode !== 'standalone' && suggest.data && (
                <div className="flex items-center justify-between gap-2 rounded bg-base-200 p-2 text-xs">
                  <span>{suggest.data.explanation}</span>
                  <button type="button" className="btn btn-xs" onClick={applySuggestion}>
                    {tw('applySuggest')} ▸
                  </button>
                </div>
              )}

              <div className="overflow-x-auto rounded-lg border border-base-300">
                <table className="table table-sm">
                  <thead>
                    <tr>
                      <th>{tw('lineItem')}</th>
                      <th className="w-40">{tw('type')}</th>
                      <th className="w-32 text-right">{tw('base')}</th>
                      <th className="w-20 text-right">{tw('rate')}</th>
                      <th className="w-28 text-right">{tw('amount')}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {lineWht.length === 0 && (
                      <tr><td colSpan={5} className="py-3 text-center text-xs text-base-content/50">
                        {mode === 'ti' ? tw('noWhtLines') : tw('noWhtLinesNonVat')}
                      </td></tr>
                    )}
                    {lineWht.map((l, i) => (
                      <tr key={i}>
                        <td className="min-w-[12rem]">
                          <div className="text-sm">{l.description}</div>
                          <span className="text-xs text-base-content/50">{l.productType}</span>
                        </td>
                        <td className="min-w-[12rem]">
                          <WhtTypeSelect
                            value={l.whtTypeId}
                            placeholder={tw('noWht')}
                            className="select-sm"
                            ariaLabel={`${tw('type')} ${i + 1}`}
                            onChange={(id, wt) => setLine(i, { whtTypeId: id, rate: wt?.rate ?? 0 })}
                          />
                        </td>
                        <td>
                          <input type="number" step="0.01" className="input input-bordered input-sm w-full text-right"
                            value={l.base}
                            onChange={(e) => setLine(i, { base: Number(e.target.value) })}
                            aria-label={`${tw('base')} ${i + 1}`} />
                        </td>
                        <td className="text-right tabular-nums">{l.whtTypeId ? `${(l.rate * 100).toFixed(2)}%` : '—'}</td>
                        <td className="text-right tabular-nums">{rowWht(l).toFixed(2)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>

              <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
                <label className="form-control">
                  <span className="label-text">{tw('amount')} ({tw('total')})</span>
                  <input className="input input-bordered input-sm tabular-nums bg-base-200" readOnly
                    value={whtAmount.toFixed(2)} />
                </label>
                <label className="form-control">
                  <span className="label-text">{tw('cashReceived')}</span>
                  <input className="input input-bordered input-sm tabular-nums bg-base-200" readOnly
                    value={cashReceived.toFixed(2)} />
                </label>
                <label className="form-control">
                  <span className="label-text">{tw('certNo')} <span className="text-ink-400">(ใส่ทีหลังได้)</span></span>
                  <input className="input input-bordered input-sm" value={whtCertNo}
                    placeholder="ระบุภายหลังจากหน้ารายละเอียด"
                    onChange={(e) => setWhtCertNo(e.target.value)} />
                </label>
                <label className="form-control">
                  <span className="label-text">{tw('certDate')}</span>
                  <input type="date" className="input input-bordered input-sm" value={whtCertDate}
                    onChange={(e) => setWhtCertDate(e.target.value)} />
                </label>
              </div>
              <p className="text-xs text-warning">{tw('multiCatExplain')}</p>
            </div>
          )}
        </SectionCard>
      </form>
      </DocumentCreateLayout>

      <PostConfirmDialog
        docType="receipt"
        open={confirm !== null}
        busy={post.isPending}
        summary={{ customer: `#${watch('customerId')}`, total, vat: 0 }}
        recipients={[]}
        onClose={() => setConfirm(null)}
        onConfirm={async () => {
          if (!confirm) return;
          try { await post.mutateAsync(confirm.id); toast.success(tc('posted')); router.push(`/receipts/${confirm.id}`); }
          catch { toast.error(tc('error')); }
          finally { setConfirm(null); }
        }}
      />
    </>
  );
}
