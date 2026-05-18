// Mirrors the backend JSON contract (camelCase). Source of truth = shipped API
// (Answer-Sana-Question-Backend2 Q2/Q3 "the contract you shipped IS the contract").

export type DocStatus = 'Draft' | 'Approved' | 'Posted' | 'Voided';

export interface TaxInvoiceListItem {
  taxInvoiceId: number;
  docNo: string | null;
  docDate: string;
  customerName: string;
  customerTaxId: string | null;
  totalAmount: number;
  taxAmount: number;
  status: DocStatus;
  paymentStatus: string;
  currencyCode: string;
}

export interface CursorPage<T> {
  items: T[];
  nextCursor: number | null;
  hasMore: boolean;
}

export interface TaxInvoiceDetailLine {
  lineNo: number;
  productCode: string | null;
  descriptionTh: string;
  quantity: number;
  uomText: string;
  unitPrice: number;
  discountAmount: number;
  lineAmount: number;
  taxCode: string | null;
  taxRate: number;
  taxAmount: number;
  totalAmount: number;
}

export interface TaxInvoiceDetail {
  taxInvoiceId: number;
  docNo: string | null;
  status: DocStatus;
  docDate: string;
  taxPointDate: string;
  supplierName: string;
  supplierTaxId: string;
  supplierBranchCode: string;
  supplierAddress: string;
  customerId: number;
  customerName: string;
  customerTaxId: string | null;
  customerBranchCode: string | null;
  customerAddress: string;
  customerVatRegistered: boolean;
  currencyCode: string;
  isTaxInclusive: boolean;
  subtotalAmount: number;
  discountAmount: number;
  taxableAmount: number;
  nonTaxableAmount: number;
  taxAmount: number;
  totalAmount: number;
  paymentStatus: string;
  dueDate: string | null;
  notes: string | null;
  postedAt: string | null;
  businessUnitId: number | null;
  businessUnitCode: string | null;
  lines: TaxInvoiceDetailLine[];
}

export interface NumberGapRow {
  series: string;
  missingSeqNo: number;
}
export interface NumberGapReport {
  year: number | null;
  month: number | null;
  docType: string | null;
  gaps: NumberGapRow[];
  hasGaps: boolean;
}

export interface TaxInvoicePostedResult {
  taxInvoiceId: number;
  docNo: string;
  postedAt: string;
  totalAmount: number;
  taxAmount: number;
}

export interface CreateTaxInvoiceLineInput {
  productId: number | null;
  productCode: string | null;
  descriptionTh: string;
  quantity: number;
  uomId: number;
  uomText: string;
  unitPrice: number;
  discountPercent: number;
  taxCodeId: number;
  taxCode: string;
  taxRate: number;
}
export interface CreateTaxInvoiceRequest {
  docDate: string;
  customerId: number;
  isTaxInclusive: boolean;
  currencyCode: string;
  exchangeRate: number;
  notes: string | null;
  paymentTerms: string | null;
  dueDate: string | null;
  lines: CreateTaxInvoiceLineInput[];
  businessUnitId?: number | null;   // Sprint 8
}

// ───────────────────────── Sprint 4: Receipt + CN/DN ─────────────────────────

