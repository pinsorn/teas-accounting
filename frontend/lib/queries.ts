'use client';

import {
  useMutation,
  useQuery,
  useQueryClient,
} from '@tanstack/react-query';
import { apiGet, apiPost, apiPut, apiDelete, apiUploadFile, qs, fetchAllPages } from './api';
import type {
  CreateTaxInvoiceRequest,
  NumberGapReport,
  TaxInvoiceDetail,
  TaxInvoiceListItem,
  TaxInvoicePostedResult,
  ReceiptListItem,
  ReceiptDetail,
  AdjustmentNoteListItem,
  AdjustmentNoteDetail,
  VendorListItem,
  VendorDetail,
  CreateVendorRequest,
  UpdateVendorRequest,
  ExpenseCategoryLite,
  PaymentVoucherListItem,
  PaymentVoucherDetail,
  WhtCertificateListItem,
  WhtCertificateDetail,
  WhtTypeListItem,
  WhtBaseSuggestion,
  WhtReceivableRegister,
  WhtReceivableAging,
  WhtMissingCertReport,
  TrialBalanceReport,
  ProfitLossReport,
  SalesSummary,
  Pnd30Filing,
  InputVatRegister,
  OutputVatRegister,
  WhtFiling,
  Pnd36Filing,
  TaxFilingHistoryRow,
  BusinessUnitListItem,
  BusinessUnitDetail,
  CreateBusinessUnitRequest,
  UpdateBusinessUnitRequest,
  CompanyBuSetting,
  EmployeeListItem,
  EmployeeDetail,
  CreateEmployeeRequest,
  UpdateEmployeeRequest,
  PayrollRunListItem,
  PayrollRunDetail,
  CreatePayrollRunRequest,
  CompanyProfile,
  UpdateCompanyProfileSoftRequest,
  UpdateRegisteredAddressRequest,
  CompanyListItem,
  CompanyDetail,
  CreateCompanyRequest,
  UpdateCompanyRequest,
  MePermissions,
  ProductListItem,
  ProductDetail,
  QuotationListItem,
  QuotationDetail,
  BillingNoteListItem,
  BillingNoteDetail,
  SalesOrderListItem,
  SalesOrderDetail,
  DeliveryOrderListItem,
  DeliveryOrderDetail,
  VendorInvoiceListItem,
  VendorInvoiceDetail,
  CreateVendorInvoiceRequest,
  CreateViFromPvRequest,
  VendorInvoicePostedResult,
  PaymentVoucherApprovedResult,
  AttachmentItem,
  PurchaseOrderListItem,
  PurchaseOrderDetail,
  OutstandingPoReport,
  ApAgingReport,
  ApiKeyListItem,
  CreateApiKeyRequest,
  ApiKeyCreatedResult,
  CreateReceiptRequest,
  CreateAdjustmentNoteRequest,
  CreateProductRequest,
  UpdateProductRequest,
  CreateQuotationRequest,
  UpdateQuotationRequest,
  CreateSalesOrderRequest,
  CreateDeliveryOrderRequest,
  UpdateBillingNoteRequest,
  CreateBillingNoteRequest,
  CreatePurchaseOrderRequest,
} from './types';

export interface TaxInvoiceFilters {
  dateFrom?: string;
  dateTo?: string;
  customerId?: number;
  status?: string;
  businessUnitId?: number;
  includeUnspecified?: boolean;
}

// cont.81 (DataTable) — fetch-all so the unified client-side table filters/sorts the
// whole set. Server-side filter params are no longer needed (TanStack does it on the
// client); kept the optional arg for back-compat but it is ignored.
export function useTaxInvoices(_filters?: TaxInvoiceFilters) {
  return useQuery({
    queryKey: ['tax-invoices', 'all'],
    queryFn: () => fetchAllPages<TaxInvoiceListItem>('tax-invoices'),
  });
}

export function useTaxInvoice(id: number) {
  return useQuery({
    queryKey: ['tax-invoice', id],
    queryFn: () => apiGet<TaxInvoiceDetail>(`tax-invoices/${id}`),
    enabled: Number.isFinite(id) && id > 0,
  });
}

export function useCreateTaxInvoice() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: CreateTaxInvoiceRequest) =>
      apiPost<{ tax_invoice_id: number }>('tax-invoices/', req),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['tax-invoices'] }),
  });
}

export function usePostTaxInvoice() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: number) =>
      apiPost<TaxInvoicePostedResult>(`tax-invoices/${id}/post`),
    onSuccess: (_r, id) => {
      qc.invalidateQueries({ queryKey: ['tax-invoices'] });
      qc.invalidateQueries({ queryKey: ['tax-invoice', id] });
    },
  });
}

export function useNumberGaps(year?: number, month?: number, docType?: string) {
  return useQuery({
    queryKey: ['number-gaps', year, month, docType],
    queryFn: () =>
      apiGet<NumberGapReport>(
        `reports/number-gaps${qs({ year, month, doc_type: docType })}`,
      ),
  });
}

// ───────────────────────── Sprint 4: Receipt + CN/DN ─────────────────────────

// cont.82 — fetch-all for the unified client-side DataTable.
export function useReceipts(businessUnitId?: number, includeUnspecified?: boolean) {
  return useQuery({
    queryKey: ['receipts', 'all', businessUnitId, includeUnspecified],
    queryFn: () => fetchAllPages<ReceiptListItem>('receipts', {
      businessUnitId, includeUnspecified: includeUnspecified || undefined,
    }),
  });
}
export function useReceipt(id: number) {
  return useQuery({
    queryKey: ['receipt', id],
    queryFn: () => apiGet<ReceiptDetail>(`receipts/${id}`),
    enabled: id > 0,
  });
}
export function useCreateReceipt() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: CreateReceiptRequest) => apiPost<{ receipt_id: number }>('receipts/', req),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['receipts'] }),
  });
}
export function usePostReceipt() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => apiPost(`receipts/${id}/post`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['receipts'] }),
  });
}

