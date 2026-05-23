using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting.Infrastructure.Migrations
{
    /// <summary>
    /// Sprint 13h P9 — Delivery Order status expands from 3 to 4 states:
    /// Draft, Posted, Cancelled → Draft, Issued, Delivered, Cancelled.
    /// Posted rows migrate to Delivered (those already had the linked TI fired).
    /// Status is persisted as UPPER string (HasConversion in EF config), so the
    /// column shape (varchar(20)) does not change — only the allowed values do.
    /// </summary>
    public partial class AddDeliveryOrderDeliveredStage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfill existing POSTED rows to DELIVERED. The previous Pattern X
            // semantics auto-fired the TI on Post, so any row currently POSTED
            // already has a linked TI — Delivered is the correct new state.
            migrationBuilder.Sql(
                "UPDATE sales.delivery_orders SET status = 'DELIVERED' WHERE status = 'POSTED';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse: collapse DELIVERED + ISSUED back to POSTED.
            migrationBuilder.Sql(
                "UPDATE sales.delivery_orders SET status = 'POSTED' WHERE status IN ('DELIVERED', 'ISSUED');");
        }
    }
}
