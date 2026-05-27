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
  | 'billing-note'
  // Sprint 13j-PURCH D3 — PO prints as a buyer-issued purchase order (seller block =
  // our company, customer block = the vendor we order from).
  | 'purchase-order'
  // Sprint 13j-PURCH D-supplement — PV is now a PaperDocument too. PaperSummary
  // gained an optional `wht` field (→ "หัก ณ ที่จ่าย · WHT" row + "จ่ายสุทธิ /
  // Net Paid" total) and PaperSign gained an optional middle box, so the WHT
  // deduction the old comment said couldn't be modelled is now supported.
  // Seller block = our company; customer block = the vendor we pay. 3-box sign.
  | 'payment-voucher'
  // Sprint 13j-PURCH Flag-1 (BP-09) — on-screen READ-ONLY PaperDocument for the
  // Vendor Invoice detail page (Requirements §4.1 visual parity). The VENDOR
  // issued this tax invoice, so seller block = the vendor; customer block = our
  // company. No PrintMenu / no /pdf endpoint (§4.6); no ต้นฉบับ/สำเนา watermark
  // (it is a recorded inbound vendor doc, not one we issue copies of).
  | 'vendor-invoice';

interface DocPaperConfig {
  docType: string;
  docTypeEn: string;
  // `middle` is optional — only the Payment Voucher uses a 3-box signature strip.
  signRoles: { left: string; middle?: string; right: string };
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
  'purchase-order': { docType: 'ใบสั่งซื้อ', docTypeEn: 'PURCHASE ORDER', signRoles: { left: 'ผู้สั่งซื้อ', right: 'ผู้รับใบสั่งซื้อ' }, validUntilLabel: 'กำหนดส่งมอบ' },
  // Sprint 13j-PURCH D-supplement — Payment Voucher: 3-box sign (ผู้จัดทำ / ผู้อนุมัติ / ผู้รับเงิน).
  'payment-voucher': { docType: 'ใบสำคัญจ่าย', docTypeEn: 'PAYMENT VOUCHER', signRoles: { left: 'ผู้จัดทำ', middle: 'ผู้อนุมัติ', right: 'ผู้รับเงิน' } },
  // Sprint 13j-PURCH Flag-1 (BP-09) — Vendor Invoice (we are the buyer): the
  // vendor signs as ผู้ขาย, we sign as ผู้ซื้อ.
  'vendor-invoice': { docType: 'ใบกำกับภาษีซื้อ', docTypeEn: 'VENDOR INVOICE', signRoles: { left: 'ผู้ขาย', right: 'ผู้ซื้อ' } },
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
    case 'purchase-order':
      // Sprint 13j-PURCH D3 — an approved/sent/closed PO is the committed version.
      return ['Approved', 'Sent', 'Closed'].includes(status)
        ? { text: 'อนุมัติแล้ว', variant: 'success' }
        : undefined;
    case 'payment-voucher':
      // Sprint 13j-PURCH D-supplement — a posted PV is the committed payment.
      return status === 'Posted' ? { text: 'ต้นฉบับ', variant: 'success' } : undefined;
    case 'vendor-invoice':
      // Sprint 13j-PURCH Flag-1 — recorded inbound vendor doc: no positive
      // watermark (the CANCELLED override above still applies if voided).
      return undefined;
  }
}

// CompanyProfile → CUSTOMER block. Used by the Vendor Invoice paper (Flag-1),
// where the issuing vendor is the seller and our company sits in the customer
// slot. Mirrors companyToSeller's address join but maps to CustomerInfo's shape.
export function companyToCustomer(c: CompanyProfile | undefined): CustomerInfo {
  if (!c) return { name: '—' };
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
    taxId: c.taxId ? formatTaxId(c.taxId) : null,
    branchCode: c.branchCode || '00000',
    address: addr,
    phone: c.phone,
  };
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