export function useAdjustmentNotes(
  noteType?: 'CREDIT' | 'DEBIT', businessUnitId?: number, includeUnspecified?: boolean,
) {
  return useQuery({
    queryKey: ['adjustment-notes', 'all', noteType, businessUnitId, includeUnspecified],
    queryFn: () => fetchAllPages<AdjustmentNoteListItem>('tax-adjustment-notes', {
      noteType, businessUnitId, includeUnspecified: includeUnspecified || undefined,
    }),
  });
}
export function useAdjustmentNote(id: number) {
  return useQuery({
    queryKey: ['adjustment-note', id],
    queryFn: () => apiGet<AdjustmentNoteDetail>(`tax-adjustment-notes/${id}`),
    enabled: id > 0,
  });
}
export function useCreateAdjustmentNote() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: CreateAdjustmentNoteRequest) => apiPost<{ note_id: number }>('tax-adjustment-notes/', req),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['adjustment-notes'] }),
  });
}
export function usePostAdjustmentNote() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => apiPost(`tax-adjustment-notes/${id}/post`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['adjustment-notes'] }),
  });
}

// ───────────────────────── Sprint 5: Purchase (AP) ─────────────────────────
// /vendors + /expense-categories return plain arrays; PV + WHT use CursorPage.

export function useVendors(search?: string) {
  return useQuery({
    queryKey: ['vendors', search ?? ''],
    queryFn: () => apiGet<VendorListItem[]>(`vendors${qs({ search, pageSize: 100 })}`),
  });
}

// Sprint 13j-FE — Customer master (sales). GET /customers returns a plain array.
import type { CustomerListItem, CustomerDetail, CreateCustomerRequest, UpdateCustomerRequest } from './types';
export function useCustomers(search?: string) {
  return useQuery({
    queryKey: ['customers', search ?? ''],
    queryFn: () => apiGet<CustomerListItem[]>(`customers${qs({ search, pageSize: 100 })}`),
  });
}
export function useCustomer(id: number | null) {
  return useQuery({
    queryKey: ['customer', id],
    enabled: id != null && Number.isFinite(id) && id > 0,
    queryFn: () => apiGet<CustomerDetail>(`customers/${id}`),
  });
}
export function useCreateCustomer() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: CreateCustomerRequest) => apiPost<{ customer_id: number }>('customers', req),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['customers'] }),
  });
}
// Sprint 13j-FE — supply customer 50ทวิ no/date after posting a receipt.
export function useSetReceiptWhtCert(id: number) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: { certNo: string; certDate: string | null }) =>
      apiPost<void>(`receipts/${id}/wht-cert`, body),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['receipt', id] }),
  });
}

// Sprint 13j-FE — record a print (original/copy) for a fiscal doc.
import type { PrintMarkResult } from './types';
export function useMarkPrinted(docType: string, id: number) {
  return useMutation({
    mutationFn: (copy: boolean) => apiPost<PrintMarkResult>(`${docType}/${id}/mark-printed?copy=${copy}`),
  });
}
export function useUpdateCustomer(id: number) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: UpdateCustomerRequest) => apiPut<void>(`customers/${id}`, req),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['customers'] });
      qc.invalidateQueries({ queryKey: ['customer', id] });
    },
  });
}
export function useVendor(id: number) {
  return useQuery({
    queryKey: ['vendor', id],
    queryFn: () => apiGet<VendorDetail>(`vendors/${id}`),
    enabled: id > 0,
  });
}
export function useCreateVendor() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: CreateVendorRequest) => apiPost<{ vendor_id: number }>('vendors/', req),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['vendors'] }),
  });
}
export function useUpdateVendor(id: number) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: UpdateVendorRequest) => apiPut<void>(`vendors/${id}`, req),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['vendors'] });
      qc.invalidateQueries({ queryKey: ['vendor', id] });
    },
  });
}

export function useExpenseCategories() {
  return useQuery({
    queryKey: ['expense-categories'],
    queryFn: () => apiGet<ExpenseCategoryLite[]>('expense-categories'),
  });
}

export function usePaymentVouchers(incompleteOnly = false) {
  return useQuery({
    queryKey: ['payment-vouchers', 'all', { incompleteOnly }],
    queryFn: () => fetchAllPages<PaymentVoucherListItem>('payment-vouchers', {
      incompleteOnly: incompleteOnly || undefined,
    }),
  });
}
export function usePaymentVoucher(id: number) {
  return useQuery({
    queryKey: ['payment-voucher', id],
    queryFn: () => apiGet<PaymentVoucherDetail>(`payment-vouchers/${id}`),
    enabled: id > 0,
  });
}

export function useWhtCertificates() {
  return useQuery({
    queryKey: ['wht-certificates', 'all'],
    queryFn: () => fetchAllPages<WhtCertificateListItem>('wht-certificates'),
  });
}
export function useWhtCertificate(id: number) {
  return useQuery({
    queryKey: ['wht-certificate', id],
    queryFn: () => apiGet<WhtCertificateDetail>(`wht-certificates/${id}`),
    enabled: id > 0,
  });
}

