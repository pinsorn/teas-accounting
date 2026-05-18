namespace Accounting.Domain.Entities.Purchase;

/// <summary>
/// Settlement allocation: one Payment Voucher pays N Vendor Invoices (mirrors
/// Receipt→TaxInvoice applications). Simple case is 1:1 via
/// <c>payment_vouchers.vendor_invoice_id</c>; this table covers the many case.
/// PV-settles-VI GL/settled-amount roll-up is Sprint-6 wiring
/// (Answer-Sana-Question-Backend5-Followup §3) — the table ships now with the model.
/// </summary>
public class PaymentVoucherApplication
{
    public long ApplicationId { get; set; }
    public long PaymentVoucherId { get; set; }
    public long VendorInvoiceId  { get; set; }
    public decimal AppliedAmount { get; set; }
}
