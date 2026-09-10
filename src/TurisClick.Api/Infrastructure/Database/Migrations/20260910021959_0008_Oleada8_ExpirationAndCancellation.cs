using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TurisClick.Api.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class _0008_Oleada8_ExpirationAndCancellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_reservations_ai_itinerary_id",
                table: "reservations");

            migrationBuilder.AddColumn<string>(
                name: "cancellation_reason",
                table: "reservation_items",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cancelled_at",
                table: "reservation_items",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_reservations_ai_itinerary_id",
                table: "reservations",
                column: "ai_itinerary_id",
                unique: true,
                filter: "ai_itinerary_id IS NOT NULL AND status NOT IN ('EXPIRED', 'CANCELLED')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_reservations_ai_itinerary_id",
                table: "reservations");

            migrationBuilder.DropColumn(
                name: "cancellation_reason",
                table: "reservation_items");

            migrationBuilder.DropColumn(
                name: "cancelled_at",
                table: "reservation_items");

            migrationBuilder.CreateIndex(
                name: "ix_reservations_ai_itinerary_id",
                table: "reservations",
                column: "ai_itinerary_id",
                unique: true,
                filter: "ai_itinerary_id IS NOT NULL");
        }
    }
}
