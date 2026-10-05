using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TurisClick.Api.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class _0011_Oleada11_ReservaDePaqueteConVuelo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_flight_bookings_flight_quote_id",
                table: "flight_bookings");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "flight_bookings",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AddColumn<string>(
                name: "carrier_iata",
                table: "flight_bookings",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "carrier_name",
                table: "flight_bookings",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "destination_iata",
                table: "flight_bookings",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "inbound_arrival_at",
                table: "flight_bookings",
                type: "timestamp",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "inbound_date",
                table: "flight_bookings",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "inbound_departure_at",
                table: "flight_bookings",
                type: "timestamp",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "inbound_flight_number",
                table: "flight_bookings",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "itinerary_summary",
                table: "flight_bookings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_reconciliation_at",
                table: "flight_bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "next_reconciliation_at",
                table: "flight_bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "origin_iata",
                table: "flight_bookings",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "outbound_arrival_at",
                table: "flight_bookings",
                type: "timestamp",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "outbound_date",
                table: "flight_bookings",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<DateTime>(
                name: "outbound_departure_at",
                table: "flight_bookings",
                type: "timestamp",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "outbound_flight_number",
                table: "flight_bookings",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "reconciliation_attempts",
                table: "flight_bookings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "travelers",
                table: "flight_bookings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "ix_flight_bookings_reconciliation",
                table: "flight_bookings",
                columns: new[] { "status", "next_reconciliation_at" });

            migrationBuilder.CreateIndex(
                name: "ux_flight_bookings_flight_quote_id",
                table: "flight_bookings",
                column: "flight_quote_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_flight_bookings_reconciliation",
                table: "flight_bookings");

            migrationBuilder.DropIndex(
                name: "ux_flight_bookings_flight_quote_id",
                table: "flight_bookings");

            migrationBuilder.DropColumn(
                name: "carrier_iata",
                table: "flight_bookings");

            migrationBuilder.DropColumn(
                name: "carrier_name",
                table: "flight_bookings");

            migrationBuilder.DropColumn(
                name: "destination_iata",
                table: "flight_bookings");

            migrationBuilder.DropColumn(
                name: "inbound_arrival_at",
                table: "flight_bookings");

            migrationBuilder.DropColumn(
                name: "inbound_date",
                table: "flight_bookings");

            migrationBuilder.DropColumn(
                name: "inbound_departure_at",
                table: "flight_bookings");

            migrationBuilder.DropColumn(
                name: "inbound_flight_number",
                table: "flight_bookings");

            migrationBuilder.DropColumn(
                name: "itinerary_summary",
                table: "flight_bookings");

            migrationBuilder.DropColumn(
                name: "last_reconciliation_at",
                table: "flight_bookings");

            migrationBuilder.DropColumn(
                name: "next_reconciliation_at",
                table: "flight_bookings");

            migrationBuilder.DropColumn(
                name: "origin_iata",
                table: "flight_bookings");

            migrationBuilder.DropColumn(
                name: "outbound_arrival_at",
                table: "flight_bookings");

            migrationBuilder.DropColumn(
                name: "outbound_date",
                table: "flight_bookings");

            migrationBuilder.DropColumn(
                name: "outbound_departure_at",
                table: "flight_bookings");

            migrationBuilder.DropColumn(
                name: "outbound_flight_number",
                table: "flight_bookings");

            migrationBuilder.DropColumn(
                name: "reconciliation_attempts",
                table: "flight_bookings");

            migrationBuilder.DropColumn(
                name: "travelers",
                table: "flight_bookings");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "flight_bookings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.CreateIndex(
                name: "IX_flight_bookings_flight_quote_id",
                table: "flight_bookings",
                column: "flight_quote_id");
        }
    }
}
