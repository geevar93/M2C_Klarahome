using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KlaraHome.Modules.Reporting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BackfillCodFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Data only, no schema change. The order fact derived is_cod from a payment-method name
            // ("Cod") that no order ever carried - the Orders module says "CashOnDelivery" - so
            // every fact written so far filed a cash-on-delivery order as prepaid. The method name is
            // stored on the same row, so the flag is recoverable exactly.
            migrationBuilder.Sql(
                "UPDATE reporting.fact_orders SET is_cod = TRUE "
                + "WHERE is_cod = FALSE AND lower(payment_method) IN ('cashondelivery', 'cod');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo: the previous value was a defect, not a state worth restoring.
        }
    }
}
