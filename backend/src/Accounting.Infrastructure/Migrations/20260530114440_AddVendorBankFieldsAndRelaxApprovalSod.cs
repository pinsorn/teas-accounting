using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVendorBankFieldsAndRelaxApprovalSod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_po_sod",
                schema: "purchase",
                table: "purchase_orders");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pv_sod",
                schema: "purchase",
                table: "payment_vouchers");

            migrationBuilder.AddColumn<string>(
                name: "bank_account_name",
                schema: "master",
                table: "vendors",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bank_account_no",
                schema: "master",
                table: "vendors",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bank_name",
                schema: "master",
                table: "vendors",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "swift_code",
                schema: "master",
                table: "vendors",
                type: "character varying(11)",
                maxLength: 11,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "bank_account_name",
                schema: "master",
                table: "vendors");

            migrationBuilder.DropColumn(
                name: "bank_account_no",
                schema: "master",
                table: "vendors");

            migrationBuilder.DropColumn(
                name: "bank_name",
                schema: "master",
                table: "vendors");

            migrationBuilder.DropColumn(
                name: "swift_code",
                schema: "master",
                table: "vendors");

            migrationBuilder.AddCheckConstraint(
                name: "ck_po_sod",
                schema: "purchase",
                table: "purchase_orders",
                sql: "approved_by IS NULL OR approved_by <> created_by");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pv_sod",
                schema: "purchase",
                table: "payment_vouchers",
                sql: "approved_by IS NULL OR approved_by <> created_by");
        }
    }
}
