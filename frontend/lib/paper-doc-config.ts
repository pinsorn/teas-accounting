import type { CompanyProfile, CustomerDetail } from '@/lib/types';
import type { SellerInfo, CustomerInfo, WatermarkVariant } from '@/components/paper/types';
import { formatTaxId } from '@/lib/utils';

// Sprint 13j-FE §C7 — per-doctype titles, sign roles, and watermark rules.
// Passed into PaperDocument via props (PaperDocument never hardcodes these).

export type PaperDocKind =
  | 'quotation'
  | 'sales-order'
  | 'delivery-order'
  | 'tax-invoice'
  | 'receipt'
  | 'credit-note'
  | 'debit-note'
  | 'billing-note';

interface DocPaperConfig {
  docType: string;
  docTypeEn: string;
  signRoles: { left: string; right: string };
  validUntilLabel?: string;
}

export const PAPER_DOC: Record<PaperDocKind, DocPaperConfig> = {
  quotation: { docType: 'ใบเสนอราคา', docTypeEn: 'QUOTATION', signRoles: { left: 'ผู้เสนอราคา', right: 'ผู้รับใบเสนอราคา' }, validUntilLabel: 'ยืนราคาถึง' },
  'sales-order': { docType: 'ใบสั่งขาย', docTypeEn: 'SALES ORDER', signRoles: { left: 'ผู้ขาย', right: 'ผู้สั่งซื้อ' } },
  'delivery-order': { docType: 'ใบส่งของ', docTypeEn: 'DELIVERY ORDER', signRoles: { left: 'ผู้ส่งของ', right: 'ผู้รับของ' } },
  'tax-invoice': { docType: 'ใบกำกับภาษี', docTypeEn: 'TAX INVOICE', signRoles: { left: 'ผู้ออกใบกำกับ', right: 'ผู้ซื้อ' }, validUntilLabel: 'ครบกำหนดชำระ' },
  receipt: { docType: 'ใบเสร็จรับเงิน', docTypeEn: 'RECEIPT', signRoles: { left: 'ผู้รับเงิน', right: 'ผู้จ่ายเงิน' } },
  'credit-note': { docType: 'ใบลดหนี้', docTypeEn: 'CREDIT NOTE', signRoles: { left: 'ผู้ออกใบลดหนี้', right: 'ผู้ซื้อ' } },
  'debit-note': { docType: 'ใบเพิ่มหนี้', docTypeEn: 'DEBIT NOTE', signRoles: { left: 'ผู้ออกใบเพิ่มหนี้', right: 'ผู้ซื้อ' } },
  'billing-note': { docType: 'ใบแจ้งหนี้', docTypeEn: 'INVOICE', signRoles: { left: 'ผู้ออกใบแจ้งหนี้', right: 'ผู้รับใบแจ้งหนี้' }, validUntilLabel: 'ครบกำหนดชำระ' },
};

const CANCELLED = new Set(['Cancelled', 'Voided', 'Rejected']);

// §C7 matrix → {text, variant} | undefined. Cancelled/voided always shows the
// danger "ยกเลิก" watermark; posted/finalized states show the doctype default.
export function paperWatermark(
  kind: PaperDocKind,
  status: string,
): { text: string; variant: WatermarkVariant } | undefined {
  if (CANCELLED.has(status)) return { text: 'ยกเลิก', variant: 'danger' };
  switch (kind) {
    case 'quotation':
      return undefined; // §C7: Quotation has no positive watermark
    case 'sales-order':
      return ['Confirmed', 'Posted', 'Closed'].includes(status)
        ? { text: 'ยืนยันแล้ว', variant: 'success' }
        : undefined;
    case 'delivery-order':
      return status === 'Delivered' ? { text: 'ส่งของแล้ว', variant: 'success' } : undefined;
    case 'tax-invoice':
    case 'receipt':
    case 'credit-note':
    case 'debit-note':
      return status === 'Posted' ? { text: 'ต้นฉบับ', variant: 'success' } : undefined;
    case 'billing-note':
      return ['Posted', 'Issued', 'Settled'].includes(status)
        ? { text: 'ออกแล้ว', variant: 'info' }
        : undefined;
  }
}

// Customer master → paper customer block. Q/SO/DO/BN details only carry
// customerId+name, so the page fetches the master record (useCustomer) and
// merges its address/taxId/branch/contact/phone here. (TaxInvoice/CN use their
// own posted snapshot instead — immutable per compliance.)
export function custInfo(name: string, c: CustomerDetail | undefined): CustomerInfo {
  return {
    name,
    taxId: c?.taxId ? formatTaxId(c.taxId) : null,
    branchCode: c?.branchCode ?? null,
    address: c?.billingAddress ?? null,
    contact: c?.contactPerson ?? null,
    phone: c?.phone ?? null,
  };
}

// CompanyProfile → seller block (used for Q/SO/DO/BN/CN/DN/Receipt; TaxInvoice
// uses its own posted snapshot instead).
export function companyToSeller(c: CompanyProfile | undefined): SellerInfo {
  if (!c) {
    return { name: '—', taxId: '', branchCode: '00000', address: '' };
  }
  const addr = [
    c.registeredAddressLine1,
    c.registeredAddressLine2,
    c.registeredSubdistrict,
    c.registeredDistrict,
    c.registeredProvince,
    c.registeredPostalCode,
  ]
    .filter(Boolean)
    .join(' ');
  return {
    name: c.tradeName || c.legalName,
    taxId: c.taxId,
    branchCode: c.branchCode || '00000',
    address: addr,
    logoUrl: c.logoUrl,
    phone: c.phone,
    email: c.email,
  };
}
