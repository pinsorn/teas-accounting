using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Accounting.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessUnits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "business_unit_id",
                schema: "sales",
                table: "tax_invoices",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "business_unit_id",
                schema: "sales",
                table: "tax_adjustment_notes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "business_unit_id",
                schema: "sales",
                table: "receipts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "business_unit_id",
                schema: "gl",
                table: "journal_lines",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "requires_business_unit",
                schema: "master",
                table: "companies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "business_units",
                schema: "master",
                columns: table => new
                {
                    business_unit_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name_th = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    name_en = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    default_revenue_account_id = table.Column<long>(type: "bigint", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_business_units", x => x.business_unit_id);
                    table.ForeignKey(
                        name: "fk_business_units_chart_of_accounts_default_revenue_account_id",
                        column: x => x.default_revenue_account_id,
                        principalSchema: "master",
                        principalTable: "chart_of_accounts",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tax_invoices_business_unit_id",
                schema: "sales",
                table: "tax_invoices",
                column: "business_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_tax_invoices_company_id_business_unit_id",
                schema: "sales",
                table: "tax_invoices",
                columns: new[] { "company_id", "business_unit_id" },
                filter: "business_unit_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_tax_adjustment_notes_business_unit_id",
                schema: "sales",
                table: "tax_adjustment_notes",
                column: "business_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_receipts_business_unit_id",
                schema: "sales",
                table: "receipts",
                column: "business_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_receipts_company_id_business_unit_id",
                schema: "sales",
                table: "receipts",
                columns: new[] { "company_id", "business_unit_id" },
                filter: "business_unit_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_journal_lines_business_unit_id",
                schema: "gl",
                table: "journal_lines",
                column: "business_unit_id",
                filter: "business_unit_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_business_units_company_id_code",
                schema: "master",
                table: "business_units",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_business_units_default_revenue_account_id",
                schema: "master",
                table: "business_units",
                column: "default_revenue_account_id");

            migrationBuilder.AddForeignKey(
                name: "fk_journal_lines_business_units_business_unit_id",
                schema: "gl",
                table: "journal_lines",
                column: "business_unit_id",
                principalSchema: "master",
                principalTable: "business_units",
                principalColumn: "business_unit_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_receipts_business_units_business_unit_id",
                schema: "sales",
                table: "receipts",
                column: "business_unit_id",
                principalSchema: "master",
                principalTable: "business_units",
                principalColumn: "business_unit_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_tax_adjustment_notes_business_units_business_unit_id",
                schema: "sales",
                table: "tax_adjustment_notes",
                column: "business_unit_id",
                principalSchema: "master",
                principalTable: "business_units",
                principalColumn: "business_unit_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_tax_invoices_business_units_business_unit_id",
                schema: "sales",
                table: "tax_invoices",
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
                name: "fk_journal_lines_business_units_business_unit_id",
                schema: "gl",
                table: "journal_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_receipts_business_units_business_unit_id",
                schema: "sales",
                table: "receipts");

            migrationBuilder.DropForeignKey(
                name: "fk_tax_adjustment_notes_business_units_business_unit_id",
                schema: "sales",
                table: "tax_adjustment_notes");

            migrationBuilder.DropForeignKey(
                name: "fk_tax_invoices_business_units_business_unit_id",
                schema: "sales",
                table: "tax_invoices");

            migrationBuilder.DropTable(
                name: "business_units",
                schema: "master");

            migrationBuilder.DropIndex(
                name: "ix_tax_invoices_business_unit_id",
                schema: "sales",
                table: "tax_invoices");

            migrationBuilder.DropIndex(
                name: "ix_tax_invoices_company_id_business_unit_id",
                schema: "sales",
                table: "tax_invoices");

            migrationBuilder.DropIndex(
                name: "ix_tax_adjustment_notes_business_unit_id",
                schema: "sales",
                table: "tax_adjustment_notes");

            migrationBuilder.DropIndex(
                name: "ix_receipts_business_unit_id",
                schema: "sales",
                table: "receipts");

            migrationBuilder.DropIndex(
                name: "ix_receipts_company_id_business_unit_id",
                schema: "sales",
                table: "receipts");

            migrationBuilder.DropIndex(
                name: "ix_journal_lines_business_unit_id",
                schema: "gl",
                table: "journal_lines");

            migrationBuilder.DropColumn(
                name: "business_unit_id",
                schema: "sales",
                table: "tax_invoices");

            migrationBuilder.DropColumn(
                name: "business_unit_id",
                schema: "sales",
                table: "tax_adjustment_notes");

            migrationBuilder.DropColumn(
                name: "business_unit_id",
                schema: "sales",
                table: "receipts");

            migrationBuilder.DropColumn(
                name: "business_unit_id",
                schema: "gl",
                table: "journal_lines");

            migrationBuilder.DropColumn(
                name: "requires_business_unit",
                schema: "master",
                table: "companies");
        }
    }
}
