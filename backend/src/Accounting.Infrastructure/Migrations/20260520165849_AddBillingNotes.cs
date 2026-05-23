using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Accounting.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "billing_notes",
                schema: "sales",
                columns: table => new
                {
                    billing_note_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    branch_id = table.Column<int>(type: "integer", nullable: false),
                    doc_no = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    doc_date = table.Column<DateOnly>(type: "date", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    customer_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    customer_address = table.Column<string>(type: "text", nullable: true),
                    customer_tax_id = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: true),
                    customer_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    business_unit_id = table.Column<int>(type: "integer", nullable: true),
                    quotation_id = table.Column<long>(type: "bigint", nullable: true),
                    tax_invoice_ids = table.Column<long[]>(type: "bigint[]", nullable: true),
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    exchange_rate = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: false),
                    subtotal_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    vat_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    internal_notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    cancelled_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    settled_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_billing_notes", x => x.billing_note_id);
                    table.ForeignKey(
                        name: "fk_billing_notes_quotations_quotation_id",
                        column: x => x.quotation_id,
                        principalSchema: "sales",
                        principalTable: "quotations",
                        principalColumn: "quotation_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "billing_note_lines",
                schema: "sales",
                columns: table => new
                {
                    line_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    billing_note_id = table.Column<long>(type: "bigint", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    product_id = table.Column<long>(type: "bigint", nullable: true),
                    product_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    product_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    tax_invoice_id = table.Column<long>(type: "bigint", nullable: true),
                    description_th = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    uom_text = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    discount_percent = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    discount_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    line_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    tax_code_id = table.Column<int>(type: "integer", nullable: false),
                    tax_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tax_rate = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    tax_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_billing_note_lines", x => x.line_id);
                    table.ForeignKey(
                        name: "fk_billing_note_lines_billing_notes_billing_note_id",
                        column: x => x.billing_note_id,
                        principalSchema: "sales",
                        principalTable: "billing_notes",
                        principalColumn: "billing_note_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_billing_note_lines_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "master",
                        principalTable: "products",
                        principalColumn: "product_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_billing_note_lines_tax_invoices_tax_invoice_id",
                        column: x => x.tax_invoice_id,
                        principalSchema: "sales",
                        principalTable: "tax_invoices",
                        principalColumn: "tax_invoice_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_billing_note_lines_billing_note_id_line_no",
                schema: "sales",
                table: "billing_note_lines",
                columns: new[] { "billing_note_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_billing_note_lines_product_id",
                schema: "sales",
                table: "billing_note_lines",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_billing_note_lines_tax_invoice_id",
                schema: "sales",
                table: "billing_note_lines",
                column: "tax_invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_billing_notes_company_id_doc_no",
                schema: "sales",
                table: "billing_notes",
                columns: new[] { "company_id", "doc_no" },
                unique: true,
                filter: "doc_no IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_billing_notes_quotation_id",
                schema: "sales",
                table: "billing_notes",
                column: "quotation_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "billing_note_lines",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "billing_notes",
                schema: "sales");
        }
    }
}