// ───────────────────────── Sprint 6: VendorInvoice + PV approve ─────────────
export function useVendorInvoices(incompleteOnly = false) {
  return useQuery({
    queryKey: ['vendor-invoices', 'all', { incompleteOnly }],
    queryFn: () => fetchAllPages<VendorInvoiceListItem>('vendor-invoices', {
      incompleteOnly: incompleteOnly || undefined,
    }),
  });
}
export function useVendorInvoice(id: number) {
  return useQuery({
    queryKey: ['vendor-invoice', id],
    queryFn: () => apiGet<VendorInvoiceDetail>(`vendor-invoices/${id}`),
    enabled: id > 0,
  });
}
export function useCreateVendorInvoice() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: CreateVendorInvoiceRequest) =>
      apiPost<{ vendor_invoice_id: number }>('vendor-invoices/', req),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['vendor-invoices'] }),
  });
}
// purchase-completeness Phase 2 — guided PV→VI create. Pre-fills a VI draft from
// the PV + links PV.VendorInvoiceId. 409 (pv.vi_exists) if a VI is already linked.
export function useCreateViFromPv() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ pvId, req }: { pvId: number; req: CreateViFromPvRequest }) =>
      apiPost<{ vendor_invoice_id: number }>(`payment-vouchers/${pvId}/vendor-invoice`, req),
    onSuccess: (_r, { pvId }) => {
      qc.invalidateQueries({ queryKey: ['payment-vouchers'] });
      qc.invalidateQueries({ queryKey: ['payment-voucher', pvId] });
      qc.invalidateQueries({ queryKey: ['vendor-invoices'] });
    },
  });
}
export function usePostVendorInvoice() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: number) =>
      apiPost<VendorInvoicePostedResult>(`vendor-invoices/${id}/post`),
    onSuccess: (_r, id) => {
      qc.invalidateQueries({ queryKey: ['vendor-invoices'] });
      qc.invalidateQueries({ queryKey: ['vendor-invoice', id] });
    },
  });
}

export function useApprovePaymentVoucher() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: number) =>
      apiPost<PaymentVoucherApprovedResult>(`payment-vouchers/${id}/approve`),
    onSuccess: (_r, id) => {
      qc.invalidateQueries({ queryKey: ['payment-vouchers'] });
      qc.invalidateQueries({ queryKey: ['payment-voucher', id] });
    },
  });
}
export function usePostPaymentVoucher() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => apiPost(`payment-vouchers/${id}/post`),
    onSuccess: (_r, id) => {
      qc.invalidateQueries({ queryKey: ['payment-vouchers'] });
      qc.invalidateQueries({ queryKey: ['payment-voucher', id] });
    },
  });
}

// ───────────────────────── Sprint 8: Business Units ─────────────────────────
export function useBusinessUnits(includeInactive = false) {
  return useQuery({
    queryKey: ['business-units', includeInactive],
    queryFn: () => apiGet<BusinessUnitListItem[]>(
      `business-units${qs({ includeInactive: includeInactive ? 'true' : undefined })}`),
  });
}