export interface ReceiptListItem {
  receiptId: number; docNo: string | null; docDate: string;
  customerName: string; amount: number; status: DocStatus; currencyCode: string;
  whtAmount: number;
}
// Sprint 8.6 — AR-side WHT
export interface WhtTypeListItem {
  whtTypeId: number; code: string; nameTh: string; nameEn: string | null;
  rate: number; formType: string; incomeTypeCode: string;
  effectiveFrom: string; effectiveTo: string | null; isActive: boolean;
}
export interface WhtBaseSuggestion {
  appliedSubtotalExVat: number;
  suggestedWhtTypeId: number | null; suggestedWhtTypeCode: string | null;
  suggestedWhtRate: number; suggestedWhtBase: number;
  suggestedWhtAmount: number; explanation: string;
}
export interface WhtReceivableRegisterRow {
  docNo: string; docDate: string; customerName: string;
  customerTaxId: string | null; whtAmount: number; customerWhtCertNo: string | null;
}
export interface WhtReceivableRegister {
  fromDate: string; toDate: string;
  rows: WhtReceivableRegisterRow[]; totalWht: number;
}
export interface WhtReceivableAgingRow {
  customerName: string; customerTaxId: string | null; docNo: string;
  docDate: string; whtAmount: number; ageDays: number;
  certReceived: boolean; reconciled: boolean;
}
export interface WhtReceivableAgingBuckets {
  current: number; days30: number; days60: number; days90Plus: number;
}
export interface WhtReceivableAging {
  rows: WhtReceivableAgingRow[]; totalOutstanding: number;
  buckets: WhtReceivableAgingBuckets;
}
// Sprint 9 Part A — financial reports
export interface TrialBalanceReportRow {
  accountCode: string; accountNameTh: string; accountType: string;
  normalBalance: string; debit: number; credit: number; net: number;
}
export interface TrialBalanceReport {
  asOfDate: string; companyId: number;
  rows: TrialBalanceReportRow[];
  totals: { debit: number; credit: number; balanced: boolean };
}
export interface ProfitLossGroup {
  businessUnitId: number | null; businessUnitCode: string | null;
  groupName: string; revenue: number; expense: number; netProfit: number;
}
export interface ProfitLossReport {
  from: string; to: string;
  groups: ProfitLossGroup[]; totals: ProfitLossGroup; note: string;
}
export interface SalesSummaryRow {
  dimension: string; label: string; docCount: number;
  subtotal: number; vat: number; total: number;
}
export interface SalesSummary {
  from: string; to: string; groupBy: string;
  rows: SalesSummaryRow[]; totals: SalesSummaryRow;
}
// Sprint 9 Part B — VAT compliance / ภ.พ.30
export interface MonthlyClaimRatio {
  yearMonth: number; taxableSales: number; exemptSales: number;
  totalSales: number; claimRatio: number; applicableTo: string;
}
export interface Pnd30LineAmount { amount: number; vat: number; }
export interface Pnd30Apportionment {
  sharedInputVat: number; claimRatio: number; claimableAmount: number;
}
export interface Pnd30Lines {
  salesTaxable: Pnd30LineAmount; salesZeroRated: Pnd30LineAmount;
  salesExempt: Pnd30LineAmount; totalSales: number; outputVatTotal: number;
  purchaseTaxable: Pnd30LineAmount;
  purchaseProportionalApportionment: Pnd30Apportionment;
  inputVatTotal: number; netVatPayable: number; creditCarryForward: number;
}
export interface TaxFilingCompany {
  taxId: string; nameTh: string; nameEn: string | null; branchCode: string;
}
export interface Pnd30Filing {
  period: number; company: TaxFilingCompany; filingDueDate: string;
  submissionMode: string; lines: Pnd30Lines; warnings: string[]; status: string;
}
export interface InputVatRegisterRow {
  docDate: string; docNo: string; vendorName: string; vendorTaxId: string | null;
  taxablePurchaseSubtotal: number; exemptPurchaseSubtotal: number;
  recoverableVat: number; nonRecoverableVat: number;
  proportionalClaimAmount: number; totalPaid: number;
}
export interface InputVatRegister {
  period: number; rows: InputVatRegisterRow[];
  taxableTotal: number; exemptTotal: number; recoverableVatTotal: number;
}
export interface OutputVatRegisterRow {
  docDate: string; docNo: string; docType: string; customerName: string;
  customerTaxId: string | null; subtotal: number; vat: number;
  total: number; category: string;
}
export interface OutputVatRegister {
  period: number; rows: OutputVatRegisterRow[];
  subtotalTotal: number; vatTotal: number;
}
// Sprint 9 Part C — WHT filings (ภ.ง.ด.3/53/54) + ภ.พ.36
export interface WhtFilingRow {
  certNo: string; payeeName: string; payeeTaxId: string | null;
  incomeTypeCode: string; incomeAmount: number; whtRate: number; whtAmount: number;
}
export interface WhtFiling {
  period: number; formType: string; filingDueDate: string;
  submissionMode: string; rows: WhtFilingRow[];
  totals: { income: number; wht: number }; status: string;
}
export interface Pnd36Row {
  vendorName: string; vendorCountry: string | null; refDoc: string;
  serviceAmountThb: number; vatRate: number; vatAmount: number;
}
export interface Pnd36Filing {
  period: number; filingDueDate: string; submissionMode: string;
  rows: Pnd36Row[]; totalService: number; totalVat: number;
  reverseChargeJournalId: number | null; status: string;
}
export interface TaxFilingHistoryRow {
  filingId: number; formType: string; period: number; status: string;
  finalizedAt: string | null; submissionMode: string | null; rdAckRef: string | null;
}
export interface ReceiptAppliedTo {
  taxInvoiceId: number; tiDocNo: string | null; appliedAmount: number;
  businessUnitCode: string | null;
}
export interface ReceiptDetail {
  receiptId: number; docNo: string | null; status: DocStatus; docDate: string;
  customerName: string; customerTaxId: string | null; paymentMethod: string;
  chequeNo: string | null; amount: number; currencyCode: string; notes: string | null;
  postedAt: string | null; appliedTo: ReceiptAppliedTo[];
  businessUnitCode: string | null;
  // Sprint 8.6 — AR-side WHT (0/null when none).
  whtAmount: number; whtTypeCode: string | null; whtRate: number;
  whtBase: number; cashReceived: number;
  customerWhtCertNo: string | null; customerWhtCertDate: string | null;
}

