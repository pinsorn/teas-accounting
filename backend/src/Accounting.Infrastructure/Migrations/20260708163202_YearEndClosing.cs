using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Accounting.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class YearEndClosing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_closing_entry",
                schema: "gl",
                table: "journal_entries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "fiscal_year_closes",
                schema: "gl",
                columns: table => new
                {
                    fiscal_year_close_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    fiscal_year = table.Column<int>(type: "integer", nullable: false),
                    fiscal_start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    fiscal_end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    net_profit = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    closing_journal_id = table.Column<long>(type: "bigint", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    closed_by = table.Column<long>(type: "bigint", nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    reversed_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    reversed_by = table.Column<long>(type: "bigint", nullable: true),
                    reversing_journal_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fiscal_year_closes", x => x.fiscal_year_close_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_fiscal_year_closes_company_id_fiscal_year",
                schema: "gl",
                table: "fiscal_year_closes",
                columns: new[] { "company_id", "fiscal_year" },
                unique: true,
                filter: "reversed_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fiscal_year_closes",
                schema: "gl");

            migrationBuilder.DropColumn(
                name: "is_closing_entry",
                schema: "gl",
                table: "journal_entries");
        }
    }
}
