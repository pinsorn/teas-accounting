using Accounting.Application.Identity;

namespace Accounting.Api.Authorization;

/// <summary>
/// Sprint 13k — the SINGLE source of the bilingual labels for the 66 permission codes
/// in <see cref="Permissions.All"/>. The role-editor frontend renders these directly
/// (it does NOT re-translate). Module is derived from the code prefix. A unit test
/// asserts the catalog count == Permissions.All.Count so the two can never drift.
/// </summary>
public static class PermissionCatalog
{
    private static readonly Lazy<IReadOnlyList<PermissionCatalogItem>> _items = new(BuildItems);

    /// <summary>Built once (lazily, so the Labels field is initialized first); ordered by
    /// module then code for a stable, groupable UI.</summary>
    public static IReadOnlyList<PermissionCatalogItem> Items => _items.Value;

    private static IReadOnlyList<PermissionCatalogItem> BuildItems() =>
        Permissions.All
            .Select(code =>
            {
                var (th, en) = Labels.TryGetValue(code, out var l) ? l : (code, code);
                return new PermissionCatalogItem(code, ModuleOf(code), th, en);
            })
            .OrderBy(i => i.Module, StringComparer.Ordinal)
            .ThenBy(i => i.Code, StringComparer.Ordinal)
            .ToList();

    /// <summary>Module = the code prefix before the first dot.</summary>
    private static string ModuleOf(string code)
    {
        var dot = code.IndexOf('.');
        return dot > 0 ? code[..dot] : code;
    }

