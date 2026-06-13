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
  customerId: number;
  businessUnitId: number | null;
}

export interface CursorPage<T> {
  items: T[];
  nextCursor: number | null;
  hasMore: boolean;
}

// Sprint 13d P6 — Company Profile (hybrid lock). Hard fields read-only in UI.
export interface CompanyProfile {
  companyId: number;
  legalName: string;
  taxId: string;
  registrationNumber: string | null;
  registeredAddressLine1: string;
  registeredAddressLine2: string | null;
  regBuilding: string | null;
  regRoomNo: string | null;
  regFloor: string | null;
  regVillage: string | null;
  regHouseNo: string | null;
  regMoo: string | null;
  regSoi: string | null;
  regStreet: string | null;
  registeredSubdistrict: string | null;
  registeredDistrict: string | null;
  registeredProvince: string;
  registeredPostalCode: string;
  vatRegistrationDate: string | null;
  branchCode: string;
  tradeName: string | null;
  logoUrl: string | null;
  phone: string | null;
  email: string | null;
  website: string | null;
  contactName: string | null;
  bankName: string | null;
  bankAccountNo: string | null;
  bankAccountName: string | null;
  ssoEmployerAccountNo: string | null;
}

export interface UpdateRegisteredAddressRequest {
  building: string | null; roomNo: string | null; floor: string | null; village: string | null;
  houseNo: string | null; moo: string | null; soi: string | null; street: string | null;
  subdistrict: string | null; district: string | null; province: string; postalCode: string;
}

// Sprint 13d P3 — current user's effective scopes (drives PermissionGate).
export interface MePermissions {
  permissions: string[];
  roles: string[];
  isSuperAdmin: boolean;
}

export interface UpdateCompanyProfileSoftRequest {
  tradeName: string | null;
  logoUrl: string | null;
  phone: string | null;
  email: string | null;
  website: string | null;
  contactName: string | null;
  bankName: string | null;
  bankAccountNo: string | null;
  bankAccountName: string | null;
  ssoEmployerAccountNo: string | null;
}

// Legal form of the company — mirrors BE Accounting.Domain.Enums.LegalEntityType
// (JsonStringEnumConverter → PascalCase strings on the wire).
export type LegalEntityType =
  | 'LimitedCompany'        // บริษัทจำกัด
  | 'PublicLimitedCompany'  // บริษัทมหาชน
  | 'LimitedPartnership'    // ห้างหุ้นส่วนจำกัด
  | 'OrdinaryPartnership'   // ห้างหุ้นส่วนสามัญ
  | 'JointVenture'          // กิจการร่วมค้า
  | 'SoleProprietor'        // เจ้าของคนเดียว
  | 'Other';                // นิติบุคคลอื่น

// §4.5 — ภ.พ.30 filing mode is per-company config, never a casual UI toggle.
export type Pnd30SubmissionMode = 'manual' | 'auto';

// Phase C-C — master `companies` row (super-admin GET /companies). Carries
// PaidUpCapital for the CIT SME test (ทุน ≤ 5 ล้าน ∧ รายได้ ≤ 30 ล้าน).
// Per-company VAT mode: vatRate is a FRACTION (0.07), the UI shows percent.
export interface CompanyListItem {
  companyId: number;
  taxId: string;
  nameTh: string;
  nameEn: string | null;
  legalEntityType: LegalEntityType;
  vatRegistered: boolean;
  baseCurrency: string;
  isActive: boolean;
  paidUpCapital: number | null;
  vatRate: number;
  pnd30SubmissionMode: Pnd30SubmissionMode;
}
/** Back-compat alias (PaidUpCapitalCard et al. predate /settings/companies). */
export type CompanyDto = CompanyListItem;

// GET /companies/{id} — full row for the super-admin edit form (PUT replaces
// every updatable field, so the FE must prefill all of them first).
export interface CompanyDetail extends CompanyListItem {
  registrationDate: string | null;
  vatRegisterDate: string | null;
  fiscalYearStartMonth: number;
  addressTh: string | null;
  subDistrict: string | null;
  district: string | null;
  province: string | null;
  postalCode: string | null;
  phone: string | null;
  email: string | null;
}

