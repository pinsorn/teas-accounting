using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class QuotationSingleInvoice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_tax_invoices_quotation_id",
                schema: "sales",
                table: "tax_invoices");

            migrationBuilder.CreateIndex(
                name: "ix_tax_invoices_quotation_id",
                schema: "sales",
                table: "tax_invoices",
                column: "quotation_id",
                unique: true,
                filter: "quotation_id IS NOT NULL AND status = 'POSTED'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_tax_invoices_quotation_id",
                schema: "sales",
                table: "tax_invoices");

            migrationBuilder.CreateIndex(
                name: "ix_tax_invoices_quotation_id",
                schema: "sales",
                table: "tax_invoices",
                column: "quotation_id",
                filter: "quotation_id IS NOT NULL");
        }
    }
}
