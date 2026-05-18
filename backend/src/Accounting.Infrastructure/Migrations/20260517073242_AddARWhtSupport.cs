using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddARWhtSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_wht_types_company_id_code",
                schema: "tax",
                table: "wht_types");

            migrationBuilder.AddColumn<DateOnly>(
                name: "effective_from",
                schema: "tax",
                table: "wht_types",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(2020, 1, 1));

            migrationBuilder.AddColumn<DateOnly>(
                name: "effective_to",
                schema: "tax",
                table: "wht_types",
                type: "date",
                nullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "payment_voucher_id",
                schema: "tax",
                table: "wht_certificates",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<string>(
                name: "direction",
                schema: "tax",
                table: "wht_certificates",
                type: "character(1)",
                fixedLength: true,
                maxLength: 1,
                nullable: false,
                defaultValue: "P");

            migrationBuilder.AddColumn<long>(
                name: "receipt_id",
                schema: "tax",
                table: "wht_certificates",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "cash_received",
                schema: "sales",
                table: "receipts",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateOnly>(
                name: "customer_wht_cert_date",
                schema: "sales",
                table: "receipts",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "customer_wht_cert_no",
                schema: "sales",
                table: "receipts",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "wht_amount",
                schema: "sales",
                table: "receipts",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "wht_type_id",
                schema: "sales",
                table: "receipts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "default_wht_type_id",
                schema: "master",
                table: "customers",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_wht_types_company_id_code_effective_from",
                schema: "tax",
                table: "wht_types",
                columns: new[] { "company_id", "code", "effective_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_wht_certificates_receipt_id",
                schema: "tax",
                table: "wht_certificates",
                column: "receipt_id");

            migrationBuilder.CreateIndex(
                name: "ix_receipts_wht_type_id",
                schema: "sales",
                table: "receipts",
                column: "wht_type_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_receipts_wht_nonneg",
                schema: "sales",
                table: "receipts",
                sql: "wht_amount >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_receipts_wht_type",
                schema: "sales",
                table: "receipts",
                sql: "(wht_amount = 0 AND wht_type_id IS NULL) OR (wht_amount > 0 AND wht_type_id IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_customers_default_wht_type_id",
                schema: "master",
                table: "customers",
                column: "default_wht_type_id");

            migrationBuilder.AddForeignKey(
                name: "fk_customers_wht_types_default_wht_type_id",
                schema: "master",
                table: "customers",
                column: "default_wht_type_id",
                principalSchema: "tax",
                principalTable: "wht_types",
                principalColumn: "wht_type_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_receipts_wht_types_wht_type_id",
                schema: "sales",
                table: "receipts",
                column: "wht_type_id",
                principalSchema: "tax",
                principalTable: "wht_types",
                principalColumn: "wht_type_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_customers_wht_types_default_wht_type_id",
                schema: "master",
                table: "customers");

            migrationBuilder.DropForeignKey(
                name: "fk_receipts_wht_types_wht_type_id",
                schema: "sales",
                table: "receipts");

            migrationBuilder.DropIndex(
                name: "ix_wht_types_company_id_code_effective_from",
                schema: "tax",
                table: "wht_types");

            migrationBuilder.DropIndex(
                name: "ix_wht_certificates_receipt_id",
                schema: "tax",
                table: "wht_certificates");

            migrationBuilder.DropIndex(
                name: "ix_receipts_wht_type_id",
                schema: "sales",
                table: "receipts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_receipts_wht_nonneg",
                schema: "sales",
                table: "receipts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_receipts_wht_type",
                schema: "sales",
                table: "receipts");

            migrationBuilder.DropIndex(
                name: "ix_customers_default_wht_type_id",
                schema: "master",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "effective_from",
                schema: "tax",
                table: "wht_types");

            migrationBuilder.DropColumn(
                name: "effective_to",
                schema: "tax",
                table: "wht_types");

            migrationBuilder.DropColumn(
                name: "direction",
                schema: "tax",
                table: "wht_certificates");

            migrationBuilder.DropColumn(
                name: "receipt_id",
                schema: "tax",
                table: "wht_certificates");

            migrationBuilder.DropColumn(
                name: "cash_received",
                schema: "sales",
                table: "receipts");

            migrationBuilder.DropColumn(
                name: "customer_wht_cert_date",
                schema: "sales",
                table: "receipts");

            migrationBuilder.DropColumn(
                name: "customer_wht_cert_no",
                schema: "sales",
                table: "receipts");

            migrationBuilder.DropColumn(
                name: "wht_amount",
                schema: "sales",
                table: "receipts");

            migrationBuilder.DropColumn(
                name: "wht_type_id",
                schema: "sales",
                table: "receipts");

            migrationBuilder.DropColumn(
                name: "default_wht_type_id",
                schema: "master",
                table: "customers");

            migrationBuilder.AlterColumn<long>(
                name: "payment_voucher_id",
                schema: "tax",
                table: "wht_certificates",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_wht_types_company_id_code",
                schema: "tax",
                table: "wht_types",
                columns: new[] { "company_id", "code" },
                unique: true);
        }
    }
}
