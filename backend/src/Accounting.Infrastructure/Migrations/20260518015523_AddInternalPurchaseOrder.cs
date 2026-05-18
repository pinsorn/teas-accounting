using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Accounting.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInternalPurchaseOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "purchase_order_id",
                schema: "purchase",
                table: "vendor_invoices",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "purchase_orders",
                schema: "purchase",
                columns: table => new
                {
                    purchase_order_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    branch_id = table.Column<int>(type: "integer", nullable: false),
                    doc_no = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    doc_date = table.Column<DateOnly>(type: "date", nullable: false),
                    expected_delivery_date = table.Column<DateOnly>(type: "date", nullable: true),
                    vendor_id = table.Column<long>(type: "bigint", nullable: false),
                    vendor_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    vendor_address = table.Column<string>(type: "text", nullable: true),
                    vendor_tax_id = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: true),
                    vendor_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    business_unit_id = table.Column<int>(type: "integer", nullable: true),
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    exchange_rate = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: false),
                    subtotal_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    vat_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    total_amount_thb = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    internal_notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    approved_by = table.Column<long>(type: "bigint", nullable: true),
                    sent_to_vendor_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_orders", x => x.purchase_order_id);
                    table.CheckConstraint("ck_po_sod", "approved_by IS NULL OR approved_by <> created_by");
                });

            migrationBuilder.CreateTable(
                name: "purchase_order_lines",
                schema: "purchase",
                columns: table => new
                {
                    line_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    purchase_order_id = table.Column<long>(type: "bigint", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    product_id = table.Column<long>(type: "bigint", nullable: true),
                    product_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    description_th = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    uom_text = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    unit_price = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    line_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    tax_code_id = table.Column<int>(type: "integer", nullable: true),
                    tax_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    tax_rate = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    tax_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_order_lines", x => x.line_id);
                    table.ForeignKey(
                        name: "fk_purchase_order_lines_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "master",
                        principalTable: "products",
                        principalColumn: "product_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_order_lines_purchase_orders_purchase_order_id",
                        column: x => x.purchase_order_id,
                        principalSchema: "purchase",
                        principalTable: "purchase_orders",
                        principalColumn: "purchase_order_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_vendor_invoices_purchase_order_id",
                schema: "purchase",
                table: "vendor_invoices",
                column: "purchase_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_lines_product_id",
                schema: "purchase",
                table: "purchase_order_lines",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_lines_purchase_order_id_line_no",
                schema: "purchase",
                table: "purchase_order_lines",
                columns: new[] { "purchase_order_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_company_id_doc_no",
                schema: "purchase",
                table: "purchase_orders",
                columns: new[] { "company_id", "doc_no" },
                unique: true,
                filter: "doc_no IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_company_id_status",
                schema: "purchase",
                table: "purchase_orders",
                columns: new[] { "company_id", "status" });

            migrationBuilder.AddForeignKey(
                name: "fk_vendor_invoices_purchase_orders_purchase_order_id",
                schema: "purchase",
                table: "vendor_invoices",
                column: "purchase_order_id",
                principalSchema: "purchase",
                principalTable: "purchase_orders",
                principalColumn: "purchase_order_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_vendor_invoices_purchase_orders_purchase_order_id",
                schema: "purchase",
                table: "vendor_invoices");

            migrationBuilder.DropTable(
                name: "purchase_order_lines",
                schema: "purchase");

            migrationBuilder.DropTable(
                name: "purchase_orders",
                schema: "purchase");

            migrationBuilder.DropIndex(
                name: "ix_vendor_invoices_purchase_order_id",
                schema: "purchase",
                table: "vendor_invoices");

            migrationBuilder.DropColumn(
                name: "purchase_order_id",
                schema: "purchase",
                table: "vendor_invoices");
        }
    }
}
