using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTaxInvoiceQuotationReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "quotation_id",
                schema: "sales",
                table: "tax_invoices",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_tax_invoices_quotation_id",
                schema: "sales",
                table: "tax_invoices",
                column: "quotation_id",
                filter: "quotation_id IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_tax_invoices_quotations_quotation_id",
                schema: "sales",
                table: "tax_invoices",
                column: "quotation_id",
                principalSchema: "sales",
                principalTable: "quotations",
                principalColumn: "quotation_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_tax_invoices_quotations_quotation_id",
                schema: "sales",
                table: "tax_invoices");

            migrationBuilder.DropIndex(
                name: "ix_tax_invoices_quotation_id",
                schema: "sales",
                table: "tax_invoices");

            migrationBuilder.DropColumn(
                name: "quotation_id",
                schema: "sales",
                table: "tax_invoices");
        }
    }
}