export type AdjustmentNoteType = 'Credit' | 'Debit';
export interface AdjustmentNoteListItem {
  noteId: number; docNo: string | null; noteType: AdjustmentNoteType; docDate: string;
  customerName: string; totalAmount: number; taxAmount: number; status: DocStatus;
  currencyCode: string; originalTaxInvoiceId: number;
}
export interface AdjustmentNoteDetail {
  noteId: number; docNo: string | null; noteType: AdjustmentNoteType; status: DocStatus;
  docDate: string; originalTaxInvoiceId: number; originalTiDocNo: string | null;
  reasonCode: string | null; reason: string; customerName: string;
  customerTaxId: string | null; customerAddress: string; currencyCode: string;
  subtotalAmount: number; taxRate: number; taxAmount: number; totalAmount: number;
  notes: string | null; postedAt: string | null;
  businessUnitCode: string | null;
}

export const CREDIT_NOTE_REASONS = ['Typo','AmountError','CustomerInfo','Return','PriceReduce','Cancel'] as const;
export const DEBIT_NOTE_REASONS = ['PriceIncrease','AdditionalCharge','ScopeExpansion','Typo'] as const;

// ───────────────────────── Sprint 5: Purchase (AP) ─────────────────────────
// Read-only surface + vendor master. PV create/approve UI deferred pending
// Question-Backend5 (B1 VendorInvoice / B2 SoD-approve).

export type VendorType = 'Individual' | 'Corporate';

