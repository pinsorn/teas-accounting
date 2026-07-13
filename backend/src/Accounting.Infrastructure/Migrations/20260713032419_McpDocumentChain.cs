using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class McpDocumentChain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "delivery_order_id",
                schema: "sales",
                table: "tax_invoices",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "sales_order_id",
                schema: "sales",
                table: "tax_invoices",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "sales_order_id",
                schema: "sales",
                table: "billing_notes",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_tax_invoices_delivery_order_id",
                schema: "sales",
                table: "tax_invoices",
                column: "delivery_order_id",
                filter: "delivery_order_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_tax_invoices_sales_order_id",
                schema: "sales",
                table: "tax_invoices",
                column: "sales_order_id",
                filter: "sales_order_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_billing_notes_sales_order_id",
                schema: "sales",
                table: "billing_notes",
                column: "sales_order_id",
                filter: "sales_order_id IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_billing_notes_sales_orders_sales_order_id",
                schema: "sales",
                table: "billing_notes",
                column: "sales_order_id",
                principalSchema: "sales",
                principalTable: "sales_orders",
                principalColumn: "sales_order_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_tax_invoices_delivery_orders_delivery_order_id",
                schema: "sales",
                table: "tax_invoices",
                column: "delivery_order_id",
                principalSchema: "sales",
                principalTable: "delivery_orders",
                principalColumn: "delivery_order_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_tax_invoices_sales_orders_sales_order_id",
                schema: "sales",
                table: "tax_invoices",
                column: "sales_order_id",
                principalSchema: "sales",
                principalTable: "sales_orders",
                principalColumn: "sales_order_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_billing_notes_sales_orders_sales_order_id",
                schema: "sales",
                table: "billing_notes");

            migrationBuilder.DropForeignKey(
                name: "fk_tax_invoices_delivery_orders_delivery_order_id",
                schema: "sales",
                table: "tax_invoices");

            migrationBuilder.DropForeignKey(
                name: "fk_tax_invoices_sales_orders_sales_order_id",
                schema: "sales",
                table: "tax_invoices");

            migrationBuilder.DropIndex(
                name: "ix_tax_invoices_delivery_order_id",
                schema: "sales",
                table: "tax_invoices");

            migrationBuilder.DropIndex(
                name: "ix_tax_invoices_sales_order_id",
                schema: "sales",
                table: "tax_invoices");

            migrationBuilder.DropIndex(
                name: "ix_billing_notes_sales_order_id",
                schema: "sales",
                table: "billing_notes");

            migrationBuilder.DropColumn(
                name: "delivery_order_id",
                schema: "sales",
                table: "tax_invoices");

            migrationBuilder.DropColumn(
                name: "sales_order_id",
                schema: "sales",
                table: "tax_invoices");

            migrationBuilder.DropColumn(
                name: "sales_order_id",
                schema: "sales",
                table: "billing_notes");
        }
    }
}
