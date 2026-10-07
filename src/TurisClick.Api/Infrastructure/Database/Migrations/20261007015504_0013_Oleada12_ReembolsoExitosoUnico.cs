using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TurisClick.Api.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class _0013_Oleada12_ReembolsoExitosoUnico : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_payment_transactions_refund_key",
                table: "payment_transactions");

            migrationBuilder.CreateIndex(
                name: "ux_payment_transactions_refund_key",
                table: "payment_transactions",
                column: "idempotency_key",
                unique: true,
                filter: "type = 'REFUND' AND status = 'SUCCEEDED'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_payment_transactions_refund_key",
                table: "payment_transactions");

            migrationBuilder.CreateIndex(
                name: "ux_payment_transactions_refund_key",
                table: "payment_transactions",
                column: "idempotency_key",
                unique: true,
                filter: "type = 'REFUND'");
        }
    }
}
