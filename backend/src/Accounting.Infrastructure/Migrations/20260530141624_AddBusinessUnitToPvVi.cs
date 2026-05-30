using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessUnitToPvVi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "business_unit_id",
                schema: "purchase",
                table: "vendor_invoices",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "business_unit_id",
                schema: "purchase",
                table: "payment_vouchers",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_vendor_invoices_business_unit_id",
                schema: "purchase",
                table: "vendor_invoices",
                column: "business_unit_id",
                filter: "business_unit_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_payment_vouchers_business_unit_id",
                schema: "purchase",
                table: "payment_vouchers",
                column: "business_unit_id",
                filter: "business_unit_id IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_payment_vouchers_business_units_business_unit_id",
                schema: "purchase",
                table: "payment_vouchers",
                column: "business_unit_id",
                principalSchema: "master",
                principalTable: "business_units",
                principalColumn: "business_unit_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_vendor_invoices_business_units_business_unit_id",
                schema: "purchase",
                table: "vendor_invoices",
                column: "business_unit_id",
                principalSchema: "master",
                principalTable: "business_units",
                principalColumn: "business_unit_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_payment_vouchers_business_units_business_unit_id",
                schema: "purchase",
                table: "payment_vouchers");

            migrationBuilder.DropForeignKey(
                name: "fk_vendor_invoices_business_units_business_unit_id",
                schema: "purchase",
                table: "vendor_invoices");

            migrationBuilder.DropIndex(
                name: "ix_vendor_invoices_business_unit_id",
                schema: "purchase",
                table: "vendor_invoices");

            migrationBuilder.DropIndex(
                name: "ix_payment_vouchers_business_unit_id",
                schema: "purchase",
                table: "payment_vouchers");

            migrationBuilder.DropColumn(
                name: "business_unit_id",
                schema: "purchase",
                table: "vendor_invoices");

            migrationBuilder.DropColumn(
                name: "business_unit_id",
                schema: "purchase",
                table: "payment_vouchers");
        }
    }
}