export interface VendorListItem {
  vendorId: number; vendorCode: string; vendorType: VendorType;
  nameTh: string; taxId: string | null; vatRegistered: boolean; isActive: boolean;
}
export interface VendorDetail extends VendorListItem {
  nameEn: string | null; branchCode: string | null; branchName: string | null;
  address: string | null; contactPerson: string | null; phone: string | null;
  email: string | null; paymentTermDays: number; defaultCurrency: string;
  defaultWhtTypeCode: string | null;
  isForeign: boolean; hasThaiVatDReg: boolean; countryCode: string | null;
}
export interface CreateVendorRequest {
  vendorCode: string; vendorType: VendorType; nameTh: string; nameEn: string | null;
  taxId: string | null; branchCode: string | null; branchName: string | null;
  vatRegistered: boolean; address: string | null; contactPerson: string | null;
  phone: string | null; email: string | null; paymentTermDays: number;
  defaultCurrency: string; defaultWhtTypeCode: string | null;
  isForeign?: boolean; hasThaiVatDReg?: boolean; countryCode?: string | null;
}

export interface ExpenseCategoryLite {
  categoryId: number; categoryCode: string; nameTh: string;
  isRecoverableVat: boolean; isCapex: boolean;
}

export interface PaymentVoucherListItem {
  paymentVoucherId: number; docNo: string | null; docDate: string;
  vendorName: string; vendorTaxId: string | null; subPrefix: string;
  totalPaid: number; whtAmount: number; status: DocStatus; currencyCode: string;
}
export interface PaymentVoucherLineView {
  lineNo: number; expenseAccountId: number; description: string; amount: number;
  vatRate: number; vatAmount: number; isRecoverableVat: boolean;
  whtTypeId: number | null; whtRate: number; whtAmount: number;
}
export interface PaymentVoucherDetail {
  paymentVoucherId: number; docNo: string | null; status: DocStatus; docDate: string;
  vendorId: number; vendorName: string; vendorTaxId: string | null;
  vendorBranchCode: string | null; vendorAddress: string | null;
  expenseCategoryId: number; subPrefix: string; paymentMethod: string;
  chequeNo: string | null; chequeDate: string | null; bankAccountId: number | null;
  currencyCode: string; exchangeRate: number; subtotalAmount: number;
  vatAmount: number; whtAmount: number; totalPaid: number; totalAmountThb: number;
  description: string | null; notes: string | null;
  vendorInvoiceId: number | null;
  approvedBy: number | null; approvedAt: string | null;
  postedAt: string | null;
  selfWithholdMode: boolean; requiresPnd36ReverseCharge: boolean;
  lines: PaymentVoucherLineView[];
}
export interface PaymentVoucherApprovedResult {
  paymentVoucherId: number; approvedBy: number; approvedAt: string;
}

// ───────────────────────── Sprint 6: VendorInvoice + settle ─────────────────
export interface VendorInvoiceListItem {
  vendorInvoiceId: number; docNo: string | null; docDate: string;
  vendorName: string; vendorTaxId: string | null; vendorTaxInvoiceNo: string;
  vatClaimPeriod: number; totalAmount: number; vatAmount: number;
  settledAmount: number; settlementStatus: string; status: DocStatus;
  currencyCode: string;
}
export interface VendorInvoiceLineView {
  lineNo: number; expenseCategoryId: number; expenseAccountId: number;
  description: string; amount: number; vatRate: number; vatAmount: number;
  isRecoverableVat: boolean; isCapex: boolean; isCogs: boolean;
}
export interface VendorInvoiceDetail {
  vendorInvoiceId: number; docNo: string | null; status: DocStatus; docDate: string;
  vendorTaxInvoiceNo: string; vendorTaxInvoiceDate: string; vatClaimPeriod: number;
  vendorId: number; vendorName: string; vendorTaxId: string | null;
  vendorBranchCode: string | null; vendorAddress: string | null;
  currencyCode: string; exchangeRate: number; subtotalAmount: number;
  vatAmount: number; nonRecoverableVatAmount: number; totalAmount: number;
  settledAmount: number; settlementStatus: string; notes: string | null;
  postedAt: string | null;
  purchaseOrderId: number | null; purchaseOrderDocNo: string | null;
  lines: VendorInvoiceLineView[];
}
export interface CreateVendorInvoiceLineInput {
  expenseCategoryId: number; expenseAccountId: number | null;
  description: string; amount: number; vatRate: number;
}
export interface CreateVendorInvoiceRequest {
  docDate: string; vendorId: number;
  vendorTaxInvoiceNo: string; vendorTaxInvoiceDate: string;
  vatClaimPeriod: number | null; currencyCode: string; exchangeRate: number;
  notes: string | null; lines: CreateVendorInvoiceLineInput[];
  hasInputVat?: boolean;
  purchaseOrderId?: number | null;   // Sprint 12 — optional PO link
}
export interface VendorInvoicePostedResult {
  vendorInvoiceId: number; docNo: string; postedAt: string;
  totalAmount: number; vatAmount: number; vatClaimPeriod: number;
  poOverReceiptWarning?: string | null;   // Sprint 12 — >105% chip (HTTP 200)
}

