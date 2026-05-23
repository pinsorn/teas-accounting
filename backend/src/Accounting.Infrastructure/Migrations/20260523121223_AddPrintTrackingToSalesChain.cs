using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPrintTrackingToSalesChain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "original_printed_at",
                schema: "sales",
                table: "sales_orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "print_count",
                schema: "sales",
                table: "sales_orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "original_printed_at",
                schema: "sales",
                table: "quotations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "print_count",
                schema: "sales",
                table: "quotations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "original_printed_at",
                schema: "sales",
                table: "delivery_orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "print_count",
                schema: "sales",
                table: "delivery_orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "original_printed_at",
                schema: "sales",
                table: "billing_notes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "print_count",
                schema: "sales",
                table: "billing_notes",
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
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "print_count",
                schema: "sales",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "original_printed_at",
                schema: "sales",
                table: "quotations");

            migrationBuilder.DropColumn(
                name: "print_count",
                schema: "sales",
                table: "quotations");

            migrationBuilder.DropColumn(
                name: "original_printed_at",
                schema: "sales",
                table: "delivery_orders");

            migrationBuilder.DropColumn(
                name: "print_count",
                schema: "sales",
                table: "delivery_orders");

            migrationBuilder.DropColumn(
                name: "original_printed_at",
                schema: "sales",
                table: "billing_notes");

            migrationBuilder.DropColumn(
                name: "print_count",
                schema: "sales",
                table: "billing_notes");
        }
    }
}
