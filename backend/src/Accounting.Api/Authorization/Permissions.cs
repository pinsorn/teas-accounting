namespace Accounting.Api.Authorization;

/// <summary>Canonical permission codes. Mirror to <c>sys.permissions</c> via seed migration.</summary>
public static class Permissions
{
    public static class Master
    {
        public const string CompanyManage     = "master.company.manage";
        public const string BranchManage      = "master.branch.manage";
        public const string CustomerManage    = "master.customer.manage";
        public const string CustomerRead      = "master.customer.read";    // Sprint 13h P1
        public const string VendorManage      = "master.vendor.manage";
        public const string CoaManage         = "master.coa.manage";
        public const string BusinessUnitManage = "master.business_unit.manage";
        public const string ProductManage      = "master.product.manage";  // Sprint 10
        public const string ProductRead        = "master.product.read";    // Sprint 10
    }

    public static class Sys
    {
        public const string UserManage         = "sys.user.manage";
        public const string RoleManage         = "sys.role.manage";
        public const string DocPrefixManage    = "sys.doc_prefix.manage";
        public const string ExpenseCatManage   = "sys.expense_category.manage";
        // BP-01 (RV2) — read-only listing so PV/VI-creating roles (ap_clerk etc.) can
        // populate the expense-category picker without the full manage grant.
        public const string ExpenseCatRead     = "sys.expense_category.read";
        // Sprint 11 — file attachments.
        public const string AttachmentUpload   = "sys.attachment.upload";
        public const string AttachmentRead     = "sys.attachment.read";
        public const string AttachmentDelete   = "sys.attachment.delete";
        // Sprint 14 — external API key management (admins only).
        public const string ApiKeyManage       = "sys.api_key.manage";
    }

    public static class Gl
    {
        public const string JournalCreate  = "gl.journal.create";
        public const string JournalPost    = "gl.journal.post";
        public const string JournalRead    = "gl.journal.read";
        public const string PeriodClose    = "gl.period.close";
    }

    public static class Sales
    {
        public const string TaxInvoiceCreate = "sales.tax_invoice.create";
        public const string TaxInvoicePost   = "sales.tax_invoice.post";
        public const string TaxInvoiceRead   = "sales.tax_invoice.read";
        public const string ReceiptCreate    = "sales.receipt.create";
        public const string ReceiptPost      = "sales.receipt.post";
        public const string ReceiptRead      = "sales.receipt.read";       // Sprint 13i B1
        public const string CreditNoteCreate = "sales.credit_note.create";
        public const string CreditNotePost   = "sales.credit_note.post";
        public const string CreditNoteRead   = "sales.credit_note.read";   // Sprint 13i B1
        public const string DebitNoteCreate  = "sales.debit_note.create";
        public const string DebitNotePost    = "sales.debit_note.post";
        public const string DebitNoteRead    = "sales.debit_note.read";    // Sprint 13i B1
        // Sprint 10 — Q→SO→DO chain (manage = create/transition; read covered by manage).
        public const string QuotationManage     = "sales.quotation.manage";
        public const string SalesOrderManage    = "sales.sales_order.manage";
        public const string DeliveryOrderManage = "sales.delivery_order.manage";
        // Sprint 13h P6.2 — Billing Note (ใบแจ้งหนี้/ใบวางบิล).
        public const string BillingNoteRead     = "sales.billing_note.read";
        public const string BillingNoteManage   = "sales.billing_note.manage";
    }

    public static class Purchase
    {
        public const string PaymentVoucherCreate  = "purchase.payment_voucher.create";
        public const string PaymentVoucherApprove = "purchase.payment_voucher.approve";
        public const string PaymentVoucherPost    = "purchase.payment_voucher.post";
        public const string PaymentVoucherRead    = "purchase.payment_voucher.read";
        public const string WhtRead               = "purchase.wht.read";
        public const string VendorInvoiceCreate   = "purchase.vendor_invoice.create";
        public const string VendorInvoicePost     = "purchase.vendor_invoice.post";
        public const string VendorInvoiceRead     = "purchase.vendor_invoice.read";
        // Sprint 12 — internal Purchase Order.
        public const string PurchaseOrderCreate   = "purchase.purchase_order.create";
        public const string PurchaseOrderApprove  = "purchase.purchase_order.approve";
        public const string PurchaseOrderRead     = "purchase.purchase_order.read";
        public const string PurchaseOrderCancel   = "purchase.purchase_order.cancel";
    }

    public static class Tax
    {
        public const string VatRegisterRead = "tax.vat_register.read";
        public const string Pnd30Read       = "tax.pnd30.read";
        public const string Pnd3Read        = "tax.pnd3.read";
        public const string Pnd53Read       = "tax.pnd53.read";
        public const string WhtTypeManage   = "tax.wht_type.manage";  // Sprint 8.6
        // Sprint 9 — tax-filing lifecycle (built Part B, reused by Part C C7).
        public const string FilingPreview   = "tax.filing.preview";
        public const string FilingFinalize  = "tax.filing.finalize";
        public const string FilingRead      = "tax.filing.read";
    }

    public static class Report
    {
        public const string TrialBalance = "report.trial_balance.read";
        public const string ProfitLoss   = "report.profit_loss.read";
        public const string AuditRead    = "report.audit.read";
    }

    /// <summary>All permission codes — for seed migration.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Master.CompanyManage, Master.BranchManage, Master.CustomerManage, Master.CustomerRead, Master.VendorManage,
        Master.CoaManage, Master.BusinessUnitManage,
        Master.ProductManage, Master.ProductRead,
        Sys.UserManage, Sys.RoleManage, Sys.DocPrefixManage, Sys.ExpenseCatManage, Sys.ExpenseCatRead,
        Sys.AttachmentUpload, Sys.AttachmentRead, Sys.AttachmentDelete, Sys.ApiKeyManage,
        Gl.JournalCreate, Gl.JournalPost, Gl.JournalRead, Gl.PeriodClose,
        Sales.TaxInvoiceCreate, Sales.TaxInvoicePost, Sales.TaxInvoiceRead,
        Sales.ReceiptCreate, Sales.ReceiptPost, Sales.ReceiptRead,
        Sales.CreditNoteCreate, Sales.CreditNotePost, Sales.CreditNoteRead,
        Sales.DebitNoteCreate, Sales.DebitNotePost, Sales.DebitNoteRead,
        Sales.QuotationManage, Sales.SalesOrderManage, Sales.DeliveryOrderManage,
        Sales.BillingNoteRead, Sales.BillingNoteManage,
        Purchase.PaymentVoucherCreate, Purchase.PaymentVoucherApprove, Purchase.PaymentVoucherPost,
        Purchase.PaymentVoucherRead, Purchase.WhtRead,
        Purchase.VendorInvoiceCreate, Purchase.VendorInvoicePost, Purchase.VendorInvoiceRead,
        Purchase.PurchaseOrderCreate, Purchase.PurchaseOrderApprove,
        Purchase.PurchaseOrderRead, Purchase.PurchaseOrderCancel,
        Tax.VatRegisterRead, Tax.Pnd30Read, Tax.Pnd3Read, Tax.Pnd53Read, Tax.WhtTypeManage,
        Tax.FilingPreview, Tax.FilingFinalize, Tax.FilingRead,
        Report.TrialBalance, Report.ProfitLoss, Report.AuditRead,
    ];
}
