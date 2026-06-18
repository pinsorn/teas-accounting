using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PurchaseCreatedViaApiKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "created_via_api_key_name",
                schema: "purchase",
                table: "vendor_invoices",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "created_via_api_key_name",
                schema: "purchase",
                table: "purchase_orders",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "created_via_api_key_name",
                schema: "purchase",
                table: "payment_vouchers",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "created_via_api_key_name",
                schema: "purchase",
                table: "vendor_invoices");

            migrationBuilder.DropColumn(
                name: "created_via_api_key_name",
                schema: "purchase",
                table: "purchase_orders");

            migrationBuilder.DropColumn(
                name: "created_via_api_key_name",
                schema: "purchase",
                table: "payment_vouchers");
        }
    }
}