// cont.82 — shared BU id → "CODE — name" resolver for DataTable BU columns/filters
// (list rows carry only businessUnitId). Returns "—" when unset so a faceted select
// groups the no-BU rows under one option.
export function useBusinessUnitName() {
  const { data = [] } = useBusinessUnits(true);
  return (id: number | null | undefined): string => {
    if (id == null) return '—';
    const b = data.find((u) => u.businessUnitId === id);
    return b ? `${b.code} — ${b.nameTh}` : `#${id}`;
  };
}
export function useBusinessUnit(id: number) {
  return useQuery({
    queryKey: ['business-unit', id],
    queryFn: () => apiGet<BusinessUnitDetail>(`business-units/${id}`),
    enabled: id > 0,
  });
}
export function useCreateBusinessUnit() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: CreateBusinessUnitRequest) => apiPost<{ business_unit_id: number }>('business-units/', req),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['business-units'] }),
  });
}
export function useUpdateBusinessUnit() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (v: { id: number; req: UpdateBusinessUnitRequest }) =>
      apiPut<void>(`business-units/${v.id}`, v.req),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['business-units'] }),
  });
}
export function useDeactivateBusinessUnit() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => apiDelete(`business-units/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['business-units'] }),
  });
}

// ───────────────────────── Payroll P-A: Employees ─────────────────────────
export function useEmployees(includeInactive = false) {
  return useQuery({
    queryKey: ['employees', includeInactive],
    queryFn: () => apiGet<EmployeeListItem[]>(
      `employees${qs({ includeInactive: includeInactive ? 'true' : undefined })}`),
  });
}
export function useEmployee(id: number) {
  return useQuery({
    queryKey: ['employee', id],
    queryFn: () => apiGet<EmployeeDetail>(`employees/${id}`),
    enabled: id > 0,
  });
}
export function useCreateEmployee() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: CreateEmployeeRequest) => apiPost<{ employee_id: number }>('employees/', req),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['employees'] }),
  });
}
export function useUpdateEmployee() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (v: { id: number; req: UpdateEmployeeRequest }) =>
      apiPut<void>(`employees/${v.id}`, v.req),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['employees'] }),
  });
}
export function useDeactivateEmployee() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => apiDelete(`employees/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['employees'] }),
  });
}
// ───────────────────────── Payroll P-C/P-D: Runs ─────────────────────────
export function usePayrollRuns() {
  return useQuery({
    queryKey: ['payroll-runs'],
    queryFn: () => apiGet<PayrollRunListItem[]>('payroll/runs'),
  });
}
export function usePayrollRun(id: number) {
  return useQuery({
    queryKey: ['payroll-run', id],
    queryFn: () => apiGet<PayrollRunDetail>(`payroll/runs/${id}`),
    enabled: id > 0,
  });
}
export function useCreatePayrollRun() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: CreatePayrollRunRequest) => apiPost<unknown>('payroll/runs', req),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['payroll-runs'] }),
  });
}
function useRunTransition(action: 'approve' | 'post' | 'pay') {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => apiPost<unknown>(`payroll/runs/${id}/${action}`),
    onSuccess: (_d, id) => {
      qc.invalidateQueries({ queryKey: ['payroll-runs'] });
      qc.invalidateQueries({ queryKey: ['payroll-run', id] });
    },
  });
}
export const useApprovePayrollRun = () => useRunTransition('approve');
export const usePostPayrollRun = () => useRunTransition('post');
export const usePayPayrollRun = () => useRunTransition('pay');
export function useDeletePayrollRun() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => apiDelete(`payroll/runs/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['payroll-runs'] }),
  });
}

// ───────────────────────── Sprint 13d P3: Permissions ─────────────────────────
export function useMePermissions() {
  return useQuery({
    queryKey: ['me-permissions'],
    queryFn: () => apiGet<MePermissions>('me/permissions'),
    staleTime: 5 * 60_000, // scopes change rarely; refetched on login (new mount)
  });
}

// ───────────────────────── Sprint 13d P6: Company Profile ─────────────────────────
export function useCompanyProfile() {
  return useQuery({
    queryKey: ['company-profile'],
    queryFn: () => apiGet<CompanyProfile>('company-profile'),
  });
}
export function useUpdateCompanyProfileSoft() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: UpdateCompanyProfileSoftRequest) =>
      apiPut<unknown>('company-profile/soft', req),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['company-profile'] }),
  });
}

// Registered address (HARD) edit — admin confirmed the DBD/ภ.พ.09 filing in the UI.
export function useUpdateRegisteredAddress() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: UpdateRegisteredAddressRequest) =>
      apiPut<unknown>('company-profile/registered-address', req),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['company-profile'] }),
  });
}

// Sprint 13h P10 — company logo upload (multipart). Returns the new logo URL.
export function useUploadCompanyLogo() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (file: File) =>
      apiUploadFile<{ logoUrl: string }>('company-profile/logo', file),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['company-profile'] }),
  });
}

export function useCompanyBuSetting() {
  return useQuery({
    queryKey: ['company-bu-setting'],
    queryFn: () => apiGet<CompanyBuSetting>('business-units/company-setting'),
  });
}
export function useSetCompanyBuSetting() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (requiresBusinessUnit: boolean) =>
      apiPut<unknown>('business-units/company-setting', { requiresBusinessUnit }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['company-bu-setting'] }),
  });
}

// ──────────── Per-company VAT mode — companies CRUD (/settings/companies) ────────────
// Super-admin only (BE enforces master.company.manage on the whole /companies group).

export function useCompanies() {
  return useQuery({
    queryKey: ['companies'],
    queryFn: () => apiGet<CompanyListItem[]>('companies'),
  });
}

export function useCompany(id: number) {
  return useQuery({
    queryKey: ['company', id],
    queryFn: () => apiGet<CompanyDetail>(`companies/${id}`),
    enabled: Number.isFinite(id) && id > 0,
  });
}

export function useCreateCompany() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: CreateCompanyRequest) => apiPost<unknown>('companies/', req),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['companies'] }),
  });
}

export function useUpdateCompany(id: number) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: UpdateCompanyRequest) => apiPut<unknown>(`companies/${id}`, req),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['companies'] });
      qc.invalidateQueries({ queryKey: ['company', id] });
      // §4.6 — vatRegistered/vatRate drive app-wide VAT mode (nav, e-Tax CTAs,
      // doc forms) via /system/info → must refetch immediately after an edit.
      qc.invalidateQueries({ queryKey: ['system-info'] });
    },
  });
}

// ───────────────────────── Sprint 8.5 — VAT mode ─────────────────────────

export interface SystemInfo {
  vatMode: boolean;
  vatRate: number;
  pnd30SubmissionMode: string;
}

/** /system/info — VAT-mode drives e-Tax CTA visibility (non-VAT can't e-Tax). */
export function useSystemInfo() {
  return useQuery({
    queryKey: ['system-info'],
    queryFn: async () => {
      const r = await apiGet<{
        vat_mode: boolean; vat_rate: number; pnd30_submission_mode: string;
      }>('system/info');
      return {
        vatMode: r.vat_mode,
        vatRate: r.vat_rate,
        pnd30SubmissionMode: r.pnd30_submission_mode,
      } satisfies SystemInfo;
    },
    staleTime: 5 * 60_000,
  });
}

export type VatThresholdStatus =
  | 'NotApplicable' | 'Ok' | 'Approaching' | 'Exceeded';

/** /system/vat-threshold-status — ม.85/1 rolling-12-mo revenue band. */
export function useVatThresholdStatus() {
  return useQuery({
    queryKey: ['vat-threshold-status'],
    queryFn: () =>
      apiGet<{ status: VatThresholdStatus }>('system/vat-threshold-status'),
    staleTime: 5 * 60_000,
  });
}

// ───────────────────────── Sprint 8.6 — AR-side WHT ─────────────────────────

export function useWhtTypes(includeInactive = false) {
  return useQuery({
    queryKey: ['wht-types', includeInactive],
    queryFn: () => apiGet<WhtTypeListItem[]>(
      `wht-types${qs({ includeInactive: includeInactive ? 'true' : undefined })}`),
  });
}
export function useCreateWhtType() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: {
      code: string; nameTh: string; nameEn: string | null;
      incomeTypeCode: string; formType: string; rate: number;
    }) => apiPost<{ wht_type_id: number }>('wht-types/', req),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['wht-types'] }),
  });
}
export function useUpdateWhtType() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, req }: {
      id: number;
      req: { nameTh: string; nameEn: string | null; incomeTypeCode: string; formType: string };
    }) => apiPut<unknown>(`wht-types/${id}`, req),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['wht-types'] }),
  });
}
export function useDeactivateWhtType() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => apiDelete<unknown>(`wht-types/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['wht-types'] }),
  });
}
export function useReactivateWhtType() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => apiPost<unknown>(`wht-types/${id}/reactivate`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['wht-types'] }),
  });
}
export function useChangeWhtRate() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, newRate, effectiveFrom }: {
      id: number; newRate: number; effectiveFrom: string;
    }) => apiPost<unknown>(`wht-types/${id}/change-rate`, { newRate, effectiveFrom }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['wht-types'] }),
  });
}
// Sprint (multi-category WHT) — POST the applied amounts so the server can pro-rate
// the per-income-type base across partial payments. Returns Categories breakdown.
export function useWhtBaseSuggest(
  // VAT path sets taxInvoiceId; non-VAT Invoice→Receipt sets billingNoteId.
  applications: { taxInvoiceId?: number; billingNoteId?: number; appliedAmount: number }[],
  customerId: number,
) {
  const key = applications
    .map((a) => `${a.taxInvoiceId ?? ''}|${a.billingNoteId ?? ''}:${a.appliedAmount}`)
    .join(',');
  return useQuery({
    queryKey: ['wht-base-suggest', key, customerId],
    enabled: applications.length > 0 && customerId > 0,
    queryFn: () => apiPost<WhtBaseSuggestion>(
      'receipts/wht-base-suggest', { customerId, applications }),
  });
}
export function useWhtReceivableRegister(fromDate: string, toDate: string) {
  return useQuery({
    queryKey: ['wht-receivable-register', fromDate, toDate],
    enabled: !!fromDate && !!toDate,
    queryFn: () => apiGet<WhtReceivableRegister>(
      `reports/wht-receivable-register${qs({ fromDate, toDate })}`),
  });
}
export function useWhtReceivableAging() {
  return useQuery({
    queryKey: ['wht-receivable-aging'],
    queryFn: () => apiGet<WhtReceivableAging>('reports/wht-receivable-aging'),
  });
}
// Sprint 13j-tail — receipts missing the customer 50ทวิ cert (period = yyyymm)
export function useWhtMissingCert(period: number) {
  return useQuery({
    queryKey: ['wht-receivable-missing-cert', period],
    enabled: period > 0,
    queryFn: () => apiGet<WhtMissingCertReport>(
      `reports/wht-receivable-missing-cert${qs({ period })}`),
  });
}

