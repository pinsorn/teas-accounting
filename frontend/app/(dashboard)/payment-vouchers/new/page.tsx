'use client';

import { Suspense, useEffect, useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { Plus, ShieldAlert, Trash2 } from 'lucide-react';
import { apiErrorToast } from '@/lib/api/errors';
import { DateInput } from '@/components/ui/DateInput';
import { ExpenseCategorySelector } from '@/components/ui/ExpenseCategorySelector';
import { ProductPicker, taxRateForProductType } from '@/components/forms/ProductPicker';
import { BusinessUnitSelector } from '@/components/ui/BusinessUnitSelector';
import { PercentRateInput } from '@/components/ui/PercentRateInput';
import { apiPost } from '@/lib/api';
import { useVendor, useWhtTypes, usePurchaseOrder, useVendorInvoice, useCompanyBuSetting, useCompanyProfile, useMePermissions } from '@/lib/queries';
import { derivePvPrefillBase } from '@/lib/pv-prefill';
import type { ProductTypeStr } from '@/lib/types';
import { bangkokToday, formatDateBE, formatTaxId } from '@/lib/utils';
import { PaperDocument } from '@/components/paper/PaperDocument';
import { PAPER_DOC, companyToSeller, DRAFT_DOC_NO, VENDOR_PARTY_LABEL } from '@/lib/paper-doc-config';
import { DocumentCreateLayout } from '@/components/create/DocumentCreateLayout';
import { SectionCard } from '@/components/create/SectionCard';
import { PartySelectBox } from '@/components/create/PartySelectBox';
import { TotalsSummaryBox, type TotalRow } from '@/components/create/TotalsSummaryBox';
import { LivePreviewPane } from '@/components/create/LivePreviewPane';

interface Row {
  key: number; description: string; amount: number;
  whtRate: number; recoverable: boolean;
  // Set by the ProductPicker (master) or defaults for an ad-hoc free-text line.
  // VAT is DERIVED from productType + the vendor's VAT status — never typed by hand.
  productId: number | null; productCode: string | null; productType: ProductTypeStr;
  // cont.77 — the 50ทวิ income type (WhtType) for this line. Picking one auto-fills the
  // WHT rate; null = no WHT (or fall back to the expense-category default at post).
  whtTypeId: number | null;
}
const emptyRow = (k: number): Row =>
  ({ key: k, description: '', amount: 0, whtRate: 0, recoverable: true,
     productId: null, productCode: null, productType: 'GOOD', whtTypeId: null });

function PvForm() {
  const t = useTranslations('pv');
  const tc = useTranslations('common');
  const tcr = useTranslations('create');
  const router = useRouter();
  const params = useSearchParams();
  const fromVi = params.get('fromVendorInvoiceId');
  // ITEM 9 — PO→PV convenience pre-fill (mirrors fromVendorInvoiceId). No backend
  // PO→PV endpoint/link; we just fetch the PO and seed vendor + line rows.
  const fromPo = params.get('fromPurchaseOrderId');
  const po = usePurchaseOrder(fromPo ? Number(fromPo) : null).data;
  const docDate = bangkokToday();
  const company = useCompanyProfile();

  const [vendorId, setVendorId] = useState<number | null>(null);
  const [vendorLabel, setVendorLabel] = useState('');
  // R4 — declared here (not at its old spot below) so the VI-prefill effect further down
  // can read it: it needs the vendor's CURRENT vatRegistered flag to compute the rate the
  // form will actually re-derive for the prefilled line.
  const vendor = useVendor(vendorId ?? 0).data;
  // Sprint BU-PURCH — business unit, optional unless the company opted in.
  const buRequired = useCompanyBuSetting().data?.requiresBusinessUnit ?? false;
  const [businessUnitId, setBusinessUnitId] = useState<number | null>(null);
  const [buError, setBuError] = useState(false);
  // PV→VI / PO→PV prefill: seed BU from the source doc if it carries one.
  const vi = useVendorInvoice(fromVi ? Number(fromVi) : 0).data;
  const [categoryId, setCategoryId] = useState<number | null>(null);
  const [catRecoverable, setCatRecoverable] = useState(true);
  const [method, setMethod] = useState<'Cash' | 'Transfer' | 'Cheque'>('Transfer');
  const [chequeNo, setChequeNo] = useState('');
  const [chequeDate, setChequeDate] = useState(docDate);
  const [rows, setRows] = useState<Row[]>([emptyRow(1)]);
  const [busy, setBusy] = useState(false);
  const [manualSelfWithhold, setManualSelfWithhold] = useState(false);
  // wht-grossup (2026-06-12) — when the payee won't be withheld, RD requires the absorbed
  // tax to be grossed up into the payee's income. ตลอดไป (forever) is the safe default.
  const [grossUpMode, setGrossUpMode] = useState<'GROSS_UP_FOREVER' | 'GROSS_UP_ONCE'>('GROSS_UP_FOREVER');

  // cont.77 — per-line WHT type (income type for the 50ทวิ).
  const whtTypes = useWhtTypes().data ?? [];

  // ITEM 9 — when arriving from a PO, seed the vendor + line rows once the PO
  // detail has loaded (guard with a ref-via-state so user edits aren't clobbered
  // on re-render). description ← line description, amount ← net, and the product
  // identity/taxonomy (productId/code/type) carry over so the derived VAT matches the
  // PO line's actual product (an exempt good stays 0%, not a flat 7%).
  const [poPrefilled, setPoPrefilled] = useState(false);
  useEffect(() => {
    if (!po || poPrefilled) return;
    setVendorId(po.vendorId);
    if (po.businessUnitId != null) setBusinessUnitId(po.businessUnitId);
    setRows(
      po.lines.length
        ? po.lines.map((l, i) => ({
            ...emptyRow(i + 1),
            description: l.descriptionTh,
            amount: l.lineAmount,
            productId: l.productId,
            productCode: l.productCode,
            productType: l.productType,
          }))
        : [emptyRow(1)],
    );
    setPoPrefilled(true);
  }, [po, poPrefilled]);

  // WP3 3.5 (F24) — arriving from a posted VI's "ชำระด้วยใบสำคัญจ่าย" CTA: prefill
  // vendor, one line ("ชำระ <VI docNo>", amount = outstanding = total − settled) and
  // the expense category/recoverable flag from the VI's first line — the user reviews
  // and adjusts everything from there (nothing is locked).
  const [viPrefilled, setViPrefilled] = useState(false);
  useEffect(() => {
    if (!vi || viPrefilled) return;
    setVendorId(vi.vendorId);
    setVendorLabel(vi.vendorName);
    if (vi.businessUnitId != null) setBusinessUnitId(vi.businessUnitId);
    const firstLine = vi.lines[0];
    if (firstLine) {
      setCategoryId(firstLine.expenseCategoryId);
      setCatRecoverable(firstLine.isRecoverableVat);
    }
    // R4 — `vendorId` was JUST set above on this same pass, so `vendor` (fetched by the
    // PREVIOUS vendorId, often null) hasn't caught up yet on this render; wait for it to
    // resolve to THIS VI's vendor before deriving the rate (this effect re-fires on the
    // `vendor` dependency below, still guarded by `!viPrefilled`).
    if (!vendor || vendor.vendorId !== vi.vendorId) return;
    const outstanding = Math.max(vi.totalAmount - vi.settledAmount, 0);
    // The PV line always RE-DERIVES VAT (rate = vendor.vatRegistered ? rate-for-productType
    // : 0) and adds it on top of `amount` — it never accepts a VAT-inclusive figure
    // directly. Ham decision: prefill must land the PV grand total EXACTLY on `outstanding`
    // in every vendor/VAT combo, so compute base from the SAME rate the form will actually
    // apply to this row (not the VI's own historical rate — those can differ, e.g. a
    // non-VAT-registered vendor's VI carrying non-recoverable VAT).
    const productType = firstLine?.productType ?? 'GOOD';
    const rate = vendor.vatRegistered ? taxRateForProductType(productType) : 0;
    const baseAmount = derivePvPrefillBase(outstanding, rate);
    setRows([{
      ...emptyRow(1),
      description: t('settleLineDesc', { docNo: vi.docNo ?? `#${vi.vendorInvoiceId}` }),
      amount: baseAmount,
      productType,
    }]);
    setViPrefilled(true);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [vi, viPrefilled, vendor]);

  // Sprint 8.7 — vendor flags drive self-withhold (auto/lock for foreign).
  const foreignNoVatD = !!vendor?.isForeign && !vendor.hasThaiVatDReg;
  const foreignVatD = !!vendor?.isForeign && !!vendor.hasThaiVatDReg;
  const selfWithholdLocked = !!vendor?.isForeign;
  const selfWithhold = foreignNoVatD ? true : foreignVatD ? false : manualSelfWithhold;

  // cont.77 — input VAT only when the vendor is VAT-registered (a non-VAT vendor issues
  // no tax invoice → nothing to claim) AND (for a foreign vendor) it holds a Thai VAT-D
  // registration (ม.82/5) — dual-flag rule, same predicate as vendor-invoices/new/page.tsx
  // (autoNoInputVat). A single-flag check missed foreignNoVatD vendors (vatRegistered can
  // default true even though hasThaiVatDReg is false), fabricating VAT that was never
  // charged (army B-rc F2). Default true until the vendor loads.
  const vendorVat = vendor ? vendor.vatRegistered && !foreignNoVatD : true;

  // VAT is derived, never typed: 0 if the vendor isn't VAT-registered (ม.82/5) or the
  // product is VAT-exempt (ม.81); otherwise the standard rate for that product type.
  const lineVat = (r: Row): number => (vendorVat ? taxRateForProductType(r.productType) : 0);
  const subtotal = rows.reduce((s, r) => s + r.amount, 0);
  // Round per line (2dp) to mirror the BE's per-line VatAmount so the preview total matches.
  const vat = rows.reduce((s, r) => s + Math.round(r.amount * lineVat(r) * 100) / 100, 0);
  // Mirrors WhtPayerModes.Compute (BE): gross-up the base under self-withhold.
  const whtFor = (amount: number, rate: number) => {
    if (rate <= 0) return 0;
    if (!selfWithhold) return amount * rate;
    return grossUpMode === 'GROSS_UP_FOREVER'
      ? (amount / (1 - rate)) * rate
      : amount * (1 + rate) * rate;
  };
  const wht = rows.reduce((s, r) => s + whtFor(r.amount, r.whtRate), 0);
  // Self-withhold pays the vendor in full; the (grossed) WHT goes to RD separately.
  const net = selfWithhold ? subtotal + vat : subtotal + vat - wht;

  // B1(a) (specs/fix-army-findings-2026-07-22.md, army B-bn F1) — a line with a WHT rate
  // but no selected Income Type (50ทวิ) used to sail through Draft-save AND Approve, then
  // 422 pv.wht_type_missing only at Post (with an already-misleading approve-confirm
  // preview shown, and an Approved PV had no way back). Block it here, client-side, before
  // it can even reach the server seam (PaymentVoucherService.CreateDraftAsync ~L281) that
  // now enforces the same rule. Simplification: this requires an EXPLICIT pick even for a
  // category with a server-side default Income Type (e.g. SVC) — the FE has no visibility
  // into that default, and always requiring an explicit selection keeps the 50ทวิ traceable
  // instead of depending on an invisible category default.
  const whtTypeMissing = rows.some((r) => r.whtRate > 0 && r.whtTypeId == null);
  const canSave =
    vendorId !== null && categoryId !== null &&
    rows.every((r) => r.description.trim() !== '' && r.amount > 0) &&
    !whtTypeMissing &&
    (method !== 'Cheque' || (chequeNo.trim() !== '' && chequeDate !== '')) &&
    (!buRequired || businessUnitId !== null);   // Sprint BU-PURCH

  function setRow(key: number, patch: Partial<Row>) {
    setRows((rs) => rs.map((r) => (r.key === key ? { ...r, ...patch } : r)));
  }

  async function saveDraft() {
    setBusy(true);
    try {
      const res = await apiPost<{ payment_voucher_id: number }>('payment-vouchers', {
        docDate,
        vendorId,
        businessUnitId,
        expenseCategoryId: categoryId,
        paymentMethod: method,
        chequeNo: method === 'Cheque' ? chequeNo : null,
        chequeDate: method === 'Cheque' ? chequeDate : null,
        bankAccountId: null,
        currencyCode: 'THB',
        exchangeRate: 1,
        description: fromVi
          ? `${t('settlingVi')} ${vi?.docNo ?? `#${fromVi}`}`
          : fromPo
            ? `${t('fromPo')} ${po?.docNo ?? `#${fromPo}`}`
            : null,
        notes: null,
        lines: rows.map((r) => ({
          expenseAccountId: null,
          description: r.description,
          amount: r.amount,
          taxCodeId: null,
          isRecoverableVat: catRecoverable,
          vatRate: lineVat(r),
          whtTypeId: r.whtTypeId,
          whtRate: r.whtRate,
          productType: r.productType,
        })),
        vendorInvoiceId: fromVi ? Number(fromVi) : null,
        selfWithholdMode: fromVi ? null : selfWithhold,
        whtPayerMode: fromVi ? null : selfWithhold ? grossUpMode : 'DEDUCT',
      });
      toast.success(tc('save'));
      router.push(`/payment-vouchers/${res.payment_voucher_id}`);
    } catch (e) {
      apiErrorToast(e);
    } finally {
      setBusy(false);
    }
  }

  const cfg = PAPER_DOC['payment-voucher'];
  const hasWht = wht > 0;

  const totalRows: TotalRow[] = [
    { label: t('subtotal'), value: subtotal },
    { label: t('vat'), value: vat },
    // Self-withhold: WHT is NOT deducted from the payment — shown as a separate
    // remit-to-RD line so the user sees the true cost of absorbing the tax.
    selfWithhold
      ? { label: t('selfWithhold.remitRow'), value: wht, muted: true }
      : { label: t('wht'), value: -wht, muted: true },
  ];

  const docMeta = fromVi
    ? `${t('settlingVi')} ${vi?.docNo ?? `#${fromVi}`}`
    : fromPo
      ? `${t('fromPo')} ${po?.docNo ?? `#${fromPo}`}`
      : docDate;

  return (
    <DocumentCreateLayout
      title={t('create')}
      docMeta={docMeta}
      actions={
        <>
          <button
            type="button"
            className="btn btn-ghost btn-sm"
            onClick={() => router.push('/payment-vouchers')}
            disabled={busy}
          >
            {tcr('cancel')}
          </button>
          <button
            type="button"
            className="btn btn-primary btn-sm"
            disabled={!canSave || busy}
            onClick={saveDraft}
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
            docNo={DRAFT_DOC_NO}
            issueDate={docDate}
            seller={companyToSeller(company.data)}
            partyLabel={VENDOR_PARTY_LABEL}
            customer={{
              name: vendorLabel || '—',
              taxId: vendor?.taxId ? formatTaxId(vendor.taxId) : null,
              branchCode: vendor?.branchCode ?? null,
              address: vendor?.address ?? null,
            }}
            items={rows.map((r) => ({ description: r.description, amount: r.amount }))}
            summary={{
              subtotal,
              beforeVat: subtotal,
              vat,
              total: subtotal + vat,
              // Self-withhold pays the vendor in full — the absorbed WHT is never a
              // deduction on the voucher (shown as a remit note in extraMetaBlock).
              wht: hasWht && !selfWithhold ? wht : null,
            }}
            signRoles={cfg.signRoles}
            extraMetaBlock={
              <div className="text-[12px] leading-relaxed text-ink-700">
                <div><b>{t('method')}:</b> {method}
                  {method === 'Cheque' && chequeNo ? ` (${chequeNo}${chequeDate ? ` / ${chequeDate}` : ''})` : ''}</div>
                {selfWithhold && wht > 0 && (
                  <div className="text-warning">
                    {t('selfWithhold.remitRow')}: {wht.toLocaleString('th-TH', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}
                  </div>
                )}
              </div>
            }
          />
        </LivePreviewPane>
      }
    >
      {/* ① ผู้ขาย */}
      <SectionCard number={1} title={t('vendor')}>
        <PartySelectBox
          kind="vendor"
          party={vendorId}
          onChange={(id, label) => { setVendorId(id); setVendorLabel(label); }}
        />
      </SectionCard>

      {/* ② ข้อมูลเอกสาร + การชำระเงิน */}
      <SectionCard number={2} title={tcr('docInfo')}>
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <DateInput value={docDate} locked label={tc('date')} />
          <ExpenseCategorySelector
            value={categoryId}
            onChange={(id, cat) => { setCategoryId(id); setCatRecoverable(cat.defaultIsRecoverableVat); }}
          />
          <BusinessUnitSelector
            value={businessUnitId}
            onChange={(id) => { setBusinessUnitId(id); if (id) setBuError(false); else setBuError(buRequired); }}
            required={buRequired}
            error={buError}
          />
          <label className="form-control">
            <span className="label-text">{t('method')}</span>
            <select className="select select-bordered" value={method}
              onChange={(e) => setMethod(e.target.value as 'Cash' | 'Transfer' | 'Cheque')}>
              <option value="Cash">Cash</option>
              <option value="Transfer">Transfer</option>
              <option value="Cheque">Cheque</option>
            </select>
          </label>
          {method === 'Cheque' && (
            <>
              <label className="form-control">
                <span className="label-text">Cheque No. *</span>
                <input className="input input-bordered" value={chequeNo}
                  onChange={(e) => setChequeNo(e.target.value)} />
              </label>
              <label className="form-control">
                <span className="label-text">Cheque Date *</span>
                <input type="date" className="input input-bordered" value={chequeDate}
                  onChange={(e) => setChequeDate(e.target.value)} />
                <span className="label-text-alt text-base-content/50">= {formatDateBE(chequeDate)}</span>
              </label>
            </>
          )}
        </div>
      </SectionCard>

      {/* ③ รายการ + self-withhold + totals */}
      <SectionCard number={3} title={t('lines')} rightMeta={tcr('lineCount', { n: rows.length })}>
        <div className="space-y-3">
          {rows.map((r, i) => (
            <div key={r.key} className="grid grid-cols-1 gap-3 rounded-lg border border-base-300 p-3 md:grid-cols-4">
              <label className="form-control md:col-span-2">
                <span className="label-text">{t('item')} *</span>
                {/* รายการดึงจาก /settings/products (purpose=purchase); พิมพ์เองได้สำหรับ ad-hoc. */}
                <ProductPicker
                  description={r.description}
                  ariaLabel={`${t('lineDescription')} ${i + 1}`}
                  purpose="purchase"
                  businessUnitId={businessUnitId}
                  // Editing the text drops the product link (now ad-hoc) but KEEPS the chosen
                  // productType so the derived VAT doesn't silently jump (e.g. an exempt line
                  // staying 0% instead of flipping to 7% on the first keystroke).
                  onDescriptionChange={(text) =>
                    setRow(r.key, { description: text, productId: null, productCode: null })}
                  onSelectProduct={(p) =>
                    setRow(r.key, {
                      description: p.nameTh, productId: p.productId, productCode: p.productCode,
                      productType: p.productType,
                      // master may seed price but never locks it (Ham cont.81): only fill if empty.
                      amount: r.amount > 0 ? r.amount : (p.defaultUnitPrice ?? 0),
                    })}
                />
              </label>
              <label className="form-control">
                <span className="label-text">{t('subtotal')} *</span>
                <input type="number" className="input input-bordered input-sm" value={r.amount}
                  onChange={(e) => setRow(r.key, { amount: Number(e.target.value) || 0 })} />
              </label>
              <label className="form-control">
                <span className="label-text">VAT</span>
                {/* คำนวณอัตโนมัติจากสินค้า/บริการ + สถานะ VAT ของผู้ขาย — แก้เองไม่ได้ */}
                <div className="input input-bordered input-sm flex items-center bg-base-200 text-base-content/70"
                  data-testid="pv-line-vat" title={!vendorVat ? t('vendorNoVat') : undefined}>
                  {(lineVat(r) * 100).toFixed(0)}%
                  {!vendorVat && <span className="ml-1 text-xs">· {t('vendorNoVatShort')}</span>}
                </div>
              </label>
              <label className="form-control md:col-span-2">
                <span className="label-text">{t('whtType')}</span>
                <select className="select select-bordered select-sm" value={r.whtTypeId ?? ''}
                  data-testid="pv-line-wht-type"
                  onChange={(e) => {
                    const tid = e.target.value ? Number(e.target.value) : null;
                    // Picking a type auto-fills its rate; clearing zeroes the WHT.
                    const picked = whtTypes.find((w) => w.whtTypeId === tid);
                    setRow(r.key, { whtTypeId: tid, whtRate: picked ? picked.rate : 0 });
                  }}>
                  <option value="">{t('whtTypeNone')}</option>
                  {whtTypes.map((w) => (
                    <option key={w.whtTypeId} value={w.whtTypeId}>
                      {w.nameTh} ({(w.rate * 100).toFixed(w.rate * 100 % 1 === 0 ? 0 : 2)}%)
                    </option>
                  ))}
                </select>
                {/* B1(a) — same rule as canSave above, surfaced inline on the offending row. */}
                {r.whtRate > 0 && r.whtTypeId == null && (
                  <span className="label-text-alt text-error" data-testid="pv-line-wht-type-required">
                    {t('whtTypeRequired')}
                  </span>
                )}
              </label>
              <label className="form-control">
                <span className="label-text">{t('wht')} %</span>
                <PercentRateInput
                  value={r.whtRate}
                  onValueChange={(f) => setRow(r.key, { whtRate: f })}
                  max={30}
                  aria-label={`${t('wht')} %`}
                />
              </label>
              <button className="btn btn-ghost btn-xs text-error md:col-span-3 md:ml-auto md:w-fit"
                onClick={() => setRows((rs) => rs.length > 1 ? rs.filter((x) => x.key !== r.key) : rs)}>
                <Trash2 className="h-3 w-3" /> {tc('delete')}
              </button>
            </div>
          ))}
        </div>
        <button
          type="button"
          className="mt-3 flex w-full items-center justify-center gap-2 rounded-field border border-dashed border-ink-200 bg-base-100 py-3 text-sm font-medium text-peach-700 hover:border-peach-300 hover:bg-peach-50"
          onClick={() => setRows((rs) => [...rs, emptyRow(Date.now())])}>
          <Plus className="h-4 w-4" aria-hidden /> {t('addLine')}
        </button>

        {/* B-rc F3 — settle-from-VI still auto-applies self-withhold GROSS_UP_FOREVER
            server-side for a foreignNoVatD vendor (saveDraft always sends
            selfWithholdMode/whtPayerMode null on fromVi; CreateDraftAsync auto-derives),
            so show the explanation here too, locked/read-only — the toggle and mode
            choice have no effect on this path (nothing is ever sent for them). */}
        {(!fromVi || foreignNoVatD) && (
          <div className="mt-4 rounded-lg border border-base-300 p-3">
            <label className="label cursor-pointer justify-start gap-3">
              <input type="checkbox" className="toggle toggle-warning toggle-sm"
                checked={selfWithhold} disabled={selfWithholdLocked || !!fromVi}
                onChange={(e) => setManualSelfWithhold(e.target.checked)} />
              <span className="font-semibold">{t('selfWithhold.toggle')}</span>
            </label>
            {foreignNoVatD && (
              <p className="mt-1 text-xs text-warning">{t('selfWithhold.autoLockedForeign')}</p>
            )}
            {foreignVatD && (
              <p className="mt-1 text-xs text-info">{t('selfWithhold.vatDInfo')}</p>
            )}
            {!selfWithholdLocked && selfWithhold && (
              <p className="mt-1 text-xs text-base-content/60">{t('selfWithhold.explanation')}</p>
            )}
            {selfWithhold && (
              <div className="mt-3 space-y-2" data-testid="pv-grossup-mode">
                {fromVi ? (
                  <p className="flex items-start gap-2 text-sm">
                    <span>
                      <span className="font-medium">{t('selfWithhold.mode.forever')}</span>
                      <span className="block text-xs text-base-content/60">{t('selfWithhold.mode.foreverHint')}</span>
                    </span>
                  </p>
                ) : (
                  (['GROSS_UP_FOREVER', 'GROSS_UP_ONCE'] as const).map((m) => (
                    <label key={m} className="flex cursor-pointer items-start gap-2 text-sm">
                      <input
                        type="radio" name="grossUpMode" className="radio radio-warning radio-sm mt-0.5"
                        checked={grossUpMode === m} onChange={() => setGrossUpMode(m)}
                      />
                      <span>
                        <span className="font-medium">
                          {m === 'GROSS_UP_FOREVER' ? t('selfWithhold.mode.forever') : t('selfWithhold.mode.once')}
                        </span>
                        <span className="block text-xs text-base-content/60">
                          {m === 'GROSS_UP_FOREVER' ? t('selfWithhold.mode.foreverHint') : t('selfWithhold.mode.onceHint')}
                        </span>
                      </span>
                    </label>
                  ))
                )}
                {wht > 0 && (
                  <p className="rounded-lg bg-warning/10 px-3 py-2 text-xs text-warning-content/80">
                    {t('selfWithhold.preview', {
                      remit: wht.toLocaleString('th-TH', { minimumFractionDigits: 2, maximumFractionDigits: 2 }),
                      effective: subtotal > 0 ? ((wht / subtotal) * 100).toFixed(4) : '0',
                    })}
                  </p>
                )}
              </div>
            )}
          </div>
        )}

        <div className="mt-4">
          <TotalsSummaryBox
            rows={totalRows}
            grandLabel={t('netPaid')}
            grandValue={net}
          />
        </div>
      </SectionCard>
    </DocumentCreateLayout>
  );
}

const SCOPE = 'purchase.payment_voucher.create';

export default function PaymentVoucherNewPage() {
  const tc = useTranslations('common');
  const perms = useMePermissions();
  const canCreate = perms.data?.isSuperAdmin || (perms.data?.permissions.includes(SCOPE) ?? false);
  if (perms.isPending) return null;
  if (perms.data && !canCreate) {
    return (
      <div className="flex flex-col items-center gap-2 py-12 text-center" data-testid="state-no-access">
        <ShieldAlert className="h-10 w-10 text-warning" aria-hidden />
        <div className="font-semibold">{tc('noAccessTitle')}</div>
        <div className="max-w-md text-sm text-base-content/60">{tc('noAccessBody', { perm: SCOPE })}</div>
      </div>
    );
  }
  return (
    <Suspense fallback={null}>
      <PvForm />
    </Suspense>
  );
}
