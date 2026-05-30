using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseLineProductType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "product_type",
                schema: "purchase",
                table: "vendor_invoice_lines",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "product_type",
                schema: "purchase",
                table: "payment_voucher_lines",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "product_type",
                schema: "purchase",
                table: "vendor_invoice_lines");

            migrationBuilder.DropColumn(
                name: "product_type",
                schema: "purchase",
                table: "payment_voucher_lines");
        }
    }
}
