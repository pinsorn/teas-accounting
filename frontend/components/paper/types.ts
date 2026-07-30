import type { ReactNode } from 'react';

// Sprint 13j-FE — PaperDocument prop API. LOCKED (Answer-Sana-Backend29 §C4):
// QuestPDF (13j-PDF) mirrors this exact shape — do not change field names.

export interface SellerInfo {
  name: string;
  taxId: string;
  branchCode: string;
  address: string;
  logoUrl?: string | null;
  phone?: string | null;
  email?: string | null;
}

export interface CustomerInfo {
  name: string;
  taxId?: string | null;
  branchCode?: string | null;
  address?: string | null;
  contact?: string | null;
  phone?: string | null;
}

export interface PaperLineItem {
  description: string;
  descriptionSub?: string | null;
  quantity?: number | null;
  unit?: string | null;
  unitPrice?: number | null;
  discountPercent?: number | null;
  amount: number;
}

export interface PaperSummary {
  subtotal: number;
  discount?: number | null;
  beforeVat?: number | null;
  vat: number;
  total: number;
  vatRate?: number | null; // percent, e.g. 7
  // Optional document-specific VAT label. Payment Vouchers use this to explain
  // that VAT is non-creditable and folded into cost for a non-VAT company.
  // Bilingual, because every other row on the printed paper renders through Bi({th,en}) —
  // a single-locale override would print one language where its neighbours print both.
  vatLabel?: { th: string; en: string };
  // Non-VAT companies (ม.86): when false the foot collapses to a single Total
  // row (no Subtotal/Before-VAT/VAT). Mirrors C# PaperSummary.ShowVat. Defaults
  // true; PaperDocument fills it from /system/info.vatMode.
  showVat?: boolean;
  // Sprint 13j-PURCH D-supplement — withholding tax deduction (Payment Voucher).
  // Mirrors C# PaperSummary.Wht (nullable). When set: PaperFoot inserts a
  // "หัก ณ ที่จ่าย · WHT" row (shown as −amount) between VAT and the grand total,
  // independent of showVat, and the grand-total label switches to
  // "จ่ายสุทธิ · Net Paid" with value = total − wht. Absent → byte-identical to today.
  wht?: number | null;
  // cont.119 — Tax Invoice with mixed taxable/exempt lines (ม.86/4 #5): when > 0 a
  // "มูลค่าสินค้าที่ได้รับยกเว้น · Exempt" row prints after Before VAT. Mirrors C#
  // PaperSummary.NonTaxable (the PDF already had it; the screen was missing it).
  nonTaxable?: number | null;
}

// §C4 locked union. `info` added additively (non-breaking) for the Billing
// Note "ออกแล้ว" default watermark (§C7) — success/danger/warning unchanged.
export type WatermarkVariant = 'success' | 'danger' | 'warning' | 'info';

export interface PaperDocumentProps {
  docType: string; // "ใบเสนอราคา"
  docTypeEn: string; // "QUOTATION"
  docNo: string;
  issueDate: string; // ISO; formatted to Buddhist-era DD/MM/YYYY+543 inside
  validUntil?: string;
  validUntilLabel?: string;
  seller: SellerInfo;
  customer: CustomerInfo;
  // Sprint 13j-PURCH (BP-03) — OPTIONAL party-box label override. The party box
  // is hardcoded "ลูกค้า / Customer" by default (every Sales caller + the Vendor
  // Invoice keep that, byte-identical). Purchase docs where our company is NOT
  // the customer pass their own label: PO = "ผู้ขาย / Vendor", PV = "ผู้รับเงิน /
  // Payee". Absent → renders exactly as today.
  partyLabel?: { th: string; en: string };
  items: PaperLineItem[];
  summary: PaperSummary;
  amountWords?: string; // pre-computed Thai baht text (else derived from total)
  notes?: string | null;
  // `middle` is optional (Sprint 13j-PURCH D-supplement). Mirrors C#
  // PaperSignRoles.Middle: present → PaperSign renders a 3-box strip
  // (ผู้จัดทำ / ผู้อนุมัติ / ผู้รับเงิน for the Payment Voucher); absent → the
  // standard two-box strip, byte-identical to every Sales caller.
  signRoles: { left: string; middle?: string; right: string };
  watermark?: { text: string; variant: WatermarkVariant };
  extraMetaBlock?: ReactNode;
  // doc-signature-and-foot-layout §F2.1 — replaces the dead `signatureImg` text-hack prop
  // (no callers existed outside the three declaration sites). Mirrors C# PaperSignatures.
  signatures?: PaperSignaturesDto | null;
}

