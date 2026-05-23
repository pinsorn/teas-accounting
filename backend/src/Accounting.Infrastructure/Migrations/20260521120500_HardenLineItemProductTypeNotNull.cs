using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Accounting.Infrastructure.Persistence;

#nullable disable

namespace Accounting.Infrastructure.Migrations
{
    /// <summary>
    /// Sprint 13i C5 — harden product_type to NOT NULL across the 5 sales line tables.
    /// Backfill any residual NULLs to 'GOOD' first (idempotent + safe), then flip the
    /// column. Mirrors the Sprint 13h P7 snapshot column shape.
    /// </summary>
    [DbContext(typeof(AccountingDbContext))]
    [Migration("20260521120500_HardenLineItemProductTypeNotNull")]
    public partial class HardenLineItemProductTypeNotNull : Migration
    {
        private static readonly string[] LineTables =
        {
            "quotation_lines", "sales_order_lines", "delivery_order_lines",
            "tax_invoice_lines", "billing_note_lines",
        };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfill first — idempotent, safe to re-run.
            foreach (var t in LineTables)
                migrationBuilder.Sql(
                    $"UPDATE sales.{t} SET product_type = 'GOOD' WHERE product_type IS NULL;");

            foreach (var t in LineTables)
                migrationBuilder.AlterColumn<string>(
                    name: "product_type",
                    schema: "sales",
                    table: t,
                    type: "character varying(20)",
                    maxLength: 20,
                    nullable: false,
                    oldClrType: typeof(string),
                    oldType: "character varying(20)",
                    oldMaxLength: 20,
                    oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var t in LineTables)
                migrationBuilder.AlterColumn<string>(
                    name: "product_type",
                    schema: "sales",
                    table: t,
                    type: "character varying(20)",
                    maxLength: 20,
                    nullable: true,
                    oldClrType: typeof(string),
                    oldType: "character varying(20)",
                    oldMaxLength: 20,
                    oldNullable: false);
        }
    }
}
