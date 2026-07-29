using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPnd2IncomeCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "pnd2_income_code",
                schema: "tax",
                table: "wht_types",
                type: "character varying(1)",
                maxLength: 1,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pnd2_income_code",
                schema: "tax",
                table: "wht_certificates",
                type: "character varying(1)",
                maxLength: 1,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "pnd2_income_code",
                schema: "tax",
                table: "wht_types");

            migrationBuilder.DropColumn(
                name: "pnd2_income_code",
                schema: "tax",
                table: "wht_certificates");
        }
    }
}