// doc-signature-and-foot-layout §D3/§F2.1 — mirrors C# PaperSignatures. URLs/positions/names are
// serialized; the FE loads images through the BFF proxy exactly like the company logo (no byte
// fields on the wire — those are [JsonIgnore]'d server-side, same split as PaperSeller.Logo).
export interface PaperSignaturesDto {
  leftUrl?: string | null;
  middleUrl?: string | null;
  stampUrl?: string | null;
  leftPosition?: string | null;
  middlePosition?: string | null;
  leftName?: string | null;
  middleName?: string | null;
  stampOnMiddle: boolean;
}

// ---------------------------------------------------------------------------
// cont.121 — canonical paper DTO (spec docs/superpowers/specs/2026-07-02-
// canonical-paper-dto.md). JSON returned by GET /{doc}/{id}/paper — the SAME
// server-side composition the QuestPDF renderer consumes (C# PaperDocModel,
// camelCase). Screen == print by construction. Distinct from the LOCKED
// PaperDocumentProps above (which stays the render-prop contract); the
// paperDtoToProps adapter in lib/paper-doc-config.ts bridges the two.
// NOTE: PaperSeller.Logo/LogoSvg are [JsonIgnore]d server-side — the FE keeps
// its own logo source (company profile), merged in by the adapter.

export interface PaperSellerDto {
  name: string;
  taxId: string;
  branchCode: string;
  address: string;
  phone?: string | null;
  email?: string | null;
}

export interface PaperCustomerDto {
  name: string;
  taxId?: string | null;
  branchCode?: string | null;
  address?: string | null;
  contact?: string | null;
  phone?: string | null;
}

export interface PaperLineDto {
  description: string;
  descriptionSub?: string | null;
  quantity?: number | null;
  unit?: string | null;
  unitPrice?: number | null;
  discountPercent?: number | null;
  amount: number;
}

export interface PaperSummaryDto {
  subtotal: number;
  discount?: number | null;
  beforeVat?: number | null;
  vat: number;
  total: number;
  vatRate?: number | null;
  showVat: boolean;
  wht?: number | null;
  nonTaxable?: number | null;
}

export interface PaperSignRolesDto {
  left: string;
  right: string;
  middle?: string | null;
}

export interface PaperPartyLabelDto {
  th: string;
  en: string;
}

export interface PaperWatermarkDto {
  text: string;
  variant: WatermarkVariant;
}

export interface PaperDocDto {
  docType: string;
  docTypeEn: string;
  docNo: string;
  issueDate: string; // "yyyy-MM-dd"
  seller: PaperSellerDto;
  customer: PaperCustomerDto;
  items: PaperLineDto[];
  summary: PaperSummaryDto;
  signRoles: PaperSignRolesDto;
  validUntil?: string | null; // "yyyy-MM-dd"
  validUntilLabel?: string | null;
  amountWords?: string | null;
  notes?: string | null;
  watermark?: PaperWatermarkDto | null;
  partyLabel?: PaperPartyLabelDto | null;
  signatures?: PaperSignaturesDto | null;
}

// F-A (specs/fix-e2e-v1260-findings.md) — money semantics, canonical source
// Infrastructure/Pdf/PaperFootPlan.cs:17-18,29: summary.total is the NET when summary.wht is
// set ("จ่ายสุทธิ"), NOT the Grand Total — Grand = total + wht, Net = total. Extracted to a pure,
// exported function (PLAN-test-hardening.md C4 mirror-contract test — was two inline lines in
// PaperFoot, each side testing only itself) so a shared BE/FE fixture can assert the two
// implementations agree instead of drifting silently (the 700/850 bug this guards against).
export function computeFootTotals(summary: PaperSummary): {
  grandTotal: number;
  netTotal: number;
  hasWht: boolean;
} {
  const hasWht = summary.wht != null;
  const grandTotal = hasWht ? summary.total + (summary.wht ?? 0) : summary.total;
  const netTotal = summary.total;
  return { grandTotal, netTotal, hasWht };
}

// Plain numeric formatter (2 dp, th-TH) for paper line/total columns.
export function fmtPaperNum(n: number | null | undefined, dp = 2): string {
  const v = typeof n === 'number' && Number.isFinite(n) ? n : 0;
  return v.toLocaleString('th-TH', { minimumFractionDigits: dp, maximumFractionDigits: dp });
}

// Buddhist-era short date DD/MM/(year+543). Mirrors design fmtDateShort.
export function fmtPaperDate(s?: string | null): string {
  if (!s) return '—';
  const d = new Date(s);
  if (Number.isNaN(d.getTime())) return '—';
  const dd = String(d.getDate()).padStart(2, '0');
  const mm = String(d.getMonth() + 1).padStart(2, '0');
  return `${dd}/${mm}/${d.getFullYear() + 543}`;
}