// POST /companies (super-admin) — 201, no body.
export interface CreateCompanyRequest {
  taxId: string;
  nameTh: string;
  nameEn: string | null;
  legalEntityType: LegalEntityType;
  registrationDate: string | null;
  vatRegistered: boolean;
  vatRegisterDate: string | null;
  fiscalYearStartMonth: number;
  addressTh: string | null;
  subDistrict: string | null;
  district: string | null;
  province: string | null;
  postalCode: string | null;
  phone: string | null;
  email: string | null;
  paidUpCapital: number | null;
  vatRate: number;
  pnd30SubmissionMode: Pnd30SubmissionMode;
}

// Full-overwrite body of PUT /companies/{id} — every field must be supplied
// (the backend assigns all of them unconditionally; omitting one nulls it,
// and omitting vatRate/pnd30SubmissionMode silently resets them to defaults).
export interface UpdateCompanyRequest {
  nameTh: string;
  nameEn: string | null;
  vatRegistered: boolean;
  vatRegisterDate: string | null;
  addressTh: string | null;
  subDistrict: string | null;
  district: string | null;
  province: string | null;
  postalCode: string | null;
  phone: string | null;
  email: string | null;
  isActive: boolean;
  paidUpCapital: number | null;
  vatRate: number;
  pnd30SubmissionMode: Pnd30SubmissionMode;
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
  quotationId?: number | null;   // Sprint 13h P6.1 — Q cross-ref
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
  quotationId?: number | null;      // Sprint 13h P6.1 — optional Q origin
}

// ───────────────────────── Sprint 4: Receipt + CN/DN ─────────────────────────

