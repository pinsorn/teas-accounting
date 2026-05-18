using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddApiKeyBuBinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "default_business_unit_id",
                schema: "sys",
                table: "api_keys",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_api_keys_default_business_unit_id",
                schema: "sys",
                table: "api_keys",
                column: "default_business_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_api_keys_key_prefix",
                schema: "sys",
                table: "api_keys",
                column: "key_prefix",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_api_keys_business_units_default_business_unit_id",
                schema: "sys",
                table: "api_keys",
                column: "default_business_unit_id",
                principalSchema: "master",
                principalTable: "business_units",
                principalColumn: "business_unit_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_api_keys_business_units_default_business_unit_id",
                schema: "sys",
                table: "api_keys");

            migrationBuilder.DropIndex(
                name: "ix_api_keys_default_business_unit_id",
                schema: "sys",
                table: "api_keys");

            migrationBuilder.DropIndex(
                name: "ix_api_keys_key_prefix",
                schema: "sys",
                table: "api_keys");

            migrationBuilder.DropColumn(
                name: "default_business_unit_id",
                schema: "sys",
                table: "api_keys");
        }
    }
}
