'use client';

import { Suspense, useEffect, useMemo, useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { Plus, ShieldAlert, Trash2 } from 'lucide-react';
import { apiErrorToast } from '@/lib/api/errors';
import { DateInput } from '@/components/ui/DateInput';
import { ExpenseCategorySelector } from '@/components/ui/ExpenseCategorySelector';
import { ProductTypeSelect } from '@/components/ui/ProductTypeSelect';
import { BusinessUnitSelector } from '@/components/ui/BusinessUnitSelector';
import { PostConfirmDialog } from '@/components/ui/PostConfirmDialog';
import { PercentRateInput } from '@/components/ui/PercentRateInput';
import {
  useCreateVendorInvoice, usePostVendorInvoice, useVendor,
  usePurchaseOrders, usePurchaseOrder, useCompanyBuSetting, useCompanyProfile, useSystemInfo,
  useMePermissions,
} from '@/lib/queries';
import { derivePoLineVatRate } from '@/lib/po-line-vat';
import type { ProductTypeStr } from '@/lib/types';
import { bangkokToday, formatDateBE, formatTHB } from '@/lib/utils';
import { PaperDocument } from '@/components/paper/PaperDocument';
import { PAPER_DOC, companyToCustomer, DRAFT_DOC_NO } from '@/lib/paper-doc-config';
import { DocumentCreateLayout } from '@/components/create/DocumentCreateLayout';
import { SectionCard } from '@/components/create/SectionCard';
import { PartySelectBox } from '@/components/create/PartySelectBox';
import { TotalsSummaryBox, type TotalRow } from '@/components/create/TotalsSummaryBox';
import { LivePreviewPane } from '@/components/create/LivePreviewPane';

interface Row {
  key: number; categoryId: number | null; recoverable: boolean;
  description: string; amount: number; vatRate: number;
  productType: ProductTypeStr;
}
const emptyRow = (k: number): Row =>
  ({ key: k, categoryId: null, recoverable: true, description: '', amount: 0, vatRate: 0.07, productType: 'GOOD' });

// ม.82/4 — claim period options: vendor TI month .. +6 (yyyymm).
function claimOptions(vendorTiDate: string): number[] {
  const d = new Date(vendorTiDate + 'T00:00:00');
  if (Number.isNaN(d.getTime())) return [];
  return Array.from({ length: 7 }, (_, m) => {
    const x = new Date(d.getFullYear(), d.getMonth() + m, 1);
    return x.getFullYear() * 100 + (x.getMonth() + 1);
  });
}

function VendorInvoiceNewForm() {
  const t = useTranslations('vi');
  const tc = useTranslations('common');
  const tcr = useTranslations('create');
  const router = useRouter();
  const params = useSearchParams();
  // WP3 3.2 (F8) — arriving from an approved PO's "บันทึกใบกำกับภาษีซื้อ" CTA:
  // preselect the vendor + link the PO (mirrors the PV form's fromPurchaseOrderId
  // / fromVendorInvoiceId prefill pattern).
  const fromPoId = params.get('fromPurchaseOrderId');
  const create = useCreateVendorInvoice();
  const post = usePostVendorInvoice();
  const company = useCompanyProfile();
  const docDate = bangkokToday(); // locked (CLAUDE.md §10)

  const [vendorId, setVendorId] = useState<number | null>(null);
  const [vendorLabel, setVendorLabel] = useState('');
  const [tiNo, setTiNo] = useState('');
  const [tiDate, setTiDate] = useState(docDate);
  const [claim, setClaim] = useState<number | null>(null);
  const [rows, setRows] = useState<Row[]>([emptyRow(1)]);
  const [confirm, setConfirm] = useState<{ id: number } | null>(null);
  const [poId, setPoId] = useState<number | null>(fromPoId ? Number(fromPoId) : null);
  // Sprint BU-PURCH — business unit, optional unless the company opted in.
  const buRequired = useCompanyBuSetting().data?.requiresBusinessUnit ?? false;
  const [businessUnitId, setBusinessUnitId] = useState<number | null>(null);
  const [buError, setBuError] = useState(false);

  // Sprint 8.7 — vendor flags drive has_input_vat (auto/lock for non-VAT /
  // foreign-no-VAT-D vendors). Backend re-derives if not sent; we send explicit.
  // Declared before the PO-link effect below (WP1.3) so its derivation can read vendor VAT
  // status — note this reads the CLOSURE's vendor at effect-run time, which on the
  // "arrive via PO CTA" path (fromPoId) is still stale on the very first run (vendorId hasn't
  // propagated to a re-render yet); the manual "link a PO from the dropdown" path (vendor
  // already selected first) is unaffected.
  const vendor = useVendor(vendorId ?? 0).data;
  const autoNoInputVat = !!vendor &&
    (!vendor.vatRegistered || (vendor.isForeign && !vendor.hasThaiVatDReg));
  const foreignNoVatD = !!vendor?.isForeign && !vendor.hasThaiVatDReg;
  const hasInputVat = !autoNoInputVat;

  // WP1.2 (F27, D1) / WP1.3 (F14) — company VAT config (source of truth on the BE; FE mirror
  // only). sys.vatMode === Company.VatRegistered for the caller's tenant (ICompanyTaxConfigService).
  const sys = useSystemInfo().data;
  const companyVatRegistered = sys?.vatMode ?? true;
  const stdRate = sys?.vatRate ?? 0.07;

  // Sprint 12 — optional PO link: list Approved POs of the chosen vendor;
  // selecting one pulls its lines into the VI (category still picked by user).
  const approvedPos = usePurchaseOrders('Approved', vendorId ?? undefined).data ?? [];
  const poDetail = usePurchaseOrder(poId).data;
  useEffect(() => {
    if (!poDetail || poDetail.purchaseOrderId !== poId) return;
    if (poDetail.businessUnitId != null) setBusinessUnitId(poDetail.businessUnitId);
    // WP3 3.2 — only auto-pick the vendor when we arrived via the query param (the
    // in-form "linkPo" dropdown is a manual choice made AFTER a vendor is picked, so
    // it must never clobber the user's own selection).
    if (fromPoId) {
      setVendorId(poDetail.vendorId);
      setVendorLabel(poDetail.vendorName);
    }
    // WP5 (specs/fix-swarm-findings-all.md, round-4 ap01 HIGH) — MERGE, don't replace: this
    // effect fires again whenever `poDetail` settles, and used to blindly null out every row's
    // categoryId even when the user had already picked one on a pre-existing row (by position) —
    // e.g. the single placeholder row, picked during the async gap before this PO's lines
    // arrived. That silently discarded the pick with no error/toast, and `canSave` (below)
    // requires every row to have a categoryId, so Post never enabled. Preserve a row's own
    // already-chosen categoryId (+ its matching `recoverable`, derived together at pick time —
    // see ExpenseCategorySelector's onChange below) across the replace; description/amount/
    // vatRate always come from the PO (that IS the point of linking one).
    setRows((prev) => poDetail.lines.map((l, i) => ({
      key: i + 1,
      categoryId: prev[i]?.categoryId ?? null,
      // F-1 (Opus Tier-2 review, WP1.2) — force non-recoverable on a non-VAT company so the
      // live preview's totals box matches how the server actually posts (never claim it under
      // the recoverable bucket only to have GL book it as cost).
      recoverable: prev[i]?.categoryId != null ? prev[i]!.recoverable : companyVatRegistered,
      description: l.descriptionTh,
      amount: l.lineAmount,
      // WP1.3 (F14) — scoped derivation: only fall back to the company std rate when BOTH
      // company and vendor are VAT-registered; a genuinely non-VAT PO/vendor stays 0%.
      vatRate: derivePoLineVatRate(l, companyVatRegistered, !!vendor?.vatRegistered, stdRate),
      productType: 'GOOD' as ProductTypeStr,
    })));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [poDetail, poId, fromPoId]);

  const options = useMemo(() => claimOptions(tiDate), [tiDate]);
  const effClaim = claim ?? options[0] ?? null;

  const subtotal = rows.reduce((s, r) => s + r.amount, 0);
  // F-1 (Opus Tier-2 review, WP1.2) — AND with companyVatRegistered here too (not just at the
  // row-set sites) so the split is correct even before a category is picked (a fresh row
  // defaults recoverable=true and the server's guard doesn't apply until save).
  const rowIsRecoverable = (r: Row) => companyVatRegistered && r.recoverable;
  const vatRec = rows.reduce((s, r) => s + (rowIsRecoverable(r) ? r.amount * r.vatRate : 0), 0);
  const vatNon = rows.reduce((s, r) => s + (!rowIsRecoverable(r) ? r.amount * r.vatRate : 0), 0);
  const total = subtotal + vatRec + vatNon;

  const canSave =
    vendorId !== null && tiNo.trim() !== '' &&
    rows.length > 0 && rows.every((r) => r.categoryId !== null && r.amount > 0 && r.description.trim() !== '') &&
    (!buRequired || businessUnitId !== null);   // Sprint BU-PURCH

  function setRow(key: number, patch: Partial<Row>) {
    setRows((rs) => rs.map((r) => (r.key === key ? { ...r, ...patch } : r)));
  }

  async function saveDraft(): Promise<number | null> {
    try {
      const res = await create.mutateAsync({
        docDate,
        vendorId: vendorId!,
        vendorTaxInvoiceNo: tiNo,
        vendorTaxInvoiceDate: tiDate,
        vatClaimPeriod: effClaim,
        currencyCode: 'THB',
        exchangeRate: 1,
        notes: null,
        purchaseOrderId: poId,
        businessUnitId,
        lines: rows.map((r) => ({
          expenseCategoryId: r.categoryId!,
          expenseAccountId: null,
          description: r.description,
          amount: r.amount,
          vatRate: r.vatRate,
          productType: r.productType,
        })),
        hasInputVat,
      });
      toast.success(tc('save'));
      return res.vendor_invoice_id;
    } catch (e) {
      apiErrorToast(e);
      return null;
    }
  }

  const cfg = PAPER_DOC['vendor-invoice'];

  // F-D (specs/fix-purchase-nonvat-ux.md) — posting is already VAT-mode-aware (a non-VAT
  // company's rowIsRecoverable is always false, so vatRec is always 0 here), so VI stays
  // visible/usable in a non-VAT company. Just drop the "ภาษีซื้อ (เครดิตได้)" recoverable-VAT
  // row for that company — the wording implies a credit that structurally can never happen there.
  const totalRows: TotalRow[] = [
    { label: t('subtotal'), value: subtotal },
    ...(companyVatRegistered ? [{ label: t('vat'), value: vatRec }] : []),
    { label: t('nonRecVat'), value: vatNon, muted: true },
  ];

  return (
    <DocumentCreateLayout
      title={t('create')}
      docMeta={`${t('docDate')}: ${docDate}`}
      actions={
        <>
          <button
            type="button"
            className="btn btn-ghost btn-sm"
            onClick={() => router.push('/vendor-invoices')}
            disabled={create.isPending}
          >
            {tcr('cancel')}
          </button>
          <button
            type="button"
            className="btn btn-outline btn-sm border-ink-200 text-ink-700 hover:bg-ink-75"
            disabled={!canSave || create.isPending}
            onClick={async () => { const id = await saveDraft(); if (id) router.push('/vendor-invoices'); }}
          >
            {t('saveDraft')}
          </button>
          <button
            type="button"
            className="btn btn-primary btn-sm"
            disabled={!canSave || create.isPending}
            onClick={async () => { const id = await saveDraft(); if (id) setConfirm({ id }); }}
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
            docNo={tiNo || DRAFT_DOC_NO}
            issueDate={tiDate}
            seller={{
              name: vendorLabel || '—',
              taxId: vendor?.taxId ?? '',
              branchCode: vendor?.branchCode ?? '00000',
              address: vendor?.address ?? '',
            }}
            customer={companyToCustomer(company.data)}
            items={rows.map((r) => ({ description: r.description, amount: r.amount }))}
            summary={{
              subtotal,
              beforeVat: subtotal,
              vat: vatRec + vatNon,
              total,
            }}
            signRoles={cfg.signRoles}
          />
        </LivePreviewPane>
      }
    >
      {/* ① ผู้ขาย */}
      <SectionCard number={1} title={t('vendor')}>
        <PartySelectBox
          kind="vendor"
          party={vendorId}
          onChange={(id, label) => { setVendorId(id); setVendorLabel(label); setPoId(null); }}
        />
        {vendor && (
          <div className="mt-2 text-xs">
            {foreignNoVatD
              ? <span className="text-warning">{t('pnd36Warning')}</span>
              : autoNoInputVat
                ? <span className="text-info">{t('noInputVatInfo')}</span>
                : vendor.isForeign
                  ? <span className="text-info">{t('vatDInfo')}</span>
                  : null}
          </div>
        )}
        {/* WP1.2 (F27, D1) — advisory only; the server forces non-recoverable regardless. */}
        {!companyVatRegistered && (
          <div className="mt-2 text-xs text-info">{t('companyNonVatInfo')}</div>
        )}
        {/* WP1.4 (F13) — non-blocking nudge: a domestic VAT-registered vendor with no taxId
            can't support an input-VAT claim (ภ.พ.30). Legacy data still saves. */}
        {vendor && vendor.vatRegistered && !vendor.isForeign && !vendor.taxId && (
          <div className="mt-2 text-xs text-warning">{t('vendorTaxIdMissingWarning')}</div>
        )}
      </SectionCard>

      {/* ② ข้อมูลเอกสาร */}
      <SectionCard number={2} title={tcr('docInfo')}>
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <DateInput value={docDate} locked label={t('docDate')} />
          <BusinessUnitSelector
            value={businessUnitId}
            onChange={(id) => { setBusinessUnitId(id); if (id) setBuError(false); else setBuError(buRequired); }}
            required={buRequired}
            error={buError}
          />
          {vendorId !== null && approvedPos.length > 0 && (
            <label className="form-control sm:col-span-2">
              <span className="label-text">{t('linkPo')}</span>
              <select className="select select-bordered" data-testid="vi-po-select"
                value={poId ?? ''}
                onChange={(e) => setPoId(e.target.value ? Number(e.target.value) : null)}>
                <option value="">{t('noPo')}</option>
                {approvedPos.map((p) => (
                  <option key={p.purchaseOrderId} value={p.purchaseOrderId}>
                    {p.docNo ?? `#${p.purchaseOrderId}`} — {formatTHB(p.totalAmount)}
                  </option>
                ))}
              </select>
              <span className="label-text-alt text-base-content/50">{t('linkPoHint')}</span>
            </label>
          )}
          <label className="form-control">
            <span className="label-text">{t('vendorTiNo')} *</span>
            <input className="input input-bordered" value={tiNo}
              onChange={(e) => setTiNo(e.target.value)} />
          </label>
          <label className="form-control">
            <span className="label-text">{t('vendorTiDate')} *</span>
            <input type="date" className="input input-bordered" value={tiDate}
              max={docDate} onChange={(e) => { setTiDate(e.target.value); setClaim(null); }} />
            <span className="label-text-alt text-base-content/50">= {formatDateBE(tiDate)}</span>
          </label>
          <label className="form-control sm:col-span-2">
            <span className="label-text">{t('claimPeriod')}</span>
            <select className="select select-bordered" value={effClaim ?? ''}
              onChange={(e) => setClaim(Number(e.target.value))}>
              {options.map((o) => (
                <option key={o} value={o}>
                  {String(o).slice(0, 4)}-{String(o).slice(4)}
                </option>
              ))}
            </select>
            <span className="label-text-alt text-base-content/50">{t('claimHint')}</span>
          </label>
        </div>
      </SectionCard>

      {/* ③ รายการ + totals */}
      <SectionCard number={3} title={t('lines')} rightMeta={tcr('lineCount', { n: rows.length })}>
        <div className="space-y-3">
          {rows.map((r) => (
            <div key={r.key} className="rounded-lg border border-base-300 p-3">
              <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
                <ExpenseCategorySelector
                  value={r.categoryId}
                  onChange={(id, cat) =>
                    // F-1 (Opus Tier-2 review, WP1.2) — a non-VAT company forces non-recoverable
                    // regardless of the category's own default (server does the same in
                    // BuildLinesAsync + the CreateDraftAsync/UpdateDraftAsync/PostAsync guards).
                    setRow(r.key, {
                      categoryId: id,
                      recoverable: companyVatRegistered && cat.defaultIsRecoverableVat,
                    })}
                />
                <ProductTypeSelect
                  value={r.productType}
                  onChange={(v) => setRow(r.key, { productType: v })}
                  testId="vi-line-product-type"
                />
                {/* F-C (specs/fix-purchase-nonvat-ux.md) — default-height controls throughout
                    this row (matches the doc-create forms convention; ExpenseCategorySelector
                    was always default-height, these siblings were the -sm outliers). */}
                <label className="form-control">
                  <span className="label-text">{t('description')} *</span>
                  <input className="input input-bordered" value={r.description}
                    onChange={(e) => setRow(r.key, { description: e.target.value })} />
                </label>
                <label className="form-control">
                  <span className="label-text">{t('amount')} *</span>
                  <input type="number" className="input input-bordered" value={r.amount}
                    onChange={(e) => setRow(r.key, { amount: Number(e.target.value) || 0 })} />
                </label>
                <label className="form-control">
                  <span className="label-text">{t('vatRate')}</span>
                  <PercentRateInput
                    value={r.vatRate}
                    onValueChange={(f) => setRow(r.key, { vatRate: f })}
                    max={30}
                    quickSet={[0, 7]}
                    aria-label={t('vatRate')}
                    size="md"
                  />
                </label>
              </div>
              <div className="mt-2 flex items-center justify-between">
                {!r.recoverable && r.categoryId !== null && (
                  <span className="text-warning text-xs">⚠ {t('nonRecWarn')}</span>
                )}
                <button className="btn btn-ghost btn-xs text-error ml-auto gap-1"
                  onClick={() => setRows((rs) => rs.length > 1 ? rs.filter((x) => x.key !== r.key) : rs)}>
                  <Trash2 className="h-3 w-3" /> {t('remove')}
                </button>
              </div>
            </div>
          ))}
        </div>
        <button
          type="button"
          className="mt-3 flex w-full items-center justify-center gap-2 rounded-field border border-dashed border-ink-200 bg-base-100 py-3 text-sm font-medium text-peach-700 hover:border-peach-300 hover:bg-peach-50"
          onClick={() => setRows((rs) => [...rs, emptyRow(Date.now())])}>
          <Plus className="h-4 w-4" aria-hidden /> {t('addLine')}
        </button>

        <div className="mt-4">
          <TotalsSummaryBox
            rows={totalRows}
            grandLabel={t('total')}
            grandValue={total}
          />
        </div>
      </SectionCard>

      <PostConfirmDialog
        docType="vendor_invoice"
        open={confirm !== null}
        busy={post.isPending}
        summary={{ customer: vendorLabel || `#${vendorId}`, total, vat: vatRec }}
        recipients={[]}
        onClose={() => setConfirm(null)}
        onConfirm={async () => {
          if (!confirm) return;
          try {
            const r = await post.mutateAsync(confirm.id);
            toast.success(tc('save'));
            if (r?.poOverReceiptWarning)
              toast.warning(t('poOverReceipt'), { description: r.poOverReceiptWarning });
            router.push(`/vendor-invoices/${confirm.id}`);
          } catch (e) {
            apiErrorToast(e);
          } finally {
            setConfirm(null);
          }
        }}
      />
    </DocumentCreateLayout>
  );
}

const SCOPE = 'purchase.vendor_invoice.create';

// useSearchParams() requires a Suspense boundary (mirrors payment-vouchers/new/page.tsx).
export default function VendorInvoiceNewPage() {
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
      <VendorInvoiceNewForm />
    </Suspense>
  );
}
