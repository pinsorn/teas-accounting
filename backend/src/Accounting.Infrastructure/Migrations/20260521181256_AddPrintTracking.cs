using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPrintTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "original_printed_at",
                schema: "sales",
                table: "tax_invoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "print_count",
                schema: "sales",
                table: "tax_invoices",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "original_printed_at",
                schema: "sales",
                table: "tax_adjustment_notes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "print_count",
                schema: "sales",
                table: "tax_adjustment_notes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "original_printed_at",
                schema: "sales",
                table: "receipts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "print_count",
                schema: "sales",
                table: "receipts",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "original_printed_at",
                schema: "sales",
                table: "tax_invoices");

            migrationBuilder.DropColumn(
                name: "print_count",
                schema: "sales",
                table: "tax_invoices");

            migrationBuilder.DropColumn(
                name: "original_printed_at",
                schema: "sales",
                table: "tax_adjustment_notes");

            migrationBuilder.DropColumn(
                name: "print_count",
                schema: "sales",
                table: "tax_adjustment_notes");

            migrationBuilder.DropColumn(
                name: "original_printed_at",
                schema: "sales",
                table: "receipts");

            migrationBuilder.DropColumn(
                name: "print_count",
                schema: "sales",
                table: "receipts");
        }
    }
}