// ───────────────────────── Sprint 8: Business Units ─────────────────────────
export interface BusinessUnitListItem {
  businessUnitId: number; code: string; nameTh: string;
  nameEn: string | null; isActive: boolean;
}
export interface BusinessUnitDetail extends BusinessUnitListItem {
  defaultRevenueAccountId: number | null;
}
export interface CreateBusinessUnitRequest {
  code: string; nameTh: string; nameEn: string | null;
  defaultRevenueAccountId: number | null;
}
export interface UpdateBusinessUnitRequest {
  nameTh: string; nameEn: string | null;
  defaultRevenueAccountId: number | null; isActive: boolean;
}
export interface CompanyBuSetting { requiresBusinessUnit: boolean; }

export interface WhtCertificateListItem {
  whtCertificateId: number; docNo: string; certDate: string; paymentVoucherId: number | null;
  payeeName: string; payeeTaxId: string | null; incomeTypeCode: string;
  incomeAmount: number; whtAmount: number; formType: string; status: DocStatus;
}
export interface WhtCertificateDetail {
  whtCertificateId: number; docNo: string; certDate: string; paymentVoucherId: number | null;
  formType: string; payerName: string; payerTaxId: string; payerBranchCode: string;
  payerAddress: string; payeeName: string; payeeTaxId: string | null;
  payeeAddress: string; payeeType: string; incomeTypeCode: string;
  incomeDescription: string | null; incomeAmount: number; whtRate: number;
  whtAmount: number; status: DocStatus; issuedAt: string;
}
// Sprint 10 Part A — Product master
export type ProductTypeStr = 'GOOD' | 'SERVICE' | 'EXEMPT_GOOD' | 'EXEMPT_SERVICE';
export interface ProductListItem {
  productId: number; productCode: string; nameTh: string; nameEn: string | null;
  productType: ProductTypeStr; defaultUnitPrice: number | null; isActive: boolean;
}
export interface ProductDetail {
  productId: number; productCode: string; nameTh: string; nameEn: string | null;
  productType: ProductTypeStr; defaultUomText: string | null;
  defaultUnitPrice: number | null; defaultOutputTaxCodeId: number | null;
  defaultInputTaxCodeId: number | null; defaultWhtTypeId: number | null;
  descriptionTh: string | null; notes: string | null; isActive: boolean;
}
// Sprint 10 Part B — Q→SO→DO chain
export interface ChainLineDto {
  lineNo: number; productId: number | null; productCode: string | null;
  descriptionTh: string; quantity: number; uomText: string;
  unitPrice: number; lineAmount: number; taxAmount: number; totalAmount: number;
}
export interface QuotationListItem {
  quotationId: number; docNo: string | null; status: string; docDate: string;
  validUntilDate: string; customerName: string; totalAmount: number;
  convertedToSoId: number | null;
}
export interface QuotationDetail {
  quotationId: number; docNo: string | null; status: string; docDate: string;
  validUntilDate: string; customerId: number; customerName: string;
  businessUnitId: number | null; currencyCode: string; subtotalAmount: number;
  vatAmount: number; totalAmount: number; showWhtNote: boolean;
  convertedToSoId: number | null; notes: string | null; lines: ChainLineDto[];
}
export interface SalesOrderListItem {
  salesOrderId: number; docNo: string | null; status: string; docDate: string;
  customerName: string; totalAmount: number; quotationId: number | null;
}
export interface SalesOrderDetail {
  salesOrderId: number; docNo: string | null; status: string; docDate: string;
  customerId: number; customerName: string; businessUnitId: number | null;
  subtotalAmount: number; vatAmount: number; totalAmount: number;
  quotationId: number | null; lines: ChainLineDto[];
}
export interface DeliveryOrderListItem {
  deliveryOrderId: number; docNo: string | null; status: string; docDate: string;
  customerName: string; isCombinedWithTi: boolean;
  taxInvoiceId: number | null; salesOrderId: number | null;
}
export interface DeliveryOrderDetail {
  deliveryOrderId: number; docNo: string | null; status: string; docDate: string;
  customerId: number; customerName: string; businessUnitId: number | null;
  isCombinedWithTi: boolean; taxInvoiceId: number | null;
  salesOrderId: number | null; subtotalAmount: number; vatAmount: number;
  totalAmount: number; lines: ChainLineDto[];
}
// Sprint 11 — file attachments (polymorphic)
export interface AttachmentItem {
  attachmentId: number; category: string; fileName: string; mimeType: string;
  sizeBytes: number; uploadedAt: string; uploadedById: number;
  uploadedByName: string; description: string | null; pageCount: number | null;
}
// Sprint 12 — internal Purchase Order
export interface PoLineDto {
  lineNo: number; productId: number | null; productCode: string | null;
  descriptionTh: string; quantity: number; uomText: string | null;
  unitPrice: number; lineAmount: number; taxAmount: number; totalAmount: number;
}
export interface PurchaseOrderListItem {
  purchaseOrderId: number; docNo: string | null; status: string;
  docDate: string; expectedDeliveryDate: string | null; vendorName: string;
  totalAmount: number; businessUnitId: number | null;
}
export interface LinkedViDto { vendorInvoiceId: number; docNo: string | null; totalAmount: number; }
export interface PurchaseOrderDetail {
  purchaseOrderId: number; docNo: string | null; status: string;
  docDate: string; expectedDeliveryDate: string | null; vendorId: number;
  vendorName: string; businessUnitId: number | null; currencyCode: string;
  subtotalAmount: number; vatAmount: number; totalAmount: number;
  notes: string | null; internalNotes: string | null;
  approvedAt: string | null; approvedBy: number | null;
  sentToVendorAt: string | null; closedAt: string | null;
  cancellationReason: string | null;
  linkedViTotal: number; remaining: number;
  lines: PoLineDto[]; linkedVis: LinkedViDto[];
}
export interface OutstandingPoRow {
  poId: number; docNo: string | null; vendorName: string;
  expectedDeliveryDate: string | null; daysOverdue: number;
  agingBucket: string; poTotal: number; linkedViCount: number;
  linkedViTotal: number; remaining: number;
}
export interface OutstandingPoReport { asOf: string; rows: OutstandingPoRow[]; }

// ───────────────────────── Sprint 14: External API keys ────────────────────
export interface ApiKeyListItem {
  apiKeyId: number; name: string; keyPrefix: string; scopes: string[];
  defaultBusinessUnitId: number | null; defaultBusinessUnitCode: string | null;
  createdAt: string; lastUsedAt: string | null; expiresAt: string | null;
  revokedAt: string | null; isActive: boolean;
}
export interface CreateApiKeyRequest {
  name: string; scopes: string[];
  expiresAt: string | null; defaultBusinessUnitId: number | null;
}
export interface ApiKeyCreatedResult {
  apiKeyId: number; name: string; keyPrefix: string; plaintext: string;
}
