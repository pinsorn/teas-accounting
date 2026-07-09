using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Accounting.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BankReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "bank");

            migrationBuilder.CreateTable(
                name: "bank_accounts",
                schema: "bank",
                columns: table => new
                {
                    bank_account_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    bank_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    bank_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    account_no = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    account_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    account_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    gl_cash_account_id = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false, defaultValue: "THB"),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_accounts", x => x.bank_account_id);
                    table.ForeignKey(
                        name: "fk_bank_accounts_chart_of_accounts_gl_cash_account_id",
                        column: x => x.gl_cash_account_id,
                        principalSchema: "master",
                        principalTable: "chart_of_accounts",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "statement_imports",
                schema: "bank",
                columns: table => new
                {
                    statement_import_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    bank_account_id = table.Column<int>(type: "integer", nullable: false),
                    adapter_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    source_file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    attachment_id = table.Column<long>(type: "bigint", nullable: true),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    opening_balance = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    closing_balance = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    line_count = table.Column<int>(type: "integer", nullable: false),
                    withdrawal_total = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    deposit_total = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    imported_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    imported_by = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_statement_imports", x => x.statement_import_id);
                });

            migrationBuilder.CreateTable(
                name: "statement_lines",
                schema: "bank",
                columns: table => new
                {
                    statement_line_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    statement_import_id = table.Column<long>(type: "bigint", nullable: false),
                    bank_account_id = table.Column<int>(type: "integer", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    txn_date = table.Column<DateOnly>(type: "date", nullable: false),
                    txn_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    value_date = table.Column<DateOnly>(type: "date", nullable: true),
                    direction = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    running_balance = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    channel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    txn_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    raw_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    match_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    matched_receipt_id = table.Column<long>(type: "bigint", nullable: true),
                    matched_payment_voucher_id = table.Column<long>(type: "bigint", nullable: true),
                    posted_journal_id = table.Column<long>(type: "bigint", nullable: true),
                    matched_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    matched_by = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_statement_lines", x => x.statement_line_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bank_accounts_company_id_account_no",
                schema: "bank",
                table: "bank_accounts",
                columns: new[] { "company_id", "account_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bank_accounts_gl_cash_account_id",
                schema: "bank",
                table: "bank_accounts",
                column: "gl_cash_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_statement_imports_company_id_bank_account_id_period_start",
                schema: "bank",
                table: "statement_imports",
                columns: new[] { "company_id", "bank_account_id", "period_start" });

            migrationBuilder.CreateIndex(
                name: "ix_statement_lines_company_id_bank_account_id_txn_date",
                schema: "bank",
                table: "statement_lines",
                columns: new[] { "company_id", "bank_account_id", "txn_date" });

            migrationBuilder.CreateIndex(
                name: "ix_statement_lines_statement_import_id_line_no",
                schema: "bank",
                table: "statement_lines",
                columns: new[] { "statement_import_id", "line_no" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bank_accounts",
                schema: "bank");

            migrationBuilder.DropTable(
                name: "statement_imports",
                schema: "bank");

            migrationBuilder.DropTable(
                name: "statement_lines",
                schema: "bank");
        }
    }
}
