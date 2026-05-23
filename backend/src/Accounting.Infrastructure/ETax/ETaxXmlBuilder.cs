using System.Globalization;
using System.Text;
using System.Xml;
using Accounting.Application.Abstractions;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Sales;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.ETax;

/// <summary>
/// Skeletal e-Tax XML for a Tax Invoice. Matches the field set required by ม.86/4 but is
/// NOT yet UBL 2.1 / RD-spec compliant — production deployment must extend the XSD-validated
/// envelope before XAdES-BES signing.
/// </summary>
public sealed class ETaxXmlBuilder : IETaxXmlBuilder
{
    private readonly AccountingDbContext _db;

    public ETaxXmlBuilder(AccountingDbContext db) => _db = db;

    public string BuildTaxInvoiceXml(long taxInvoiceId, CancellationToken ct)
    {
        var ti = _db.TaxInvoices.IgnoreQueryFilters()
            .Include(t => t.Lines)
            .FirstOrDefault(t => t.TaxInvoiceId == taxInvoiceId)
            ?? throw new DomainException("etax.ti_missing", $"Tax Invoice {taxInvoiceId} not found.");

        var sb = new StringBuilder();
        // Sprint 13h P11 — `using var` disposes at scope end, AFTER `return sb.ToString()`,
        // so the writer's internal buffer was never flushed → 0-byte file (Sana BUG).
        // Wrap the writer in an explicit block so disposal (and flush) happens before we
        // hand the StringBuilder back.
        using (var w = XmlWriter.Create(sb, new XmlWriterSettings
        {
            Indent = true,
            Encoding = Encoding.UTF8,
            OmitXmlDeclaration = false,
        }))
        {

        w.WriteStartDocument();
        w.WriteStartElement("Invoice", "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2");
        w.WriteAttributeString("xmlns", "cbc", null,
            "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2");
        w.WriteAttributeString("xmlns", "cac", null,
            "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2");

        WriteSimple(w, "cbc", "ID", ti.DocNo ?? "DRAFT");
        WriteSimple(w, "cbc", "IssueDate", ti.DocDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        WriteSimple(w, "cbc", "InvoiceTypeCode", "388");   // UBL invoice
        WriteSimple(w, "cbc", "DocumentCurrencyCode", ti.CurrencyCode);

        WriteParty(w, "AccountingSupplierParty", ti.SupplierName, ti.SupplierTaxId,
            ti.SupplierBranchCode, ti.SupplierAddress);
        WriteParty(w, "AccountingCustomerParty", ti.CustomerName, ti.CustomerTaxId,
            ti.CustomerBranchCode, ti.CustomerAddress);

        // Tax total
        w.WriteStartElement("cac", "TaxTotal", null);
        WriteMoney(w, "cbc", "TaxAmount", ti.TaxAmount, ti.CurrencyCode);
        w.WriteEndElement();

        // Monetary totals
        w.WriteStartElement("cac", "LegalMonetaryTotal", null);
        WriteMoney(w, "cbc", "LineExtensionAmount", ti.SubtotalAmount, ti.CurrencyCode);
        WriteMoney(w, "cbc", "TaxExclusiveAmount", ti.SubtotalAmount, ti.CurrencyCode);
        WriteMoney(w, "cbc", "TaxInclusiveAmount", ti.TotalAmount, ti.CurrencyCode);
        WriteMoney(w, "cbc", "PayableAmount",      ti.TotalAmount, ti.CurrencyCode);
        w.WriteEndElement();

        // Lines
        foreach (var l in ti.Lines.OrderBy(x => x.LineNo))
        {
            w.WriteStartElement("cac", "InvoiceLine", null);
            WriteSimple(w, "cbc", "ID", l.LineNo.ToString(CultureInfo.InvariantCulture));
            WriteSimple(w, "cbc", "InvoicedQuantity", l.Quantity.ToString(CultureInfo.InvariantCulture));
            WriteMoney(w, "cbc", "LineExtensionAmount", l.LineAmount, ti.CurrencyCode);

            w.WriteStartElement("cac", "Item", null);
            WriteSimple(w, "cbc", "Name", l.DescriptionTh);
            w.WriteEndElement();

            w.WriteStartElement("cac", "Price", null);
            WriteMoney(w, "cbc", "PriceAmount", l.UnitPrice, ti.CurrencyCode);
            w.WriteEndElement();

            w.WriteEndElement();
        }

        w.WriteEndElement(); // Invoice
        w.WriteEndDocument();
        }   // <-- end using(w): writer flushes + closes here, before sb is read.
        return sb.ToString();
    }

    private static void WriteSimple(XmlWriter w, string prefix, string name, string value)
    {
        w.WriteStartElement(prefix, name, null);
        w.WriteString(value);
        w.WriteEndElement();
    }

    private static void WriteMoney(XmlWriter w, string prefix, string name, decimal amount, string currency)
    {
        w.WriteStartElement(prefix, name, null);
        w.WriteAttributeString("currencyID", currency);
        w.WriteString(amount.ToString("F2", CultureInfo.InvariantCulture));
        w.WriteEndElement();
    }

    private static void WriteParty(XmlWriter w, string wrapper, string name, string? taxId,
        string? branchCode, string address)
    {
        w.WriteStartElement("cac", wrapper, null);
        w.WriteStartElement("cac", "Party", null);
        w.WriteStartElement("cac", "PartyIdentification", null);
        WriteSimple(w, "cbc", "ID", taxId ?? "0000000000000");
        w.WriteEndElement();
        WriteSimple(w, "cbc", "Name", name);
        if (!string.IsNullOrEmpty(branchCode))
            WriteSimple(w, "cbc", "BranchID", branchCode);
        w.WriteStartElement("cac", "PostalAddress", null);
        WriteSimple(w, "cbc", "StreetName", address);
        w.WriteEndElement();
        w.WriteEndElement(); // Party
        w.WriteEndElement(); // wrapper
    }
}
