using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCancelReissueColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cancel_reason",
                schema: "sales",
                table: "tax_invoices",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cancel_reason_code",
                schema: "sales",
                table: "tax_invoices",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cancelled_at",
                schema: "sales",
                table: "tax_invoices",
                type: "timestamp(3) with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "cancelled_by",
                schema: "sales",
                table: "tax_invoices",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "journal_entry_id",
                schema: "sales",
                table: "tax_invoices",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "replaces_tax_invoice_id",
                schema: "sales",
                table: "tax_invoices",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "reversal_journal_entry_id",
                schema: "sales",
                table: "tax_invoices",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cancel_reason",
                schema: "sales",
                table: "receipts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cancel_reason_code",
                schema: "sales",
                table: "receipts",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cancelled_at",
                schema: "sales",
                table: "receipts",
                type: "timestamp(3) with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "cancelled_by",
                schema: "sales",
                table: "receipts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "journal_entry_id",
                schema: "sales",
                table: "receipts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "replaces_receipt_id",
                schema: "sales",
                table: "receipts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "reversal_journal_entry_id",
                schema: "sales",
                table: "receipts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cancel_reason_code",
                schema: "sales",
                table: "billing_notes",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cancelled_at",
                schema: "sales",
                table: "billing_notes",
                type: "timestamp(3) with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "cancelled_by",
                schema: "sales",
                table: "billing_notes",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "reversal_journal_entry_id",
                schema: "sales",
                table: "billing_notes",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_tax_invoices_replaces",
                schema: "sales",
                table: "tax_invoices",
                column: "replaces_tax_invoice_id",
                unique: true,
                filter: "replaces_tax_invoice_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_receipts_replaces",
                schema: "sales",
                table: "receipts",
                column: "replaces_receipt_id",
                unique: true,
                filter: "replaces_receipt_id IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_receipts_receipts_replaces_receipt_id",
                schema: "sales",
                table: "receipts",
                column: "replaces_receipt_id",
                principalSchema: "sales",
                principalTable: "receipts",
                principalColumn: "receipt_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_tax_invoices_tax_invoices_replaces_tax_invoice_id",
                schema: "sales",
                table: "tax_invoices",
                column: "replaces_tax_invoice_id",
                principalSchema: "sales",
                principalTable: "tax_invoices",
                principalColumn: "tax_invoice_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_receipts_receipts_replaces_receipt_id",
                schema: "sales",
                table: "receipts");

            migrationBuilder.DropForeignKey(
                name: "fk_tax_invoices_tax_invoices_replaces_tax_invoice_id",
                schema: "sales",
                table: "tax_invoices");

            migrationBuilder.DropIndex(
                name: "ux_tax_invoices_replaces",
                schema: "sales",
                table: "tax_invoices");

            migrationBuilder.DropIndex(
                name: "ux_receipts_replaces",
                schema: "sales",
                table: "receipts");

            migrationBuilder.DropColumn(
                name: "cancel_reason",
                schema: "sales",
                table: "tax_invoices");

            migrationBuilder.DropColumn(
                name: "cancel_reason_code",
                schema: "sales",
                table: "tax_invoices");

            migrationBuilder.DropColumn(
                name: "cancelled_at",
                schema: "sales",
                table: "tax_invoices");

            migrationBuilder.DropColumn(
                name: "cancelled_by",
                schema: "sales",
                table: "tax_invoices");

            migrationBuilder.DropColumn(
                name: "journal_entry_id",
                schema: "sales",
                table: "tax_invoices");

            migrationBuilder.DropColumn(
                name: "replaces_tax_invoice_id",
                schema: "sales",
                table: "tax_invoices");

            migrationBuilder.DropColumn(
                name: "reversal_journal_entry_id",
                schema: "sales",
                table: "tax_invoices");

            migrationBuilder.DropColumn(
                name: "cancel_reason",
                schema: "sales",
                table: "receipts");

            migrationBuilder.DropColumn(
                name: "cancel_reason_code",
                schema: "sales",
                table: "receipts");

            migrationBuilder.DropColumn(
                name: "cancelled_at",
                schema: "sales",
                table: "receipts");

            migrationBuilder.DropColumn(
                name: "cancelled_by",
                schema: "sales",
                table: "receipts");

            migrationBuilder.DropColumn(
                name: "journal_entry_id",
                schema: "sales",
                table: "receipts");

            migrationBuilder.DropColumn(
                name: "replaces_receipt_id",
                schema: "sales",
                table: "receipts");

            migrationBuilder.DropColumn(
                name: "reversal_journal_entry_id",
                schema: "sales",
                table: "receipts");

            migrationBuilder.DropColumn(
                name: "cancel_reason_code",
                schema: "sales",
                table: "billing_notes");

            migrationBuilder.DropColumn(
                name: "cancelled_at",
                schema: "sales",
                table: "billing_notes");

            migrationBuilder.DropColumn(
                name: "cancelled_by",
                schema: "sales",
                table: "billing_notes");

            migrationBuilder.DropColumn(
                name: "reversal_journal_entry_id",
                schema: "sales",
                table: "billing_notes");
        }
    }
}
