using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Accounting.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReceiptWhtAndNonVatBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_receipts_wht_type",
                schema: "sales",
                table: "receipts");

            migrationBuilder.DropIndex(
                name: "ix_receipt_applications_receipt_id_tax_invoice_id",
                schema: "sales",
                table: "receipt_applications");

            migrationBuilder.AlterColumn<long>(
                name: "tax_invoice_id",
                schema: "sales",
                table: "receipt_applications",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<long>(
                name: "delivery_order_id",
                schema: "sales",
                table: "receipt_applications",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "receipt_lines",
                schema: "sales",
                columns: table => new
                {
                    receipt_line_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    receipt_id = table.Column<long>(type: "bigint", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    product_id = table.Column<long>(type: "bigint", nullable: true),
                    product_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    product_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "GOOD"),
                    description_th = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    uom_text = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    unit_price = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_receipt_lines", x => x.receipt_line_id);
                    table.CheckConstraint("ck_receipt_lines_nonneg", "amount >= 0");
                    table.ForeignKey(
                        name: "fk_receipt_lines_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "master",
                        principalTable: "products",
                        principalColumn: "product_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_receipt_lines_receipts_receipt_id",
                        column: x => x.receipt_id,
                        principalSchema: "sales",
                        principalTable: "receipts",
                        principalColumn: "receipt_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "receipt_wht_lines",
                schema: "sales",
                columns: table => new
                {
                    receipt_wht_line_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    receipt_id = table.Column<long>(type: "bigint", nullable: false),
                    wht_type_id = table.Column<int>(type: "integer", nullable: false),
                    income_type_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    wht_type_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    wht_rate = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    base_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    wht_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_receipt_wht_lines", x => x.receipt_wht_line_id);
                    table.CheckConstraint("ck_receipt_wht_lines_nonneg", "base_amount >= 0 AND wht_amount >= 0");
                    table.ForeignKey(
                        name: "fk_receipt_wht_lines_receipts_receipt_id",
                        column: x => x.receipt_id,
                        principalSchema: "sales",
                        principalTable: "receipts",
                        principalColumn: "receipt_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_receipt_wht_lines_wht_types_wht_type_id",
                        column: x => x.wht_type_id,
                        principalSchema: "tax",
                        principalTable: "wht_types",
                        principalColumn: "wht_type_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_receipt_applications_delivery_order_id",
                schema: "sales",
                table: "receipt_applications",
                column: "delivery_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_receipt_applications_receipt_id_delivery_order_id",
                schema: "sales",
                table: "receipt_applications",
                columns: new[] { "receipt_id", "delivery_order_id" },
                unique: true,
                filter: "delivery_order_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_receipt_applications_receipt_id_tax_invoice_id",
                schema: "sales",
                table: "receipt_applications",
                columns: new[] { "receipt_id", "tax_invoice_id" },
                unique: true,
                filter: "tax_invoice_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_receipt_applications_one_doc",
                schema: "sales",
                table: "receipt_applications",
                sql: "(tax_invoice_id IS NOT NULL) <> (delivery_order_id IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_receipt_lines_product_id",
                schema: "sales",
                table: "receipt_lines",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_receipt_lines_receipt_id",
                schema: "sales",
                table: "receipt_lines",
                column: "receipt_id");

            migrationBuilder.CreateIndex(
                name: "ix_receipt_wht_lines_receipt_id",
                schema: "sales",
                table: "receipt_wht_lines",
                column: "receipt_id");

            migrationBuilder.CreateIndex(
                name: "ix_receipt_wht_lines_wht_type_id",
                schema: "sales",
                table: "receipt_wht_lines",
                column: "wht_type_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "receipt_lines",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "receipt_wht_lines",
                schema: "sales");

            migrationBuilder.DropIndex(
                name: "ix_receipt_applications_delivery_order_id",
                schema: "sales",
                table: "receipt_applications");

            migrationBuilder.DropIndex(
                name: "ix_receipt_applications_receipt_id_delivery_order_id",
                schema: "sales",
                table: "receipt_applications");

            migrationBuilder.DropIndex(
                name: "ix_receipt_applications_receipt_id_tax_invoice_id",
                schema: "sales",
                table: "receipt_applications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_receipt_applications_one_doc",
                schema: "sales",
                table: "receipt_applications");

            migrationBuilder.DropColumn(
                name: "delivery_order_id",
                schema: "sales",
                table: "receipt_applications");

            migrationBuilder.AlterColumn<long>(
                name: "tax_invoice_id",
                schema: "sales",
                table: "receipt_applications",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_receipts_wht_type",
                schema: "sales",
                table: "receipts",
                sql: "(wht_amount = 0 AND wht_type_id IS NULL) OR (wht_amount > 0 AND wht_type_id IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_receipt_applications_receipt_id_tax_invoice_id",
                schema: "sales",
                table: "receipt_applications",
                columns: new[] { "receipt_id", "tax_invoice_id" },
                unique: true);
        }
    }
}
