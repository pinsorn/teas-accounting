using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLineItemProductTypeSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "product_type",
                schema: "sales",
                table: "tax_invoice_lines",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "product_type",
                schema: "sales",
                table: "sales_order_lines",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "product_type",
                schema: "sales",
                table: "quotation_lines",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "product_type",
                schema: "sales",
                table: "delivery_order_lines",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            // Sprint 13h P7 — backfill existing line rows to GOOD per Answer-26 pattern.
            // Pre-flight COUNT logged for ops visibility; backfill defaults to GOOD
            // (the conservative non-WHT classification — service lines must be
            // explicitly re-tagged by re-issuing the document if needed).
            migrationBuilder.Sql(@"
                DO $migrate_p7$
                DECLARE
                    n_q  int; n_so int; n_do int; n_ti int;
                BEGIN
                    SELECT COUNT(*) INTO n_q  FROM sales.quotation_lines       WHERE product_type IS NULL;
                    SELECT COUNT(*) INTO n_so FROM sales.sales_order_lines     WHERE product_type IS NULL;
                    SELECT COUNT(*) INTO n_do FROM sales.delivery_order_lines  WHERE product_type IS NULL;
                    SELECT COUNT(*) INTO n_ti FROM sales.tax_invoice_lines     WHERE product_type IS NULL;
                    RAISE NOTICE 'P7 backfill: q=%, so=%, do=%, ti=%', n_q, n_so, n_do, n_ti;
                    UPDATE sales.quotation_lines      SET product_type = 'GOOD' WHERE product_type IS NULL;
                    UPDATE sales.sales_order_lines    SET product_type = 'GOOD' WHERE product_type IS NULL;
                    UPDATE sales.delivery_order_lines SET product_type = 'GOOD' WHERE product_type IS NULL;
                    UPDATE sales.tax_invoice_lines    SET product_type = 'GOOD' WHERE product_type IS NULL;
                END $migrate_p7$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "product_type",
                schema: "sales",
                table: "tax_invoice_lines");

            migrationBuilder.DropColumn(
                name: "product_type",
                schema: "sales",
                table: "sales_order_lines");

            migrationBuilder.DropColumn(
                name: "product_type",
                schema: "sales",
                table: "quotation_lines");

            migrationBuilder.DropColumn(
                name: "product_type",
                schema: "sales",
                table: "delivery_order_lines");
        }
    }
}