export interface ReceiptListItem {
  receiptId: number; docNo: string | null; docDate: string;
  customerName: string; amount: number; status: DocStatus; currencyCode: string;
  whtAmount: number;
  customerId: number; businessUnitId: number | null;
}
// Sprint 8.6 — AR-side WHT
export interface WhtTypeListItem {
  whtTypeId: number; code: string; nameTh: string; nameEn: string | null;
  rate: number; formType: string; incomeTypeCode: string;
  effectiveFrom: string; effectiveTo: string | null; isActive: boolean;
}
// Sprint (multi-category WHT, 2026-05-22) — one suggested withholding category.
export interface WhtCategorySuggestion {
  whtTypeId: number; code: string; nameTh: string;
  rate: number; base: number; amount: number;
}
// Sprint (per-line WHT, 2026-05-22) — one applied TI line for per-line classification.
export interface WhtSuggestLine {
  tiDocNo: string | null; description: string; productType: string;
  lineAmount: number; suggestedWhtTypeId: number | null;
  suggestedCode: string | null; suggestedRate: number;
}
export interface WhtBaseSuggestion {
  appliedSubtotalExVat: number;
  suggestedWhtTypeId: number | null; suggestedWhtTypeCode: string | null;
  suggestedWhtRate: number; suggestedWhtBase: number;
  suggestedWhtAmount: number; explanation: string;
  serviceSubtotal: number; goodsSubtotal: number;
  categories: WhtCategorySuggestion[] | null;
  lines: WhtSuggestLine[] | null;
}
// Sprint (multi-category WHT) — receipt WHT input line (rate/amount computed server-side).
export interface ReceiptWhtLineInput { whtTypeId: number; baseAmount: number; }
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
// Sprint 13j-tail — receipts missing the customer 50ทวิ cert no
export interface WhtMissingCertRow {
  receiptId: number; docNo: string; docDate: string;
  customerName: string; customerTaxId: string | null; whtAmount: number;
}
export interface WhtMissingCertReport {
  period: number; rows: WhtMissingCertRow[]; totalWht: number;
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
// 2026-06-13 — monthly tax summary dashboard
export interface TaxSummaryMonth {
  month: number;            // 1..12; 0 = year total
  revenue: number; expense: number; netProfit: number;
  outputVat: number; inputVat: number; vatPayable: number; vatRefundable: number;
  whtPaidPnd3: number; whtPaidPnd53: number; whtPaidPnd54: number; whtPaidPnd1: number;
  whtPaidTotal: number; whtReceived: number;
}
export interface TaxSummaryReport {
  year: number; months: TaxSummaryMonth[]; totals: TaxSummaryMonth;
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
// Sprint (receipt itemize, 2026-05-22) — goods/service line derived from applied TIs.
export interface ReceiptLineView {
  descriptionTh: string; productType: string; quantity: number; uomText: string;
  unitPrice: number; lineAmount: number; tiDocNo: string | null;
}
// Sprint (multi-category WHT) — one income-type WHT slice on the receipt.
export interface ReceiptWhtLineView {
  whtTypeId: number; whtTypeCode: string; incomeTypeCode: string | null;
  whtRate: number; baseAmount: number; whtAmount: number;
}
export interface ReceiptDetail {
  receiptId: number; docNo: string | null; status: DocStatus; docDate: string;
  customerName: string; customerTaxId: string | null; paymentMethod: string;
  chequeNo: string | null; amount: number; currencyCode: string; notes: string | null;
  postedAt: string | null; appliedTo: ReceiptAppliedTo[];
  businessUnitCode: string | null;
  // Sprint 8.6 — AR-side WHT aggregate (0/null when none; prefer whtLines for breakdown).
  whtAmount: number; whtTypeCode: string | null; whtRate: number;
  whtBase: number; cashReceived: number;
  customerWhtCertNo: string | null; customerWhtCertDate: string | null;
  // Sprint (receipt itemize + multi-category WHT, 2026-05-22).
  lines: ReceiptLineView[] | null;
  whtLines: ReceiptWhtLineView[] | null;
  // cont.70 — buyer billing address + branch for the header block.
  customerAddress: string | null;
  customerBranchCode: string | null;
}

export type AdjustmentNoteType = 'Credit' | 'Debit';
export interface AdjustmentNoteListItem {
  noteId: number; docNo: string | null; noteType: AdjustmentNoteType; docDate: string;
  customerName: string; totalAmount: number; taxAmount: number; status: DocStatus;
  currencyCode: string; originalTaxInvoiceId: number;
  customerId: number; businessUnitId: number | null;
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
  // ITEM 8 — vendor remittance details (all nullable; swiftCode = non-Thai banking).
  bankName: string | null; bankAccountNo: string | null;
  bankAccountName: string | null; swiftCode: string | null;
}
export interface CreateVendorRequest {
  vendorCode: string; vendorType: VendorType; nameTh: string; nameEn: string | null;
  taxId: string | null; branchCode: string | null; branchName: string | null;
  vatRegistered: boolean; address: string | null; contactPerson: string | null;
  phone: string | null; email: string | null; paymentTermDays: number;
  defaultCurrency: string; defaultWhtTypeCode: string | null;
  isForeign?: boolean; hasThaiVatDReg?: boolean; countryCode?: string | null;
  // ITEM 8 — vendor remittance details (all nullable; swiftCode = non-Thai banking).
  bankName?: string | null; bankAccountNo?: string | null;
  bankAccountName?: string | null; swiftCode?: string | null;
}
// Mirrors Application.Master.UpdateVendorRequest (PUT /vendors/{id}). vendorCode +
// vendorType are immutable on update, so they are NOT part of this contract.
export interface UpdateVendorRequest {
  nameTh: string; nameEn: string | null;
  taxId: string | null; branchCode: string | null; branchName: string | null;
  vatRegistered: boolean; address: string | null; contactPerson: string | null;
  phone: string | null; email: string | null; paymentTermDays: number;
  defaultCurrency: string; defaultWhtTypeCode: string | null; isActive: boolean;
  isForeign?: boolean; hasThaiVatDReg?: boolean; countryCode?: string | null;
  bankName?: string | null; bankAccountNo?: string | null;
  bankAccountName?: string | null; swiftCode?: string | null;
}

// BP-02 — mirror the BE ExpenseCategoryDto JSON exactly. The list endpoint emits
// `defaultIsRecoverableVat` (NOT `isRecoverableVat`); the old field name read
// undefined → the table rendered "—" for every row.
export interface ExpenseCategoryLite {
  categoryId: number; categoryCode: string; nameTh: string; nameEn?: string | null;
  defaultIsRecoverableVat: boolean; isCapex: boolean; isCogs?: boolean; isActive?: boolean;
}

// Purchase completeness — advisory, POSTED docs only (purchase-completeness spec D2).
export type CompletenessMissingCode =
  | 'MISSING_VI' | 'MISSING_WHT_CERT' | 'MISSING_RECEIPT_FILE' | 'MISSING_TAX_INVOICE_FILE';
export interface Completeness {
  isComplete: boolean;
  missing: CompletenessMissingCode[];
}
export interface PaymentVoucherListItem {
  paymentVoucherId: number; docNo: string | null; docDate: string;
  vendorName: string; vendorTaxId: string | null; subPrefix: string;
  totalPaid: number; whtAmount: number; status: DocStatus; currencyCode: string;
  isComplete: boolean;
  businessUnitId: number | null;   // Sprint BU-PURCH
}
export interface PaymentVoucherLineView {
  lineNo: number; expenseAccountId: number; description: string; amount: number;
  vatRate: number; vatAmount: number; isRecoverableVat: boolean;
  whtTypeId: number | null; whtRate: number; whtAmount: number;
  productType: ProductTypeStr | null;
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
  /** wht-grossup (2026-06-12): DEDUCT | GROSS_UP_FOREVER | GROSS_UP_ONCE. */
  whtPayerMode?: 'DEDUCT' | 'GROSS_UP_FOREVER' | 'GROSS_UP_ONCE';
  businessUnitId: number | null;        // Sprint BU-PURCH
  businessUnitCode: string | null;
  businessUnitName: string | null;
  lines: PaymentVoucherLineView[];
  // Sprint 13j-PURCH Flag-2 — downward chain ref: WHT cert(s) issued from this PV.
  whtCertificates: PaymentVoucherWhtCertificateRef[];
  // purchase-completeness — advisory, populated for POSTED PVs only.
  completeness?: Completeness;
}
export interface PaymentVoucherWhtCertificateRef {
  whtCertificateId: number; docNo: string; status: DocStatus;
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
  isComplete: boolean;
  businessUnitId: number | null;   // Sprint BU-PURCH
}
export interface VendorInvoiceLineView {
  lineNo: number; expenseCategoryId: number; expenseAccountId: number;
  description: string; amount: number; vatRate: number; vatAmount: number;
  isRecoverableVat: boolean; isCapex: boolean; isCogs: boolean;
  productType: ProductTypeStr | null;
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
  businessUnitId: number | null;        // Sprint BU-PURCH
  businessUnitCode: string | null;
  businessUnitName: string | null;
  lines: VendorInvoiceLineView[];
  // Sprint 13j-PURCH Flag-2 — downward chain ref: PV(s) settling this VI.
  settlingPvs: VendorInvoiceSettlingPvRef[];
  // purchase-completeness — advisory, populated for POSTED VIs only.
  completeness?: Completeness;
}
export interface VendorInvoiceSettlingPvRef {
  paymentVoucherId: number; docNo: string | null; status: DocStatus;
}
export interface CreateVendorInvoiceLineInput {
  expenseCategoryId: number; expenseAccountId: number | null;
  description: string; amount: number; vatRate: number;
  productType?: ProductTypeStr;
}
// purchase-completeness Phase 2 — PV→VI guided create (pre-fills VI draft from PV).
export interface CreateViFromPvRequest {
  vendorTaxInvoiceNo: string;
  vendorTaxInvoiceDate: string; // YYYY-MM-DD
  vatClaimPeriod?: number | null;
  hasInputVat?: boolean;
}
export interface CreateVendorInvoiceRequest {
  docDate: string; vendorId: number;
  vendorTaxInvoiceNo: string; vendorTaxInvoiceDate: string;
  vatClaimPeriod: number | null; currencyCode: string; exchangeRate: number;
  notes: string | null; lines: CreateVendorInvoiceLineInput[];
  hasInputVat?: boolean;
  purchaseOrderId?: number | null;   // Sprint 12 — optional PO link
  businessUnitId?: number | null;    // Sprint BU-PURCH
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

// ───────────────────────── Payroll P-A: Employees ─────────────────────────
export interface EmployeeAddress {
  addressNo: string | null; moo: string | null; soi: string | null; street: string | null;
  subDistrict: string | null; district: string | null; province: string | null; postalCode: string | null;
}
export interface EmployeeListItem {
  employeeId: number; employeeCode: string; fullNameTh: string;
  nationalId: string; baseSalary: number; ssoApplicable: boolean; isActive: boolean;
}
export interface EmployeeDetail {
  employeeId: number; employeeCode: string;
  titleTh: string | null; firstNameTh: string; lastNameTh: string;
  titleEn: string | null; firstNameEn: string | null; lastNameEn: string | null;
  nationalId: string; taxId: string | null;
  address: EmployeeAddress;
  hireDate: string; terminationDate: string | null;
  baseSalary: number;
  bankName: string | null; bankAccountNo: string | null; bankAccountName: string | null;
  ssoApplicable: boolean; ssoNumber: string | null;
  maritalStatus: string; spouseHasIncome: boolean; childrenCount: number;
  isActive: boolean;
}
export interface CreateEmployeeRequest {
  employeeCode: string;
  titleTh: string | null; firstNameTh: string; lastNameTh: string;
  titleEn: string | null; firstNameEn: string | null; lastNameEn: string | null;
  nationalId: string; taxId: string | null;
  address: EmployeeAddress | null;
  hireDate: string; terminationDate: string | null;
  baseSalary: number;
  bankName: string | null; bankAccountNo: string | null; bankAccountName: string | null;
  ssoApplicable: boolean; ssoNumber: string | null;
  maritalStatus: string; spouseHasIncome: boolean; childrenCount: number;
}
export type UpdateEmployeeRequest = Omit<CreateEmployeeRequest, 'employeeCode'> & { isActive: boolean };

// ───────────────────────── Payroll P-C/P-D: Runs + Payslips ─────────────────────────
export interface PayslipDto {
  payslipId: number; employeeId: number; employeeCode: string; employeeName: string; nationalId: string;
  grossTaxable: number; grossNonTaxable: number; pitWithheld: number;
  ssoEmployee: number; ssoEmployer: number; otherDeductions: number; netPay: number;
  ytdIncome: number; ytdPit: number;
}
export interface PayrollRunListItem {
  payrollRunId: number; periodYearMonth: string; payDate: string; status: string; docNo: string | null;
  employeeCount: number; totalNet: number; isPaid: boolean;
}
export interface PayrollRunDetail {
  payrollRunId: number; periodYearMonth: string; payDate: string; status: string; docNo: string | null;
  totalGrossTaxable: number; totalGrossNonTaxable: number; totalPit: number;
  totalSsoEmployee: number; totalSsoEmployer: number; totalOtherDeductions: number; totalNet: number;
  journalId: number | null;
  approvedAt: string | null; postedAt: string | null; paidAt: string | null;
  notes: string | null;
  payslips: PayslipDto[];
}
export interface CreatePayrollRunRequest { periodYearMonth: string; payDate: string; notes: string | null; }

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
  /** wht-grossup (2026-06-12): 50ทวิ เงื่อนไข — 1 หัก ณ ที่จ่าย · 2 ออกให้ตลอดไป · 3 ออกให้ครั้งเดียว. */
  whtCondition?: 1 | 2 | 3;
}
// Sprint 10 Part A — Product master
export type ProductTypeStr = 'GOOD' | 'SERVICE' | 'EXEMPT_GOOD' | 'EXEMPT_SERVICE';
export interface ProductListItem {
  productId: number; productCode: string; nameTh: string; nameEn: string | null;
  productType: ProductTypeStr; defaultUnitPrice: number | null; isActive: boolean;
  // cont.81 — purchase/sale split + BU scope + default UoM (for picker auto-fill).
  isSaleable: boolean; isPurchasable: boolean; businessUnitId: number | null;
  defaultUomText: string | null;
}
export interface ProductDetail {
  productId: number; productCode: string; nameTh: string; nameEn: string | null;
  productType: ProductTypeStr; defaultUomText: string | null;
  defaultUnitPrice: number | null; defaultOutputTaxCodeId: number | null;
  defaultInputTaxCodeId: number | null; defaultWhtTypeId: number | null;
  descriptionTh: string | null; notes: string | null; isActive: boolean;
  isSaleable: boolean; isPurchasable: boolean; businessUnitId: number | null;
}
// cont.81 — line-item picker filter context: which side of the ledger.
export type ProductPurpose = 'sale' | 'purchase';
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
  customerId: number; businessUnitId: number | null;
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
  customerId: number; businessUnitId: number | null;
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
  customerId: number; businessUnitId: number | null;
}
export interface DeliveryOrderDetail {
  deliveryOrderId: number; docNo: string | null; status: string; docDate: string;
  customerId: number; customerName: string; businessUnitId: number | null;
  isCombinedWithTi: boolean; taxInvoiceId: number | null;
  // Phase 2a (DO → Invoice): set once an Invoice has been created from this DO.
  // Optional — older BE payloads omit it; the FE treats absent as "no invoice yet".
  billingNoteId?: number | null;
  salesOrderId: number | null; subtotalAmount: number; vatAmount: number;
  totalAmount: number; lines: ChainLineDto[];
}
// Sprint 13h P6.2 — Billing Note (ใบแจ้งหนี้/ใบวางบิล)
export interface BillingNoteListItem {
  billingNoteId: number; docNo: string | null; status: string;
  docDate: string; dueDate: string; customerName: string;
  totalAmount: number; quotationId: number | null;
  customerId: number; businessUnitId: number | null;
}
// Sprint 13i C7 — a TaxInvoice grouped by a BN, from the join table.
export interface BillingNoteTaxInvoiceRef {
  taxInvoiceId: number; docNo: string | null; appliedAmount: number;
}
export interface BillingNoteDetail {
  billingNoteId: number; docNo: string | null; status: string;
  docDate: string; dueDate: string; customerId: number; customerName: string;
  businessUnitId: number | null; quotationId: number | null;
  taxInvoices: BillingNoteTaxInvoiceRef[];
  currencyCode: string; subtotalAmount: number; vatAmount: number; totalAmount: number;
  notes: string | null; lines: ChainLineDto[];
}
export interface BillingLineInput {
  productId: number | null; taxInvoiceId: number | null;
  descriptionTh: string; quantity: number; uomText: string;
  unitPrice: number; discountPercent: number;
  taxCodeId: number; taxCode: string; taxRate: number;
  productType: string | null;
}
export interface CreateBillingNoteRequest {
  docDate: string; dueDate: string; customerId: number;
  businessUnitId: number | null; quotationId: number | null;
  taxInvoiceIds: number[] | null;
  currencyCode: string; exchangeRate: number;
  notes: string | null; internalNotes: string | null;
  lines: BillingLineInput[];
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
  businessUnitCode: string | null; businessUnitName: string | null;  // Sprint BU-PURCH
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

// Sprint 13j-PURCH Phase E — AP Aging report (matches BE ApAgingRow/ApAgingReport)
export interface ApAgingRow {
  vendorId: number; vendorName: string; vendorTaxId: string | null;
  current: number; bucket31To60: number; bucket61To90: number;
  bucketOver90: number; total: number;
}
export interface ApAgingReport { asOf: string; rows: ApAgingRow[]; totals: ApAgingRow; }

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

// Sprint 13h P8 — cross-reference graph used by useCrossReferences / cross-ref chips.
export interface DocumentRef {
  id: number; docNo: string | null; status: string;
}
export interface ReceiptRef extends DocumentRef {
  appliedAmount: number;
}
export interface DocumentCrossRefs {
  quotation: DocumentRef | null;
  salesOrder: DocumentRef | null;
  deliveryOrder: DocumentRef | null;
  taxInvoices: DocumentRef[];
  receipts: ReceiptRef[];
  creditNotes: DocumentRef[];
  debitNotes: DocumentRef[];
  billingNotes: DocumentRef[];
}
export type CrossRefDocType = 'tax-invoice' | 'receipt' | 'adjustment-note';

// cont.69 Phase 3 (D7) — unified full document chain (Q→SO→DO→Invoice→TI→RC + CN/DN).
export interface ChainNode {
  id: number; docNo: string | null; docDate: string; status: string; total: number;
  /** Adjustment-note nodes only: 'Credit' | 'Debit'. Null/absent on every other node type. */
  noteType?: 'Credit' | 'Debit' | null;
}
export interface DocumentChain {
  quotation: ChainNode | null;
  salesOrder: ChainNode | null;
  deliveryOrders: ChainNode[];
  invoices: ChainNode[];
  taxInvoices: ChainNode[];
  receipts: ChainNode[];
  adjustmentNotes: ChainNode[];
}
// F (Question-Backend36) — server-resolved Purchase chain. Different topology from Sales
// (one PO root + three lists), so its own DTO; nodes reuse ChainNode for shape parity.
export interface PurchaseChain {
  purchaseOrder: ChainNode | null;
  vendorInvoices: ChainNode[];
  paymentVouchers: ChainNode[];
  whtCertificates: ChainNode[];
}
export type PurchaseChainAnchorType =
  'purchase-order' | 'vendor-invoice' | 'payment-voucher' | 'wht-certificate';

// Anchor type passed to GET /documents/chain?type=…
export type ChainAnchorType =
  | 'quotation' | 'sales-order' | 'delivery-order' | 'billing-note'
  | 'tax-invoice' | 'receipt' | 'adjustment-note';

// Sprint 13j-FE D1 — audit activity trail entry (GET /{docType}/{id}/activity).
export interface ActivityEntry {
  actor: string;
  action: string;
  fromStatus: string | null;
  toStatus: string | null;
  at: string;
  note: string | null;
}
// Route segment used by the activity endpoint (one per sales + purchase doctype).
export type ActivityDocType =
  | 'quotations'
  | 'sales-orders'
  | 'delivery-orders'
  | 'tax-invoices'
  | 'receipts'
  | 'credit-notes'
  | 'debit-notes'
  | 'billing-notes'
  // Purchase doctypes (BP-09 — parity activity rail).
  | 'purchase-orders'
  | 'vendor-invoices'
  | 'payment-vouchers'
  | 'wht-certificates';

// Sprint 13j-FE — print tracking (POST /{docType}/{id}/mark-printed?copy=).
export interface PrintMarkResult {
  originalPrintedAt: string | null;
  printCount: number;
  wasReprint: boolean;
}

// ───────────────────────── Customer master (sales) ─────────────────────────
export type CustomerType = 'Individual' | 'Corporate';
// Mirrors Application.Master.CustomerDto (GET /customers, GET /customers/{id}).
export interface CustomerListItem {
  customerId: number;
  customerCode: string;
  customerType: CustomerType;
  nameTh: string;
  nameEn: string | null;
  taxId: string | null;
  branchCode: string | null;
  vatRegistered: boolean;
  creditLimit: number;
  isActive: boolean;
}
// Mirrors Application.Master.CreateCustomerRequest (POST /customers).
export interface CreateCustomerRequest {
  customerCode: string;
  customerType: CustomerType;
  nameTh: string;
  nameEn: string | null;
  taxId: string | null;
  branchCode: string | null;
  branchName: string | null;
  vatRegistered: boolean;
  billingAddress: string | null;
  contactPerson: string | null;
  phone: string | null;
  email: string | null;
  creditLimit: number;
  paymentTermDays: number;
  defaultCurrency: string;
}
// Mirrors Application.Master.CustomerDetailDto (GET /customers/{id}).
export interface CustomerDetail {
  customerId: number;
  customerCode: string;
  customerType: CustomerType;
  nameTh: string;
  nameEn: string | null;
  taxId: string | null;
  branchCode: string | null;
  branchName: string | null;
  vatRegistered: boolean;
  billingAddress: string | null;
  contactPerson: string | null;
  phone: string | null;
  email: string | null;
  creditLimit: number;
  paymentTermDays: number;
  defaultCurrency: string;
  isActive: boolean;
}
// Mirrors Application.Master.UpdateCustomerRequest (PUT /customers/{id}).
export interface UpdateCustomerRequest {
  nameTh: string;
  nameEn: string | null;
  taxId: string | null;
  branchCode: string | null;
  branchName: string | null;
  vatRegistered: boolean;
  billingAddress: string | null;
  contactPerson: string | null;
  phone: string | null;
  email: string | null;
  creditLimit: number;
  paymentTermDays: number;
  defaultCurrency: string;
  isActive: boolean;
}
