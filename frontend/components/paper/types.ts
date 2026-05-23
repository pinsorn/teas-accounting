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
  // Non-VAT companies (ม.86): when false the foot collapses to a single Total
  // row (no Subtotal/Before-VAT/VAT). Mirrors C# PaperSummary.ShowVat. Defaults
  // true; PaperDocument fills it from /system/info.vatMode.
  showVat?: boolean;
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
  items: PaperLineItem[];
  summary: PaperSummary;
  amountWords?: string; // pre-computed Thai baht text (else derived from total)
  notes?: string | null;
  signRoles: { left: string; right: string };
  watermark?: { text: string; variant: WatermarkVariant };
  extraMetaBlock?: ReactNode;
  signatureImg?: string;
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
