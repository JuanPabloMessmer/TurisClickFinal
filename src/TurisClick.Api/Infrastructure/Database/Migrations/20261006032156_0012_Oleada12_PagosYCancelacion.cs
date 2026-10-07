using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TurisClick.Api.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class _0012_Oleada12_PagosYCancelacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cancellation_policy",
                table: "reservation_items",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cancellation_policy",
                table: "packages",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "payment_transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    reservation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    component = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    reservation_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    provider_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    failure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_transactions", x => x.id);
                    table.CheckConstraint("ck_payment_transactions_amount", "amount >= 0");
                    table.CheckConstraint("ck_payment_transactions_component", "type <> 'REFUND' OR component IS NOT NULL");
                    table.CheckConstraint("ck_payment_transactions_currency", "currency ~ '^[A-Z]{3}$'");
                    table.ForeignKey(
                        name: "FK_payment_transactions_reservations_reservation_id",
                        column: x => x.reservation_id,
                        principalTable: "reservations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "reservation_cancellations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    reservation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    provider_cancellation_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    flight_cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    resolution_attempts = table.Column<int>(type: "integer", nullable: false),
                    next_resolution_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reservation_cancellations", x => x.id);
                    table.ForeignKey(
                        name: "FK_reservation_cancellations_reservations_reservation_id",
                        column: x => x.reservation_id,
                        principalTable: "reservations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "reservation_cancellation_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    cancellation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    component = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reservation_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    paid_amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    refund_amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    fee_amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    policy_applied = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    refund_percentage = table.Column<int>(type: "integer", nullable: true),
                    refund_known = table.Column<bool>(type: "boolean", nullable: false),
                    explanation = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reservation_cancellation_lines", x => x.id);
                    table.CheckConstraint("ck_cancellation_lines_amounts", "paid_amount >= 0 AND refund_amount >= 0 AND fee_amount >= 0 AND refund_amount <= paid_amount");
                    table.CheckConstraint("ck_cancellation_lines_currency", "currency ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("ck_cancellation_lines_percentage", "refund_percentage IS NULL OR (refund_percentage BETWEEN 0 AND 100)");
                    table.ForeignKey(
                        name: "FK_reservation_cancellation_lines_reservation_cancellations_ca~",
                        column: x => x.cancellation_id,
                        principalTable: "reservation_cancellations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_reservation_items_cancellation_policy",
                table: "reservation_items",
                sql: "cancellation_policy IS NULL OR cancellation_policy ~ '^[0-9]{1,3}:[0-9]{1,3}(;[0-9]{1,3}:[0-9]{1,3}){0,5}$'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_packages_cancellation_policy",
                table: "packages",
                sql: "cancellation_policy IS NULL OR cancellation_policy ~ '^[0-9]{1,3}:[0-9]{1,3}(;[0-9]{1,3}:[0-9]{1,3}){0,5}$'");

            migrationBuilder.CreateIndex(
                name: "ix_payment_transactions_reservation",
                table: "payment_transactions",
                columns: new[] { "reservation_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_payment_transactions_refund_key",
                table: "payment_transactions",
                column: "idempotency_key",
                unique: true,
                filter: "type = 'REFUND'");

            migrationBuilder.CreateIndex(
                name: "IX_reservation_cancellation_lines_cancellation_id",
                table: "reservation_cancellation_lines",
                column: "cancellation_id");

            migrationBuilder.CreateIndex(
                name: "ix_reservation_cancellations_pending",
                table: "reservation_cancellations",
                columns: new[] { "status", "next_resolution_at" });

            migrationBuilder.CreateIndex(
                name: "ix_reservation_cancellations_reservation",
                table: "reservation_cancellations",
                columns: new[] { "reservation_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment_transactions");

            migrationBuilder.DropTable(
                name: "reservation_cancellation_lines");

            migrationBuilder.DropTable(
                name: "reservation_cancellations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reservation_items_cancellation_policy",
                table: "reservation_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_packages_cancellation_policy",
                table: "packages");

            migrationBuilder.DropColumn(
                name: "cancellation_policy",
                table: "reservation_items");

            migrationBuilder.DropColumn(
                name: "cancellation_policy",
                table: "packages");
        }
    }
}
