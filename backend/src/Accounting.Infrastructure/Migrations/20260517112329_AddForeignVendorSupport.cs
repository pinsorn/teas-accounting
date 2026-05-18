using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddForeignVendorSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "country_code",
                schema: "master",
                table: "vendors",
                type: "character(2)",
                fixedLength: true,
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "has_thai_vat_d_reg",
                schema: "master",
                table: "vendors",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_foreign",
                schema: "master",
                table: "vendors",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "has_input_vat",
                schema: "purchase",
                table: "vendor_invoices",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "requires_pnd36reverse_charge",
                schema: "purchase",
                table: "vendor_invoices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "requires_pnd36reverse_charge",
                schema: "purchase",
                table: "payment_vouchers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "self_withhold_mode",
                schema: "purchase",
                table: "payment_vouchers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "ck_vendors_foreign_vatreg",
                schema: "master",
                table: "vendors",
                sql: "is_foreign IS NOT TRUE OR vat_registered IS TRUE");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vendors_vatd_foreign",
                schema: "master",
                table: "vendors",
                sql: "has_thai_vat_d_reg IS NOT TRUE OR is_foreign IS TRUE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_vendors_foreign_vatreg",
                schema: "master",
                table: "vendors");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vendors_vatd_foreign",
                schema: "master",
                table: "vendors");

            migrationBuilder.DropColumn(
                name: "country_code",
                schema: "master",
                table: "vendors");

            migrationBuilder.DropColumn(
                name: "has_thai_vat_d_reg",
                schema: "master",
                table: "vendors");

            migrationBuilder.DropColumn(
                name: "is_foreign",
                schema: "master",
                table: "vendors");

            migrationBuilder.DropColumn(
                name: "has_input_vat",
                schema: "purchase",
                table: "vendor_invoices");

            migrationBuilder.DropColumn(
                name: "requires_pnd36reverse_charge",
                schema: "purchase",
                table: "vendor_invoices");

            migrationBuilder.DropColumn(
                name: "requires_pnd36reverse_charge",
                schema: "purchase",
                table: "payment_vouchers");

            migrationBuilder.DropColumn(
                name: "self_withhold_mode",
                schema: "purchase",
                table: "payment_vouchers");
        }
    }
}
