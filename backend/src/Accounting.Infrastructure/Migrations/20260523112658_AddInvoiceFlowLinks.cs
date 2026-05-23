using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceFlowLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_receipt_applications_one_doc",
                schema: "sales",
                table: "receipt_applications");

            migrationBuilder.AddColumn<long>(
                name: "billing_note_id",
                schema: "sales",
                table: "tax_invoices",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "billing_note_id",
                schema: "sales",
                table: "receipt_applications",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "delivery_order_id",
                schema: "sales",
                table: "billing_notes",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_tax_invoices_billing_note_id",
                schema: "sales",
                table: "tax_invoices",
                column: "billing_note_id",
                filter: "billing_note_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_receipt_applications_billing_note_id",
                schema: "sales",
                table: "receipt_applications",
                column: "billing_note_id");

            migrationBuilder.CreateIndex(
                name: "ix_receipt_applications_receipt_id_billing_note_id",
                schema: "sales",
                table: "receipt_applications",
                columns: new[] { "receipt_id", "billing_note_id" },
                unique: true,
                filter: "billing_note_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_receipt_applications_one_doc",
                schema: "sales",
                table: "receipt_applications",
                sql: "(CASE WHEN tax_invoice_id IS NOT NULL THEN 1 ELSE 0 END) + (CASE WHEN delivery_order_id IS NOT NULL THEN 1 ELSE 0 END) + (CASE WHEN billing_note_id IS NOT NULL THEN 1 ELSE 0 END) = 1");

            migrationBuilder.CreateIndex(
                name: "ix_billing_notes_delivery_order_id",
                schema: "sales",
                table: "billing_notes",
                column: "delivery_order_id",
                filter: "delivery_order_id IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_billing_notes_delivery_orders_delivery_order_id",
                schema: "sales",
                table: "billing_notes",
                column: "delivery_order_id",
                principalSchema: "sales",
                principalTable: "delivery_orders",
                principalColumn: "delivery_order_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_receipt_applications_billing_notes_billing_note_id",
                schema: "sales",
                table: "receipt_applications",
                column: "billing_note_id",
                principalSchema: "sales",
                principalTable: "billing_notes",
                principalColumn: "billing_note_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_tax_invoices_billing_notes_billing_note_id",
                schema: "sales",
                table: "tax_invoices",
                column: "billing_note_id",
                principalSchema: "sales",
                principalTable: "billing_notes",
                principalColumn: "billing_note_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_billing_notes_delivery_orders_delivery_order_id",
                schema: "sales",
                table: "billing_notes");

            migrationBuilder.DropForeignKey(
                name: "fk_receipt_applications_billing_notes_billing_note_id",
                schema: "sales",
                table: "receipt_applications");

            migrationBuilder.DropForeignKey(
                name: "fk_tax_invoices_billing_notes_billing_note_id",
                schema: "sales",
                table: "tax_invoices");

            migrationBuilder.DropIndex(
                name: "ix_tax_invoices_billing_note_id",
                schema: "sales",
                table: "tax_invoices");

            migrationBuilder.DropIndex(
                name: "ix_receipt_applications_billing_note_id",
                schema: "sales",
                table: "receipt_applications");

            migrationBuilder.DropIndex(
                name: "ix_receipt_applications_receipt_id_billing_note_id",
                schema: "sales",
                table: "receipt_applications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_receipt_applications_one_doc",
                schema: "sales",
                table: "receipt_applications");

            migrationBuilder.DropIndex(
                name: "ix_billing_notes_delivery_order_id",
                schema: "sales",
                table: "billing_notes");

            migrationBuilder.DropColumn(
                name: "billing_note_id",
                schema: "sales",
                table: "tax_invoices");

            migrationBuilder.DropColumn(
                name: "billing_note_id",
                schema: "sales",
                table: "receipt_applications");

            migrationBuilder.DropColumn(
                name: "delivery_order_id",
                schema: "sales",
                table: "billing_notes");

            migrationBuilder.AddCheckConstraint(
                name: "ck_receipt_applications_one_doc",
                schema: "sales",
                table: "receipt_applications",
                sql: "(tax_invoice_id IS NOT NULL) <> (delivery_order_id IS NOT NULL)");
        }
    }
}