// ───────────────────────── Sprint 9 Part A — financial reports ─────────────
export function useTrialBalance(asOfDate: string, includeInactive = false) {
  return useQuery({
    queryKey: ['trial-balance', asOfDate, includeInactive],
    enabled: !!asOfDate,
    queryFn: () => apiGet<TrialBalanceReport>(
      `reports/trial-balance${qs({ asOfDate, includeInactive: includeInactive || undefined })}`),
  });
}
export function useProfitLoss(
  from: string, to: string, businessUnitId?: number, includeUnspecified?: boolean) {
  return useQuery({
    queryKey: ['profit-loss', from, to, businessUnitId, includeUnspecified],
    enabled: !!from && !!to,
    queryFn: () => apiGet<ProfitLossReport>(
      `reports/profit-loss${qs({ from, to, businessUnitId,
        includeUnspecified: includeUnspecified || undefined })}`),
  });
}
export function useTaxSummary(year: number, businessUnitId?: number) {
  return useQuery({
    queryKey: ['tax-summary', year, businessUnitId],
    queryFn: () => apiGet<import('./types').TaxSummaryReport>(
      `reports/tax-summary${qs({ year, businessUnitId })}`),
  });
}
export function useSalesSummary(from: string, to: string, groupBy: string) {
  return useQuery({
    queryKey: ['sales-summary', from, to, groupBy],
    enabled: !!from && !!to,
    queryFn: () => apiGet<SalesSummary>(
      `reports/sales-summary${qs({ from, to, groupBy })}`),
  });
}

// ───────────────────────── Sprint 9 Part B — VAT compliance ────────────────
// ภ.พ.30 preview/finalize is a POST action (mode=preview|finalize).
export function usePnd30() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (v: { period: number; mode: 'preview' | 'finalize' }) =>
      apiPost<Pnd30Filing>(`tax-filings/pnd30${qs({ period: v.period, mode: v.mode })}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['tax-filings'] }),
  });
}
export function useInputVatRegister(period: number) {
  return useQuery({
    queryKey: ['input-vat-register', period],
    enabled: !!period,
    queryFn: () => apiGet<InputVatRegister>(`reports/input-vat-register${qs({ period })}`),
  });
}
export function useOutputVatRegister(period: number) {
  return useQuery({
    queryKey: ['output-vat-register', period],
    enabled: !!period,
    queryFn: () => apiGet<OutputVatRegister>(`reports/output-vat-register${qs({ period })}`),
  });
}

// ───────────────────────── Sprint 9 Part C — WHT filings ───────────────────
function whtFilingMutation(form: 'pnd3' | 'pnd53' | 'pnd54') {
  return function () {
    const qc = useQueryClient();
    return useMutation({
      mutationFn: (v: { period: number; mode: 'preview' | 'finalize' }) =>
        apiPost<WhtFiling>(`tax-filings/${form}${qs({ period: v.period, mode: v.mode })}`),
      onSuccess: () => qc.invalidateQueries({ queryKey: ['tax-filings'] }),
    });
  };
}
export const usePnd3 = whtFilingMutation('pnd3');
export const usePnd53 = whtFilingMutation('pnd53');
export const usePnd54 = whtFilingMutation('pnd54');

export function usePnd36() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (v: { period: number; mode: 'preview' | 'finalize' }) =>
      apiPost<Pnd36Filing>(`tax-filings/pnd36${qs({ period: v.period, mode: v.mode })}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['tax-filings'] }),
  });
}
export function useTaxFilings() {
  return useQuery({
    queryKey: ['tax-filings'],
    queryFn: () => apiGet<TaxFilingHistoryRow[]>('tax-filings'),
  });
}

