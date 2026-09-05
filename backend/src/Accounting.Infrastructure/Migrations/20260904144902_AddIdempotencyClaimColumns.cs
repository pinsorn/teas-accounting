using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIdempotencyClaimColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "response_status",
                schema: "sys",
                table: "idempotency_keys",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "response_body",
                schema: "sys",
                table: "idempotency_keys",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "jsonb");

            migrationBuilder.AddColumn<string>(
                name: "response_headers",
                schema: "sys",
                table: "idempotency_keys",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        // Hand-written (spec §3.7-H4): text -> jsonb is an EXPLICIT-only cast in PostgreSQL, so
        // EF's generated AlterColumn (which emits a plain ALTER COLUMN ... TYPE jsonb with no
        // USING clause) would fail here. Schema-only — §9 forbids DML, so this does NOT purge
        // in-flight claims: a Down with a live response_status IS NULL row fails on the final
        // SET NOT NULL; the operator waits out the 24h TTL or deletes the row by hand first.
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE sys.idempotency_keys ALTER COLUMN response_body TYPE jsonb
                USING CASE WHEN response_body IS NULL OR response_body = '' THEN '{}'::jsonb ELSE response_body::jsonb END;
                """);
            migrationBuilder.Sql("ALTER TABLE sys.idempotency_keys ALTER COLUMN response_body SET NOT NULL;");
            migrationBuilder.Sql("ALTER TABLE sys.idempotency_keys ALTER COLUMN response_status SET NOT NULL;");

            migrationBuilder.DropColumn(
                name: "response_headers",
                schema: "sys",
                table: "idempotency_keys");
        }
    }
}
