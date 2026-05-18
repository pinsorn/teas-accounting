'use client';

import {
  useInfiniteQuery,
  useMutation,
  useQuery,
  useQueryClient,
} from '@tanstack/react-query';
import { apiGet, apiPost, apiPut, apiDelete, qs } from './api';
import type {
  CursorPage,
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
  ExpenseCategoryLite,
  PaymentVoucherListItem,
  PaymentVoucherDetail,
  WhtCertificateListItem,
  WhtCertificateDetail,
  WhtTypeListItem,
  WhtBaseSuggestion,
  WhtReceivableRegister,
  WhtReceivableAging,
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
  ProductListItem,
  ProductDetail,
  QuotationListItem,
  QuotationDetail,
  SalesOrderListItem,
  SalesOrderDetail,
  DeliveryOrderListItem,
  DeliveryOrderDetail,
  VendorInvoiceListItem,
  VendorInvoiceDetail,
  CreateVendorInvoiceRequest,
  VendorInvoicePostedResult,
  PaymentVoucherApprovedResult,
  AttachmentItem,
  PurchaseOrderListItem,
  PurchaseOrderDetail,
  OutstandingPoReport,
} from './types';

export interface TaxInvoiceFilters {
  dateFrom?: string;
  dateTo?: string;
  customerId?: number;
  status?: string;
  businessUnitId?: number;
  includeUnspecified?: boolean;
}

export function useTaxInvoices(filters: TaxInvoiceFilters) {
  return useInfiniteQuery({
    queryKey: ['tax-invoices', filters],
    initialPageParam: undefined as number | undefined,
    queryFn: ({ pageParam }) =>
      apiGet<CursorPage<TaxInvoiceListItem>>(
        `tax-invoices${qs({ ...filters, cursor: pageParam, limit: 25 })}`,
      ),
    getNextPageParam: (last) => last.nextCursor ?? undefined,
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

export function useReceipts(businessUnitId?: number, includeUnspecified?: boolean) {
  return useInfiniteQuery({
    queryKey: ['receipts', businessUnitId, includeUnspecified],
    initialPageParam: undefined as number | undefined,
    queryFn: ({ pageParam }) =>
      apiGet<CursorPage<ReceiptListItem>>(`receipts${qs({
        cursor: pageParam, limit: 25, businessUnitId,
        includeUnspecified: includeUnspecified || undefined,
      })}`),
    getNextPageParam: (l) => l.nextCursor ?? undefined,
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
    mutationFn: (req: unknown) => apiPost<{ receipt_id: number }>('receipts/', req),
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
  return useInfiniteQuery({
    queryKey: ['adjustment-notes', noteType, businessUnitId, includeUnspecified],
    initialPageParam: undefined as number | undefined,
    queryFn: ({ pageParam }) =>
      apiGet<CursorPage<AdjustmentNoteListItem>>(
        `tax-adjustment-notes${qs({
          noteType, cursor: pageParam, limit: 25, businessUnitId,
          includeUnspecified: includeUnspecified || undefined,
        })}`),
    getNextPageParam: (l) => l.nextCursor ?? undefined,
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
    mutationFn: (req: unknown) => apiPost<{ note_id: number }>('tax-adjustment-notes/', req),
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
    mutationFn: (req: CreateVendorRequest) => apiPost<unknown>('vendors/', req),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['vendors'] }),
  });
}

export function useExpenseCategories() {
  return useQuery({
    queryKey: ['expense-categories'],
    queryFn: () => apiGet<ExpenseCategoryLite[]>('expense-categories'),
  });
}

export function usePaymentVouchers() {
  return useInfiniteQuery({
    queryKey: ['payment-vouchers'],
    initialPageParam: undefined as number | undefined,
    queryFn: ({ pageParam }) =>
      apiGet<CursorPage<PaymentVoucherListItem>>(
        `payment-vouchers${qs({ cursor: pageParam, limit: 25 })}`),
    getNextPageParam: (l) => l.nextCursor ?? undefined,
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
  return useInfiniteQuery({
    queryKey: ['wht-certificates'],
    initialPageParam: undefined as number | undefined,
    queryFn: ({ pageParam }) =>
      apiGet<CursorPage<WhtCertificateListItem>>(
        `wht-certificates${qs({ cursor: pageParam, limit: 25 })}`),
    getNextPageParam: (l) => l.nextCursor ?? undefined,
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
export function useVendorInvoices() {
  return useInfiniteQuery({
    queryKey: ['vendor-invoices'],
    initialPageParam: undefined as number | undefined,
    queryFn: ({ pageParam }) =>
      apiGet<CursorPage<VendorInvoiceListItem>>(
        `vendor-invoices${qs({ cursor: pageParam, limit: 25 })}`),
    getNextPageParam: (l) => l.nextCursor ?? undefined,
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
    mutationFn: (req: CreateBusinessUnitRequest) => apiPost<unknown>('business-units/', req),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['business-units'] }),
  });
}
export function useUpdateBusinessUnit() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (v: { id: number; req: UpdateBusinessUnitRequest }) =>
      apiPut<unknown>(`business-units/${v.id}`, v.req),
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
export function useChangeWhtRate() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, newRate, effectiveFrom }: {
      id: number; newRate: number; effectiveFrom: string;
    }) => apiPost<unknown>(`wht-types/${id}/change-rate`, { newRate, effectiveFrom }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['wht-types'] }),
  });
}
export function useWhtBaseSuggest(taxInvoiceIds: number[], customerId: number) {
  return useQuery({
    queryKey: ['wht-base-suggest', taxInvoiceIds, customerId],
    enabled: taxInvoiceIds.length > 0 && customerId > 0,
    queryFn: () => apiGet<WhtBaseSuggestion>(
      `receipts/wht-base-suggest${qs({
        taxInvoiceIds: taxInvoiceIds.join(','), customerId,
      })}`),
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
export function useProducts(includeInactive = false, search?: string) {
  return useQuery({
    queryKey: ['products', includeInactive, search ?? ''],
    queryFn: () => apiGet<ProductListItem[]>(
      `products${qs({ includeInactive: includeInactive || undefined, search: search || undefined })}`),
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
    mutationFn: (req: unknown) => apiPost<{ product_id: number }>('products/', req),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['products'] }),
  });
}
export function useUpdateProduct() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (v: { id: number; req: unknown }) => apiPut(`products/${v.id}`, v.req),
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
    mutationFn: (req: unknown) => apiPost<{ quotation_id: number }>('quotations/', req),
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
export function useCreateDeliveryOrder() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (v: { soId: number; req: unknown }) =>
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
    mutationFn: (req: unknown) => apiPost<{ purchase_order_id: number }>('purchase-orders/', req),
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
