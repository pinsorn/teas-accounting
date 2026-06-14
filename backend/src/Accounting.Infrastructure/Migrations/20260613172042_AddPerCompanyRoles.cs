using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPerCompanyRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "company_id",
                schema: "sys",
                table: "roles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "company_id",
                schema: "sys",
                table: "role_permissions",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "sys",
                table: "roles");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "sys",
                table: "role_permissions");
        }
    }
}