// ───────────────────────── Sprint 10 Part A — Product master ───────────────
export function useProducts(
  includeInactive = false, search?: string, purpose?: 'sale' | 'purchase',
  productType?: string, isActive?: boolean, businessUnitId?: number | null,
) {
  return useQuery({
    queryKey: ['products', includeInactive, search ?? '', purpose ?? '', productType ?? '', isActive ?? '', businessUnitId ?? ''],
    queryFn: () => apiGet<ProductListItem[]>(
      `products${qs({
        includeInactive: includeInactive || undefined,
        search: search || undefined,
        purpose,
        productType: productType || undefined,
        isActive,
        businessUnitId: businessUnitId ?? undefined,
      })}`),
  });
}
export function useProduct(id: number | null) {
  return useQuery({
    queryKey: ['product', id],
    enabled: id != null,
    queryFn: () => apiGet<ProductDetail>(`products/${id}`),
  });
}
export function useCreateProduct() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: CreateProductRequest) => apiPost<{ product_id: number }>('products/', req),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['products'] }),
  });
}
export function useUpdateProduct() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (v: { id: number; req: UpdateProductRequest }) => apiPut(`products/${v.id}`, v.req),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['products'] }),
  });
}
export function useDeactivateProduct() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => apiPost(`products/${id}/deactivate`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['products'] }),
  });
}

// ───────────────────────── Sprint 10 Part B — Q→SO→DO chain ────────────────
export function useQuotations(status?: string) {
  return useQuery({
    queryKey: ['quotations', status ?? ''],
    queryFn: () => apiGet<QuotationListItem[]>(`quotations${qs({ status: status || undefined })}`),
  });
}
export function useQuotation(id: number | null) {
  return useQuery({
    queryKey: ['quotation', id], enabled: id != null,
    queryFn: () => apiGet<QuotationDetail>(`quotations/${id}`),
  });
}
export function useCreateQuotation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: CreateQuotationRequest) => apiPost<{ quotation_id: number }>('quotations/', req),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['quotations'] }),
  });
}
export function useUpdateQuotation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (v: { id: number; req: UpdateQuotationRequest }) =>
      apiPut(`quotations/${v.id}`, v.req),
    onSuccess: (_d, v) => {
      qc.invalidateQueries({ queryKey: ['quotations'] });
      qc.invalidateQueries({ queryKey: ['quotation', v.id] });
    },
  });
}
export function useDeleteQuotation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => apiDelete(`quotations/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['quotations'] }),
  });
}
export function useQuotationAction() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (v: { id: number; action: string; body?: unknown }) =>
      apiPost(`quotations/${v.id}/${v.action}`, v.body),
    onSuccess: (_d, v) => {
      qc.invalidateQueries({ queryKey: ['quotations'] });
      qc.invalidateQueries({ queryKey: ['quotation', v.id] });
    },
  });
}
export function useSalesOrders(status?: string) {
  return useQuery({
    queryKey: ['sales-orders', status ?? ''],
    queryFn: () => apiGet<SalesOrderListItem[]>(`sales-orders${qs({ status: status || undefined })}`),
  });
}
export function useSalesOrder(id: number | null) {
  return useQuery({
    queryKey: ['sales-order', id], enabled: id != null,
    queryFn: () => apiGet<SalesOrderDetail>(`sales-orders/${id}`),
  });
}
export function useCreateSalesOrder() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: CreateSalesOrderRequest) => apiPost<{ sales_order_id: number }>('sales-orders/', req),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['sales-orders'] }),
  });
}
export function usePostSalesOrder() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => apiPost(`sales-orders/${id}/post`),
    onSuccess: (_d, id) => {
      qc.invalidateQueries({ queryKey: ['sales-orders'] });
      qc.invalidateQueries({ queryKey: ['sales-order', id] });
    },
  });
}
export function useCreateDeliveryOrderDraft() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: CreateDeliveryOrderRequest) =>
      apiPost<{ delivery_order_id: number }>('delivery-orders/', req),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['delivery-orders'] }),
  });
}
export function useCreateDeliveryOrder() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (v: { soId: number; req: CreateDeliveryOrderRequest }) =>
      apiPost<{ delivery_order_id: number }>(`sales-orders/${v.soId}/delivery-orders`, v.req),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['delivery-orders'] }),
  });
}
export function useDeliveryOrders(status?: string) {
  return useQuery({
    queryKey: ['delivery-orders', status ?? ''],
    queryFn: () => apiGet<DeliveryOrderListItem[]>(`delivery-orders${qs({ status: status || undefined })}`),
  });
}
export function useDeliveryOrder(id: number | null) {
  return useQuery({
    queryKey: ['delivery-order', id], enabled: id != null,
    queryFn: () => apiGet<DeliveryOrderDetail>(`delivery-orders/${id}`),
  });
}
export function useDeliveryOrderAction() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (v: { id: number; action: string }) =>
      apiPost(`delivery-orders/${v.id}/${v.action}`),
    onSuccess: (_d, v) => {
      qc.invalidateQueries({ queryKey: ['delivery-orders'] });
      qc.invalidateQueries({ queryKey: ['delivery-order', v.id] });
    },
  });
}
// New flow (Phase 2a): DO → Invoice (ใบแจ้งหนี้, BE BillingNote). Copies the DO
// lines into a fresh Draft Invoice and returns its id for navigation.
export function useCreateInvoiceFromDeliveryOrder() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: number) =>
      apiPost<{ billing_note_id: number }>(`delivery-orders/${id}/create-invoice`),
    onSuccess: (_d, id) => {
      qc.invalidateQueries({ queryKey: ['delivery-order', id] });
      qc.invalidateQueries({ queryKey: ['billing-notes'] });
    },
  });
}

// ───────────────────────── Sprint 13h P6.2 — Billing Note ─────────────────
export function useBillingNotes(status?: string) {
  return useQuery({
    queryKey: ['billing-notes', status ?? ''],
    queryFn: () => apiGet<BillingNoteListItem[]>(`billing-notes${qs({ status: status || undefined })}`),
  });
}
export function useBillingNote(id: number | null) {
  return useQuery({
    queryKey: ['billing-note', id], enabled: id != null,
    queryFn: () => apiGet<BillingNoteDetail>(`billing-notes/${id}`),
  });
}
export function useCreateBillingNote() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: CreateBillingNoteRequest) => apiPost<{ billing_note_id: number }>('billing-notes/', req),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['billing-notes'] }),
  });
}
export function useUpdateBillingNote() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (v: { id: number; req: UpdateBillingNoteRequest }) =>
      apiPut(`billing-notes/${v.id}`, v.req),
    onSuccess: (_d, v) => {
      qc.invalidateQueries({ queryKey: ['billing-notes'] });
      qc.invalidateQueries({ queryKey: ['billing-note', v.id] });
    },
  });
}
export function useDeleteBillingNote() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => apiDelete(`billing-notes/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['billing-notes'] }),
  });
}
export function useBillingNoteAction() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (v: { id: number; action: string; body?: unknown }) =>
      apiPost(`billing-notes/${v.id}/${v.action}`, v.body),
    onSuccess: (_d, v) => {
      qc.invalidateQueries({ queryKey: ['billing-notes'] });
      qc.invalidateQueries({ queryKey: ['billing-note', v.id] });
    },
  });
}
// New flow (Phase 2a): Invoice → Tax Invoice. VAT-only; BE returns 422
// `ti.non_vat_blocked` for a non-VAT company (the caller toasts the detail).
export function useCreateTaxInvoiceFromBillingNote() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: number) =>
      apiPost<{ tax_invoice_id: number }>(`billing-notes/${id}/create-tax-invoice`),
    onSuccess: (_d, id) => {
      qc.invalidateQueries({ queryKey: ['billing-note', id] });
      qc.invalidateQueries({ queryKey: ['tax-invoices'] });
    },
  });
}

