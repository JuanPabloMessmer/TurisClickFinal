using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TurisClick.Api.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class _0007_Oleada7_AiBooking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_reservations_ai_itinerary_id",
                table: "reservations");

            migrationBuilder.CreateIndex(
                name: "ix_reservations_ai_itinerary_id",
                table: "reservations",
                column: "ai_itinerary_id",
                unique: true,
                filter: "ai_itinerary_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_reservations_ai_itinerary_id",
                table: "reservations");

            migrationBuilder.CreateIndex(
                name: "ix_reservations_ai_itinerary_id",
                table: "reservations",
                column: "ai_itinerary_id");
        }
    }
}