    // Bilingual labels keyed by code. Thai primary, English secondary.
    private static readonly IReadOnlyDictionary<string, (string Th, string En)> Labels =
        new Dictionary<string, (string, string)>
        {
            // master
            [Permissions.Master.CompanyManage]      = ("จัดการข้อมูลบริษัท (ภาษี/VAT)", "Manage company (tax/VAT)"),
            [Permissions.Master.CompanyProfileManage] = ("จัดการโปรไฟล์บริษัท (ที่อยู่/โลโก้)", "Manage company profile (address/logo)"),
            [Permissions.Master.BranchManage]       = ("จัดการสาขา", "Manage branches"),
            [Permissions.Master.CustomerManage]     = ("จัดการลูกค้า", "Manage customers"),
            [Permissions.Master.CustomerRead]       = ("ดูข้อมูลลูกค้า", "View customers"),
            [Permissions.Master.VendorManage]       = ("จัดการผู้ขาย/เจ้าหนี้", "Manage vendors"),
            [Permissions.Master.CoaManage]          = ("จัดการผังบัญชี", "Manage chart of accounts"),
            [Permissions.Master.BusinessUnitManage] = ("จัดการหน่วยธุรกิจ", "Manage business units"),
            [Permissions.Master.ProductManage]      = ("จัดการสินค้า/บริการ", "Manage products"),
            [Permissions.Master.ProductRead]        = ("ดูสินค้า/บริการ", "View products"),
            [Permissions.Master.EmployeeManage]     = ("จัดการพนักงาน", "Manage employees"),

            // sys
            [Permissions.Sys.UserManage]        = ("จัดการผู้ใช้งาน", "Manage users"),
            [Permissions.Sys.RoleManage]        = ("จัดการบทบาทและสิทธิ์", "Manage roles & permissions"),
            [Permissions.Sys.DocPrefixManage]   = ("จัดการรูปแบบเลขที่เอกสาร", "Manage document prefixes"),
            [Permissions.Sys.ExpenseCatManage]  = ("จัดการหมวดค่าใช้จ่าย", "Manage expense categories"),
            [Permissions.Sys.ExpenseCatRead]    = ("ดูหมวดค่าใช้จ่าย", "View expense categories"),
            [Permissions.Sys.AttachmentUpload]  = ("อัปโหลดไฟล์แนบ", "Upload attachments"),
            [Permissions.Sys.AttachmentRead]    = ("ดูไฟล์แนบ", "View attachments"),
            [Permissions.Sys.AttachmentDelete]  = ("ลบไฟล์แนบ", "Delete attachments"),
            [Permissions.Sys.ApiKeyManage]      = ("จัดการ API key", "Manage API keys"),

            // gl
            [Permissions.Gl.JournalCreate] = ("สร้างใบสำคัญทั่วไป", "Create journal entries"),
            [Permissions.Gl.JournalPost]   = ("บันทึกบัญชีใบสำคัญ", "Post journal entries"),
            [Permissions.Gl.JournalRead]   = ("ดูใบสำคัญทั่วไป", "View journal entries"),
            [Permissions.Gl.PeriodClose]   = ("ปิดงวดบัญชี", "Close accounting period"),

            // sales
            [Permissions.Sales.TaxInvoiceCreate]    = ("สร้างใบกำกับภาษี", "Create tax invoices"),
            [Permissions.Sales.TaxInvoicePost]      = ("ออกใบกำกับภาษี (โพสต์)", "Post tax invoices"),
            [Permissions.Sales.TaxInvoiceRead]      = ("ดูใบกำกับภาษี", "View tax invoices"),
            [Permissions.Sales.ReceiptCreate]       = ("สร้างใบเสร็จรับเงิน", "Create receipts"),
            [Permissions.Sales.ReceiptPost]         = ("ออกใบเสร็จรับเงิน (โพสต์)", "Post receipts"),
            [Permissions.Sales.ReceiptRead]         = ("ดูใบเสร็จรับเงิน", "View receipts"),
            [Permissions.Sales.CreditNoteCreate]    = ("สร้างใบลดหนี้", "Create credit notes"),
            [Permissions.Sales.CreditNotePost]      = ("ออกใบลดหนี้ (โพสต์)", "Post credit notes"),
            [Permissions.Sales.CreditNoteRead]      = ("ดูใบลดหนี้", "View credit notes"),
            [Permissions.Sales.DebitNoteCreate]     = ("สร้างใบเพิ่มหนี้", "Create debit notes"),
            [Permissions.Sales.DebitNotePost]       = ("ออกใบเพิ่มหนี้ (โพสต์)", "Post debit notes"),
            [Permissions.Sales.DebitNoteRead]       = ("ดูใบเพิ่มหนี้", "View debit notes"),
            [Permissions.Sales.QuotationManage]     = ("จัดการใบเสนอราคา", "Manage quotations"),
            [Permissions.Sales.SalesOrderManage]    = ("จัดการใบสั่งขาย", "Manage sales orders"),
            [Permissions.Sales.DeliveryOrderManage] = ("จัดการใบส่งของ", "Manage delivery orders"),
            [Permissions.Sales.BillingNoteRead]     = ("ดูใบวางบิล/ใบแจ้งหนี้", "View billing notes"),
            [Permissions.Sales.BillingNoteManage]   = ("จัดการใบวางบิล/ใบแจ้งหนี้", "Manage billing notes"),

            // purchase
            [Permissions.Purchase.PaymentVoucherCreate]  = ("สร้างใบสำคัญจ่าย", "Create payment vouchers"),
            [Permissions.Purchase.PaymentVoucherApprove] = ("อนุมัติใบสำคัญจ่าย", "Approve payment vouchers"),
            [Permissions.Purchase.PaymentVoucherPost]    = ("บันทึกบัญชีใบสำคัญจ่าย", "Post payment vouchers"),
            [Permissions.Purchase.PaymentVoucherRead]    = ("ดูใบสำคัญจ่าย", "View payment vouchers"),
            [Permissions.Purchase.WhtRead]               = ("ดูภาษีหัก ณ ที่จ่าย", "View withholding tax"),
            [Permissions.Purchase.VendorInvoiceCreate]   = ("สร้างใบแจ้งหนี้ผู้ขาย", "Create vendor invoices"),
            [Permissions.Purchase.VendorInvoicePost]     = ("บันทึกใบแจ้งหนี้ผู้ขาย", "Post vendor invoices"),
            [Permissions.Purchase.VendorInvoiceRead]     = ("ดูใบแจ้งหนี้ผู้ขาย", "View vendor invoices"),
            [Permissions.Purchase.PurchaseOrderCreate]   = ("สร้างใบสั่งซื้อ", "Create purchase orders"),
            [Permissions.Purchase.PurchaseOrderApprove]  = ("อนุมัติใบสั่งซื้อ", "Approve purchase orders"),
            [Permissions.Purchase.PurchaseOrderRead]     = ("ดูใบสั่งซื้อ", "View purchase orders"),
            [Permissions.Purchase.PurchaseOrderCancel]   = ("ยกเลิกใบสั่งซื้อ", "Cancel purchase orders"),

            // tax
            [Permissions.Tax.VatRegisterRead] = ("ดูรายงานภาษีซื้อ-ขาย", "View VAT register"),
            [Permissions.Tax.Pnd30Read]       = ("ดูแบบ ภ.พ.30", "View P.P.30 (VAT return)"),
            [Permissions.Tax.Pnd3Read]        = ("ดูแบบ ภ.ง.ด.3", "View P.N.D.3"),
            [Permissions.Tax.Pnd53Read]       = ("ดูแบบ ภ.ง.ด.53", "View P.N.D.53"),
            [Permissions.Tax.WhtTypeManage]   = ("จัดการประเภทภาษีหัก ณ ที่จ่าย", "Manage WHT types"),
            [Permissions.Tax.FilingPreview]   = ("ดูตัวอย่างแบบยื่นภาษี", "Preview tax filings"),
            [Permissions.Tax.FilingFinalize]  = ("ยืนยันแบบยื่นภาษี", "Finalize tax filings"),
            [Permissions.Tax.FilingRead]      = ("ดูแบบยื่นภาษี", "View tax filings"),

            // payroll
            [Permissions.Payroll.RunManage] = ("จัดการรอบจ่ายเงินเดือน", "Manage payroll runs"),
            [Permissions.Payroll.RunPost]   = ("อนุมัติ/บันทึกบัญชีเงินเดือน", "Approve & post payroll"),
            [Permissions.Payroll.RunPay]    = ("จ่ายเงินเดือน", "Pay payroll"),

            // report
            [Permissions.Report.TrialBalance] = ("ดูงบทดลอง", "View trial balance"),
            [Permissions.Report.ProfitLoss]   = ("ดูงบกำไรขาดทุน", "View profit & loss"),
            [Permissions.Report.AuditRead]    = ("ดูบันทึกการตรวจสอบ (Audit log)", "View audit log"),
        };
}
