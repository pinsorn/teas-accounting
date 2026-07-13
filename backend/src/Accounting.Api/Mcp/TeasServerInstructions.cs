namespace Accounting.Api.Mcp;

/// <summary>
/// mcp-document-chain (D6) — the split between the static, cross-company
/// <see cref="Text"/> (wired into <c>McpServerOptions.ServerInstructions</c> at the init
/// handshake — the stateless server has no tenant at registration, so it can never vary per
/// company) and the per-company dynamic <see cref="VatGuide"/>/<see cref="NonVatGuide"/>
/// markdown returned by the <c>get_workflow_guide</c> tool (§A5), which reads
/// <see cref="Accounting.Application.Abstractions.ICompanyTaxConfigService"/> at call time.
/// </summary>
public static class TeasServerInstructions
{
    /// <summary>Static, company-agnostic rules sent once at the MCP init handshake.</summary>
    public const string Text =
        "Before advancing any document chain, call get_workflow_guide for this company's " +
        "exact steps. taxRate is FRACTIONAL (0.07 = 7%, never 7). Every create tool returns " +
        "approvalLinkMarkdown — paste that markdown link verbatim so the human can approve; " +
        "the agent can NEVER post/approve. After sending an approval link, END the turn; next " +
        "turn call get_document_status to confirm the upstream doc reached its posted/approved " +
        "state BEFORE creating the next hop. BU may be required. Resolver tools: " +
        "list_customers/list_products/list_vendors/list_bank_accounts map names→ids.";

    /// <summary>§A5 — VAT-registered company sales-chain guide (Thai). §B addition (Ham
    /// 2026-07-13) — the BN (วางบิล) hop is OPTIONAL; the default path collects via a Tax
    /// Invoice directly (step 4), never requiring a BillingNote first.</summary>
    public const string VatGuide =
        """
        # ขั้นตอนเอกสารขาย (บริษัทจด VAT)
        1. สร้างใบเสนอราคา → create_quotation_draft → ส่งลิงก์ให้ผู้ใช้กด "ส่ง/อนุมัติ"
        2. เมื่อลูกค้าตอบรับ (Accepted) → create_sales_order_draft (ใส่ quotationId)
        3. ตรวจ get_document_status ว่า SO = Posted แล้ว → ถ้ามีสินค้า (deliveryRequired=true)
           สร้างใบส่งของ create_delivery_order_draft (ใส่ salesOrderId); ถ้าบริการล้วน ข้ามได้
        4. สร้าง "ใบกำกับภาษี" (ใบแจ้งหนี้ของบริษัท VAT) → create_invoice_draft
           (deliveryOrderId ถ้ามีของ / salesOrderId ถ้าบริการล้วน) — ระบบออกเป็นใบกำกับภาษี
           * ทางเลือก (Optional): ถ้าต้อง "วางบิล" ก่อน ให้ create_billing_note_draft
             (deliveryOrderId / salesOrderId) แล้วค่อย create_tax_invoice_draft (billingNoteId)
             — ปกติไม่จำเป็น ใช้ Tax Invoice ตรงตามขั้นตอนที่ 4 ก็เพียงพอแล้ว
        5. เมื่อผู้ใช้ post ใบกำกับภาษีแล้ว → รับชำระ create_receipt_draft (ใส่ invoiceId = id ใบกำกับภาษี)
           ระบบจะตัด AR ให้ (เดบิตเงินสด/ธนาคาร เครดิตลูกหนี้ 1130) ไม่รับรู้รายได้ซ้ำ
           ถ้าลูกค้าหัก ณ ที่จ่าย ให้แนบ WHT — ระบบเดบิต 1180
           ⚠️ ห้ามรับชำระกับใบแจ้งหนี้ (BillingNote) ตรงๆ สำหรับบริษัทจด VAT — ต้องออกใบกำกับภาษี
           จากใบแจ้งหนี้ก่อน แล้วรับชำระกับใบกำกับภาษีเท่านั้น (ระบบจะปฏิเสธพร้อมข้อความแนะนำ)
        * ทุกขั้น: วางลิงก์ approvalLinkMarkdown ให้ผู้ใช้กดอนุมัติ เอเจนต์ห้าม post เอง
        * taxRate เป็นเศษส่วน (0.07 = 7%)
        """;

    /// <summary>§A5 — non-VAT company sales-chain guide (Thai), ม.86/4 warning, no TI hop.</summary>
    public const string NonVatGuide =
        """
        # ขั้นตอนเอกสารขาย (บริษัทไม่จด VAT — ม.86/4)
        ⚠️ บริษัทนี้ไม่จด VAT จึงออก "ใบกำกับภาษี" ไม่ได้ (ม.86/4) — ใช้ "ใบแจ้งหนี้" แทน
        1. create_quotation_draft → ส่งอนุมัติ
        2. Accepted → create_sales_order_draft (quotationId)
        3. SO = Posted → มีสินค้า สร้าง create_delivery_order_draft (salesOrderId); บริการล้วน ข้ามได้
        4. สร้าง "ใบแจ้งหนี้" → create_invoice_draft (deliveryOrderId / salesOrderId) — ระบบออกเป็นใบแจ้งหนี้
           (create_billing_note_draft ให้ผลเหมือนกันทุกประการสำหรับบริษัทนี้ — ใช้ตัวใดตัวหนึ่งก็ได้)
        5. รับชำระ create_receipt_draft (ใส่ invoiceId = id ใบแจ้งหนี้) — ระบบรับรู้รายได้ตอนรับเงิน
           (เดบิตเงินสด/ธนาคาร เครดิตรายได้ 4000) ไม่มี VAT ขาย
        * วางลิงก์ approvalLinkMarkdown ทุกขั้น; taxRate = 0.07 (แต่บริษัทนี้ = 0)
        """;

    /// <summary>Purchase-side guide is identical for both VAT modes — appended after the sales
    /// guide by <c>get_workflow_guide</c>.</summary>
    public const string PurchaseGuide =
        "\n\n(Purchase guide is identical for both: create_purchase_order_draft → (approve) → " +
        "create_vendor_invoice_draft (purchaseOrderId) → (post) → create_payment_voucher_draft " +
        "(vendorInvoiceId))";
}
