import { APIRequestContext, expect } from '@playwright/test';

// VendorInvoiceService.PostAsync requires ≥1 non-deleted attachment under
// (VendorInvoice, viId) — the vendor's ใบกำกับภาษีซื้อ file is the ม.86/4 + ม.82/4
// audit evidence. Every e2e that posts a VI must call this between
// `/vendor-invoices/` POST and `/vendor-invoices/{id}/post` POST, or it gets
// 422 `vi.attachment_required`.
//
// The endpoint accepts multipart/form-data (parent_type=VendorInvoice,
// parent_id=<viId>, category=TAX_INVOICE, file). We send a tiny dummy PDF —
// the backend records the row + storage path, nothing parses the bytes for
// integration tests. The caller must already be authenticated with at least
// `sys.attachment.upload` (admin / ap_clerk both have it).
export async function attachVendorTaxInvoice(
  request: APIRequestContext, apiBase: string, viId: number,
): Promise<number> {
  // Minimal PDF magic header so MIME validation / future scanners don't reject.
  const pdf = Buffer.from('%PDF-1.4\n%%EOF\n', 'utf8');
  const res = await request.post(`${apiBase}/attachments/`, {
    multipart: {
      parent_type: 'VENDOR_INVOICE',
      parent_id: String(viId),
      category: 'TAX_INVOICE',
      file: { name: `vendor-ti-${viId}.pdf`, mimeType: 'application/pdf', buffer: pdf },
    },
  });
  expect(res.status(), 'attach vendor TI file').toBe(201);
  const body = await res.json();
  return body.attachmentId ?? body.attachment_id;
}
