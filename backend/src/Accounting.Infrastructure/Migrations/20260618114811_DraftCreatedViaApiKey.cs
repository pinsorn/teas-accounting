using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DraftCreatedViaApiKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "created_via_api_key_name",
                schema: "sales",
                table: "tax_invoices",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "created_via_api_key_name",
                schema: "sales",
                table: "receipts",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "created_via_api_key_name",
                schema: "sales",
                table: "quotations",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "created_via_api_key_name",
                schema: "sales",
                table: "tax_invoices");

            migrationBuilder.DropColumn(
                name: "created_via_api_key_name",
                schema: "sales",
                table: "receipts");

            migrationBuilder.DropColumn(
                name: "created_via_api_key_name",
                schema: "sales",
                table: "quotations");
        }
    }
}