// ───────────────────────── Sprint 11 — attachments ────────────────────────
export const attachmentDownloadUrl = (id: number) =>
  `/api/proxy/attachments/${id}/download`;

export function useAttachments(parentType: string, parentId: number) {
  return useQuery({
    queryKey: ['attachments', parentType, parentId],
    enabled: !!parentType && !!parentId,
    queryFn: () => apiGet<{ items: AttachmentItem[] }>(
      `attachments${qs({ parent_type: parentType, parent_id: parentId })}`),
  });
}
export function useUploadAttachment() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (fd: FormData) => {
      // multipart goes through the BFF proxy; let the browser set the boundary.
      const res = await fetch('/api/proxy/attachments', { method: 'POST', body: fd });
      if (!res.ok) {
        const b = await res.json().catch(() => null);
        throw new Error(b?.detail ?? `Upload failed (${res.status})`);
      }
      return res.json();
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['attachments'] }),
  });
}
export function useDeleteAttachment() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => apiDelete(`attachments/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['attachments'] }),
  });
}

// ───────────────────────── Sprint 12 — internal Purchase Order ─────────────
export function usePurchaseOrders(status?: string, vendorId?: number) {
  return useQuery({
    queryKey: ['purchase-orders', status ?? '', vendorId ?? 0],
    queryFn: () => apiGet<PurchaseOrderListItem[]>(
      `purchase-orders${qs({ status: status || undefined, vendorId: vendorId || undefined })}`),
  });
}
export function usePurchaseOrder(id: number | null) {
  return useQuery({
    queryKey: ['purchase-order', id], enabled: id != null,
    queryFn: () => apiGet<PurchaseOrderDetail>(`purchase-orders/${id}`),
  });
}
export function useCreatePurchaseOrder() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: CreatePurchaseOrderRequest) => apiPost<{ purchase_order_id: number }>('purchase-orders/', req),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['purchase-orders'] }),
  });
}
export function usePurchaseOrderAction() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (v: { id: number; action: string; body?: unknown }) =>
      apiPost(`purchase-orders/${v.id}/${v.action}`, v.body),
    onSuccess: (_d, v) => {
      qc.invalidateQueries({ queryKey: ['purchase-orders'] });
      qc.invalidateQueries({ queryKey: ['purchase-order', v.id] });
    },
  });
}
export function useOutstandingPo(asOf: string, vendorId?: number, overdueOnly = false) {
  return useQuery({
    queryKey: ['outstanding-po', asOf, vendorId ?? 0, overdueOnly],
    queryFn: () => apiGet<OutstandingPoReport>(
      `reports/outstanding-po${qs({ as_of: asOf, vendorId: vendorId || undefined, overdue_only: overdueOnly || undefined })}`),
  });
}
// Sprint 13j-PURCH Phase E — AP Aging (BE query param is camelCase ?asOf=, NOT as_of)
export function useApAgingReport(asOf: string, vendorId?: number) {
  return useQuery({
    queryKey: ['ap-aging', asOf, vendorId ?? 0],
    queryFn: () => apiGet<ApAgingReport>(
      `reports/ap-aging${qs({ asOf, vendorId: vendorId || undefined })}`),
  });
}

// ───────────────────────── Sprint 14 — external API keys ───────────────────
export function useApiKeys() {
  return useQuery({
    queryKey: ['api-keys'],
    queryFn: () => apiGet<ApiKeyListItem[]>('api-keys'),
  });
}
export function useCreateApiKey() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: CreateApiKeyRequest) =>
      apiPost<ApiKeyCreatedResult>('api-keys/', req),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['api-keys'] }),
  });
}
export function useRotateApiKey() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => apiPost<ApiKeyCreatedResult>(`api-keys/${id}/rotate`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['api-keys'] }),
  });
}
export function useRevokeApiKey() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => apiDelete(`api-keys/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['api-keys'] }),
  });
}

