using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWhtPayerModeAndCondition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "wht_condition",
                schema: "tax",
                table: "wht_certificates",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "wht_payer_mode",
                schema: "purchase",
                table: "payment_vouchers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "DEDUCT");

            migrationBuilder.AddCheckConstraint(
                name: "ck_wht_certificates_condition",
                schema: "tax",
                table: "wht_certificates",
                sql: "wht_condition IN (1, 2, 3)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_vouchers_wht_payer_mode",
                schema: "purchase",
                table: "payment_vouchers",
                sql: "wht_payer_mode IN ('DEDUCT','GROSS_UP_FOREVER','GROSS_UP_ONCE')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_wht_certificates_condition",
                schema: "tax",
                table: "wht_certificates");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_vouchers_wht_payer_mode",
                schema: "purchase",
                table: "payment_vouchers");

            migrationBuilder.DropColumn(
                name: "wht_condition",
                schema: "tax",
                table: "wht_certificates");

            migrationBuilder.DropColumn(
                name: "wht_payer_mode",
                schema: "purchase",
                table: "payment_vouchers");
        }
    }
}
