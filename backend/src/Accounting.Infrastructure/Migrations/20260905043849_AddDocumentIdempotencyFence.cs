using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentIdempotencyFence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "created_via_api_key_id",
                schema: "sales",
                table: "tax_invoices",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "idempotency_key",
                schema: "sales",
                table: "tax_invoices",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "idempotency_request_hash",
                schema: "sales",
                table: "tax_invoices",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "created_via_api_key_id",
                schema: "sales",
                table: "receipts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "idempotency_key",
                schema: "sales",
                table: "receipts",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "idempotency_request_hash",
                schema: "sales",
                table: "receipts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "created_via_api_key_id",
                schema: "sales",
                table: "quotations",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "idempotency_key",
                schema: "sales",
                table: "quotations",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "idempotency_request_hash",
                schema: "sales",
                table: "quotations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_tax_invoices_idem",
                schema: "sales",
                table: "tax_invoices",
                columns: new[] { "company_id", "created_via_api_key_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_receipts_idem",
                schema: "sales",
                table: "receipts",
                columns: new[] { "company_id", "created_via_api_key_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_quotations_idem",
                schema: "sales",
                table: "quotations",
                columns: new[] { "company_id", "created_via_api_key_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_tax_invoices_idem",
                schema: "sales",
                table: "tax_invoices");

            migrationBuilder.DropIndex(
                name: "ux_receipts_idem",
                schema: "sales",
                table: "receipts");

            migrationBuilder.DropIndex(
                name: "ux_quotations_idem",
                schema: "sales",
                table: "quotations");

            migrationBuilder.DropColumn(
                name: "created_via_api_key_id",
                schema: "sales",
                table: "tax_invoices");

            migrationBuilder.DropColumn(
                name: "idempotency_key",
                schema: "sales",
                table: "tax_invoices");

            migrationBuilder.DropColumn(
                name: "idempotency_request_hash",
                schema: "sales",
                table: "tax_invoices");

            migrationBuilder.DropColumn(
                name: "created_via_api_key_id",
                schema: "sales",
                table: "receipts");

            migrationBuilder.DropColumn(
                name: "idempotency_key",
                schema: "sales",
                table: "receipts");

            migrationBuilder.DropColumn(
                name: "idempotency_request_hash",
                schema: "sales",
                table: "receipts");

            migrationBuilder.DropColumn(
                name: "created_via_api_key_id",
                schema: "sales",
                table: "quotations");

            migrationBuilder.DropColumn(
                name: "idempotency_key",
                schema: "sales",
                table: "quotations");

            migrationBuilder.DropColumn(
                name: "idempotency_request_hash",
                schema: "sales",
                table: "quotations");
        }
    }
}
