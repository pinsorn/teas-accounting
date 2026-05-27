using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPrintTrackingToPurchaseChain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "original_printed_at",
                schema: "purchase",
                table: "purchase_orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "print_count",
                schema: "purchase",
                table: "purchase_orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "original_printed_at",
                schema: "purchase",
                table: "payment_vouchers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "print_count",
                schema: "purchase",
                table: "payment_vouchers",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "original_printed_at",
                schema: "purchase",
                table: "purchase_orders");

            migrationBuilder.DropColumn(
                name: "print_count",
                schema: "purchase",
                table: "purchase_orders");

            migrationBuilder.DropColumn(
                name: "original_printed_at",
                schema: "purchase",
                table: "payment_vouchers");

            migrationBuilder.DropColumn(
                name: "print_count",
                schema: "purchase",
                table: "payment_vouchers");
        }
    }
}
