using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Accounting.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProductMasterAndFk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "products",
                schema: "master",
                columns: table => new
                {
                    product_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    product_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name_th = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    name_en = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    product_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    default_uom_text = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    default_unit_price = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    default_output_tax_code_id = table.Column<int>(type: "integer", nullable: true),
                    default_input_tax_code_id = table.Column<int>(type: "integer", nullable: true),
                    default_wht_type_id = table.Column<int>(type: "integer", nullable: true),
                    description_th = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_products", x => x.product_id);
                    table.CheckConstraint("ck_products_type", "product_type IN ('GOOD','SERVICE','EXEMPT_GOOD','EXEMPT_SERVICE')");
                    table.ForeignKey(
                        name: "fk_products_tax_codes_default_input_tax_code_id",
                        column: x => x.default_input_tax_code_id,
                        principalSchema: "tax",
                        principalTable: "tax_codes",
                        principalColumn: "tax_code_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_products_tax_codes_default_output_tax_code_id",
                        column: x => x.default_output_tax_code_id,
                        principalSchema: "tax",
                        principalTable: "tax_codes",
                        principalColumn: "tax_code_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_products_wht_types_default_wht_type_id",
                        column: x => x.default_wht_type_id,
                        principalSchema: "tax",
                        principalTable: "wht_types",
                        principalColumn: "wht_type_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tax_invoice_lines_product_id",
                schema: "sales",
                table: "tax_invoice_lines",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_products_company_id_product_code",
                schema: "master",
                table: "products",
                columns: new[] { "company_id", "product_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_products_default_input_tax_code_id",
                schema: "master",
                table: "products",
                column: "default_input_tax_code_id");

            migrationBuilder.CreateIndex(
                name: "ix_products_default_output_tax_code_id",
                schema: "master",
                table: "products",
                column: "default_output_tax_code_id");

            migrationBuilder.CreateIndex(
                name: "ix_products_default_wht_type_id",
                schema: "master",
                table: "products",
                column: "default_wht_type_id");

            migrationBuilder.AddForeignKey(
                name: "fk_tax_invoice_lines_products_product_id",
                schema: "sales",
                table: "tax_invoice_lines",
                column: "product_id",
                principalSchema: "master",
                principalTable: "products",
                principalColumn: "product_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_tax_invoice_lines_products_product_id",
                schema: "sales",
                table: "tax_invoice_lines");

            migrationBuilder.DropTable(
                name: "products",
                schema: "master");

            migrationBuilder.DropIndex(
                name: "ix_tax_invoice_lines_product_id",
                schema: "sales",
                table: "tax_invoice_lines");
        }
    }
}
