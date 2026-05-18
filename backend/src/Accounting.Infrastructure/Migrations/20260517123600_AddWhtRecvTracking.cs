using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWhtRecvTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cert_received_at",
                schema: "tax",
                table: "wht_certificates",
                type: "timestamp(3) with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reconciled_at",
                schema: "tax",
                table: "wht_certificates",
                type: "timestamp(3) with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cert_received_at",
                schema: "tax",
                table: "wht_certificates");

            migrationBuilder.DropColumn(
                name: "reconciled_at",
                schema: "tax",
                table: "wht_certificates");
        }
    }
}
