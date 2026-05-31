using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProductPurchaseSaleAndBusinessUnit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "business_unit_id",
                schema: "master",
                table: "products",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_purchasable",
                schema: "master",
                table: "products",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_saleable",
                schema: "master",
                table: "products",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateIndex(
                name: "ix_products_business_unit_id",
                schema: "master",
                table: "products",
                column: "business_unit_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_products_purpose",
                schema: "master",
                table: "products",
                sql: "is_saleable OR is_purchasable");

            migrationBuilder.AddForeignKey(
                name: "fk_products_business_units_business_unit_id",
                schema: "master",
                table: "products",
                column: "business_unit_id",
                principalSchema: "master",
                principalTable: "business_units",
                principalColumn: "business_unit_id",
                onDelete: ReferentialAction.Restrict);

            // cont.81 — products already configured with an input (purchase) tax code
            // were meant for purchasing → mark them purchasable so they keep showing
            // in the purchase picker. (Sale flag already defaults true.)
            migrationBuilder.Sql(
                "UPDATE master.products SET is_purchasable = TRUE " +
                "WHERE default_input_tax_code_id IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_products_business_units_business_unit_id",
                schema: "master",
                table: "products");

            migrationBuilder.DropIndex(
                name: "ix_products_business_unit_id",
                schema: "master",
                table: "products");

            migrationBuilder.DropCheckConstraint(
                name: "ck_products_purpose",
                schema: "master",
                table: "products");

            migrationBuilder.DropColumn(
                name: "business_unit_id",
                schema: "master",
                table: "products");

            migrationBuilder.DropColumn(
                name: "is_purchasable",
                schema: "master",
                table: "products");

            migrationBuilder.DropColumn(
                name: "is_saleable",
                schema: "master",
                table: "products");
        }
    }
}
