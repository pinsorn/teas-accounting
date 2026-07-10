using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Accounting.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixedAssets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "fixedasset");

            migrationBuilder.CreateTable(
                name: "depreciation_runs",
                schema: "fixedasset",
                columns: table => new
                {
                    depreciation_run_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    branch_id = table.Column<int>(type: "integer", nullable: false),
                    period_year = table.Column<int>(type: "integer", nullable: false),
                    period_month = table.Column<int>(type: "integer", nullable: false),
                    run_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "POSTED"),
                    total_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    asset_count = table.Column<int>(type: "integer", nullable: false),
                    journal_entry_id = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_depreciation_runs", x => x.depreciation_run_id);
                    table.CheckConstraint("ck_depreciation_runs_month", "period_month BETWEEN 1 AND 12");
                    table.ForeignKey(
                        name: "fk_depreciation_runs_journal_entries_journal_entry_id",
                        column: x => x.journal_entry_id,
                        principalSchema: "gl",
                        principalTable: "journal_entries",
                        principalColumn: "journal_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fixed_assets",
                schema: "fixedasset",
                columns: table => new
                {
                    fixed_asset_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    branch_id = table.Column<int>(type: "integer", nullable: false),
                    business_unit_id = table.Column<int>(type: "integer", nullable: true),
                    doc_no = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    prefix_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "FA"),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    category = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    acquire_date = table.Column<DateOnly>(type: "date", nullable: false),
                    vendor_invoice_id = table.Column<long>(type: "bigint", nullable: true),
                    cost = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    salvage_value = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false, defaultValue: 0m),
                    useful_life_months = table.Column<int>(type: "integer", nullable: false),
                    depreciable_base = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false, defaultValue: 0m),
                    monthly_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false, defaultValue: 0m),
                    depreciation_start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    asset_cost_account_id = table.Column<long>(type: "bigint", nullable: false),
                    accum_dep_account_id = table.Column<long>(type: "bigint", nullable: false),
                    dep_expense_account_id = table.Column<long>(type: "bigint", nullable: false),
                    accumulated_depreciation = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false, defaultValue: 0m),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "DRAFT"),
                    disposal_date = table.Column<DateOnly>(type: "date", nullable: true),
                    disposal_proceeds = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    disposal_vat_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    disposal_gain_loss = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    disposal_buyer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    disposal_journal_entry_id = table.Column<long>(type: "bigint", nullable: true),
                    writeoff_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    activated_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    activated_by = table.Column<long>(type: "bigint", nullable: true),
                    disposed_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    disposed_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fixed_assets", x => x.fixed_asset_id);
                    table.CheckConstraint("ck_fixed_assets_salvage", "salvage_value >= 0 AND salvage_value <= cost");
                    table.CheckConstraint("ck_fixed_assets_useful_life", "useful_life_months > 0");
                    table.ForeignKey(
                        name: "fk_fixed_assets_business_units_business_unit_id",
                        column: x => x.business_unit_id,
                        principalSchema: "master",
                        principalTable: "business_units",
                        principalColumn: "business_unit_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fixed_assets_chart_of_accounts_accum_dep_account_id",
                        column: x => x.accum_dep_account_id,
                        principalSchema: "master",
                        principalTable: "chart_of_accounts",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fixed_assets_chart_of_accounts_asset_cost_account_id",
                        column: x => x.asset_cost_account_id,
                        principalSchema: "master",
                        principalTable: "chart_of_accounts",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fixed_assets_chart_of_accounts_dep_expense_account_id",
                        column: x => x.dep_expense_account_id,
                        principalSchema: "master",
                        principalTable: "chart_of_accounts",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fixed_assets_journal_entries_disposal_journal_entry_id",
                        column: x => x.disposal_journal_entry_id,
                        principalSchema: "gl",
                        principalTable: "journal_entries",
                        principalColumn: "journal_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fixed_assets_vendor_invoices_vendor_invoice_id",
                        column: x => x.vendor_invoice_id,
                        principalSchema: "purchase",
                        principalTable: "vendor_invoices",
                        principalColumn: "vendor_invoice_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "depreciation_run_lines",
                schema: "fixedasset",
                columns: table => new
                {
                    depreciation_run_line_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    depreciation_run_id = table.Column<long>(type: "bigint", nullable: false),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    fixed_asset_id = table.Column<long>(type: "bigint", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    accumulated_after = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_depreciation_run_lines", x => x.depreciation_run_line_id);
                    table.ForeignKey(
                        name: "fk_depreciation_run_lines_depreciation_runs_depreciation_run_id",
                        column: x => x.depreciation_run_id,
                        principalSchema: "fixedasset",
                        principalTable: "depreciation_runs",
                        principalColumn: "depreciation_run_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_depreciation_run_lines_fixed_assets_fixed_asset_id",
                        column: x => x.fixed_asset_id,
                        principalSchema: "fixedasset",
                        principalTable: "fixed_assets",
                        principalColumn: "fixed_asset_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_depreciation_run_lines_depreciation_run_id",
                schema: "fixedasset",
                table: "depreciation_run_lines",
                column: "depreciation_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_depreciation_run_lines_fixed_asset_id_depreciation_run_id",
                schema: "fixedasset",
                table: "depreciation_run_lines",
                columns: new[] { "fixed_asset_id", "depreciation_run_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_depreciation_runs_company_id_period_year_period_month",
                schema: "fixedasset",
                table: "depreciation_runs",
                columns: new[] { "company_id", "period_year", "period_month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_depreciation_runs_journal_entry_id",
                schema: "fixedasset",
                table: "depreciation_runs",
                column: "journal_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_fixed_assets_accum_dep_account_id",
                schema: "fixedasset",
                table: "fixed_assets",
                column: "accum_dep_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_fixed_assets_asset_cost_account_id",
                schema: "fixedasset",
                table: "fixed_assets",
                column: "asset_cost_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_fixed_assets_business_unit_id",
                schema: "fixedasset",
                table: "fixed_assets",
                column: "business_unit_id",
                filter: "business_unit_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_fixed_assets_company_id_acquire_date",
                schema: "fixedasset",
                table: "fixed_assets",
                columns: new[] { "company_id", "acquire_date" });

            migrationBuilder.CreateIndex(
                name: "ix_fixed_assets_company_id_branch_id_doc_no",
                schema: "fixedasset",
                table: "fixed_assets",
                columns: new[] { "company_id", "branch_id", "doc_no" },
                unique: true,
                filter: "doc_no IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_fixed_assets_company_id_status",
                schema: "fixedasset",
                table: "fixed_assets",
                columns: new[] { "company_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_fixed_assets_dep_expense_account_id",
                schema: "fixedasset",
                table: "fixed_assets",
                column: "dep_expense_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_fixed_assets_disposal_journal_entry_id",
                schema: "fixedasset",
                table: "fixed_assets",
                column: "disposal_journal_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_fixed_assets_vendor_invoice_id",
                schema: "fixedasset",
                table: "fixed_assets",
                column: "vendor_invoice_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "depreciation_run_lines",
                schema: "fixedasset");

            migrationBuilder.DropTable(
                name: "depreciation_runs",
                schema: "fixedasset");

            migrationBuilder.DropTable(
                name: "fixed_assets",
                schema: "fixedasset");
        }
    }
}
