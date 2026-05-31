using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyProfileStructuredAddress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "reg_building",
                schema: "master",
                table: "company_profile",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reg_floor",
                schema: "master",
                table: "company_profile",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reg_house_no",
                schema: "master",
                table: "company_profile",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reg_moo",
                schema: "master",
                table: "company_profile",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reg_room_no",
                schema: "master",
                table: "company_profile",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reg_soi",
                schema: "master",
                table: "company_profile",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reg_street",
                schema: "master",
                table: "company_profile",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reg_village",
                schema: "master",
                table: "company_profile",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "reg_building",
                schema: "master",
                table: "company_profile");

            migrationBuilder.DropColumn(
                name: "reg_floor",
                schema: "master",
                table: "company_profile");

            migrationBuilder.DropColumn(
                name: "reg_house_no",
                schema: "master",
                table: "company_profile");

            migrationBuilder.DropColumn(
                name: "reg_moo",
                schema: "master",
                table: "company_profile");

            migrationBuilder.DropColumn(
                name: "reg_room_no",
                schema: "master",
                table: "company_profile");

            migrationBuilder.DropColumn(
                name: "reg_soi",
                schema: "master",
                table: "company_profile");

            migrationBuilder.DropColumn(
                name: "reg_street",
                schema: "master",
                table: "company_profile");

            migrationBuilder.DropColumn(
                name: "reg_village",
                schema: "master",
                table: "company_profile");
        }
    }
}