// Sprint 13h P8 — cross-reference resolver used by the chip rows on TI/RC/CN/DN detail.
import type { CrossRefDocType, DocumentCrossRefs } from './types';
export function useCrossReferences(docType: CrossRefDocType, id: number | null) {
  return useQuery({
    queryKey: ['cross-refs', docType, id],
    queryFn: () => apiGet<DocumentCrossRefs>(`document-cross-refs/${docType}/${id}`),
    enabled: id != null && Number.isFinite(id) && id > 0,
    staleTime: 30_000,
  });
}

// cont.69 Phase 3 (D7) — unified full document chain for the <DocumentChain> rail.
import type { ChainAnchorType, DocumentChain, PurchaseChain, PurchaseChainAnchorType } from './types';
export function useDocumentChain(type: ChainAnchorType, id: number | null) {
  return useQuery({
    queryKey: ['doc-chain', type, id],
    queryFn: () => apiGet<DocumentChain>(`documents/chain?type=${type}&id=${id}`),
    enabled: id != null && Number.isFinite(id) && id > 0,
    staleTime: 30_000,
  });
}

// F (Question-Backend36) — Purchase counterpart to useDocumentChain. One request,
// PO→VI→PV→WHT walked server-side. Replaces the 4–N detail-DTO hydration chain in
// PurchaseDocumentChain.tsx.
export function usePurchaseChain(type: PurchaseChainAnchorType, id: number | null) {
  return useQuery({
    queryKey: ['purchase-chain', type, id],
    queryFn: () => apiGet<PurchaseChain>(`documents/purchase-chain?type=${type}&id=${id}`),
    enabled: id != null && Number.isFinite(id) && id > 0,
    staleTime: 30_000,
  });
}

// Sprint 13j-FE D1 — document activity trail for the ActivityLog side rail.
import type { ActivityDocType, ActivityEntry } from './types';
export function useDocumentActivity(docType: ActivityDocType, id: number | null) {
  return useQuery({
    queryKey: ['activity', docType, id],
    queryFn: () => apiGet<ActivityEntry[]>(`${docType}/${id}/activity`),
    enabled: id != null && Number.isFinite(id) && id > 0,
    staleTime: 30_000,
  });
}

// ───────────── Sprint 13k — per-company RBAC admin (/settings/roles, /users) ─────────────
// All under /admin/rbac. companyId is OMITTED for company-admins (qs() drops undefined →
// the BE defaults to the caller's own company); super-admins pass the selected company.
// Query keys carry companyId so a super-admin's company switch refetches.
import type {
  PermissionCatalogItem,
  RoleListItem as RbacRoleListItem,
  RoleDetail as RbacRoleDetail,
  CreateRoleRequest as RbacCreateRoleRequest,
  UpdateRoleRequest as RbacUpdateRoleRequest,
  SetRolePermissionsRequest,
  RbacUserListItem,
  SetUserRolesRequest,
} from './types';

/** 66-item permission catalog — the source of permission labels (labelTh/labelEn). */
export function usePermissionCatalog() {
  return useQuery({
    queryKey: ['rbac-permissions'],
    queryFn: () => apiGet<PermissionCatalogItem[]>('admin/rbac/permissions'),
    staleTime: 60 * 60_000, // catalog is effectively static
  });
}

// `enabled` lets the super-admin pages defer the fetch until a company is picked
// (a companyId-less call would hit the BE with ambiguous scope on first render).
export function useRbacRoles(companyId?: number, enabled = true) {
  return useQuery({
    queryKey: ['rbac-roles', companyId ?? null],
    queryFn: () => apiGet<RbacRoleListItem[]>(`admin/rbac/roles${qs({ companyId })}`),
    enabled,
  });
}

export function useRbacRole(roleId: number | null) {
  return useQuery({
    queryKey: ['rbac-role', roleId],
    queryFn: () => apiGet<RbacRoleDetail>(`admin/rbac/roles/${roleId}`),
    enabled: roleId != null && Number.isFinite(roleId) && roleId > 0,
  });
}

export function useCreateRbacRole() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: RbacCreateRoleRequest) => apiPost<unknown>('admin/rbac/roles', req),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['rbac-roles'] }),
  });
}

export function useUpdateRbacRole() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (v: { id: number; req: RbacUpdateRoleRequest }) =>
      apiPut<void>(`admin/rbac/roles/${v.id}`, v.req),
    onSuccess: (_d, v) => {
      qc.invalidateQueries({ queryKey: ['rbac-roles'] });
      qc.invalidateQueries({ queryKey: ['rbac-role', v.id] });
    },
  });
}

export function useDeleteRbacRole() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => apiDelete<void>(`admin/rbac/roles/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['rbac-roles'] }),
  });
}

export function useSetRolePermissions() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (v: { id: number; req: SetRolePermissionsRequest }) =>
      apiPut<void>(`admin/rbac/roles/${v.id}/permissions`, v.req),
    onSuccess: (_d, v) => {
      qc.invalidateQueries({ queryKey: ['rbac-roles'] });
      qc.invalidateQueries({ queryKey: ['rbac-role', v.id] });
    },
  });
}

export function useRbacUsers(companyId?: number, enabled = true) {
  return useQuery({
    queryKey: ['rbac-users', companyId ?? null],
    queryFn: () => apiGet<RbacUserListItem[]>(`admin/rbac/users${qs({ companyId })}`),
    enabled,
  });
}

export function useSetUserRoles() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (v: { id: number; req: SetUserRolesRequest }) =>
      apiPut<void>(`admin/rbac/users/${v.id}/roles`, v.req),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['rbac-users'] }),
  });
}
