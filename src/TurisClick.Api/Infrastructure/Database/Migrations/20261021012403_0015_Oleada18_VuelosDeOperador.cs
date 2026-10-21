using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TurisClick.Api.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class _0015_Oleada18_VuelosDeOperador : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_reservation_items_product_shape",
                table: "reservation_items");

            migrationBuilder.AddColumn<string>(
                name: "booking_reference",
                table: "reservation_items",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "issuance_note",
                table: "reservation_items",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "issuance_status",
                table: "reservation_items",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "NOT_REQUIRED");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "issued_at",
                table: "reservation_items",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "issued_by",
                table: "reservation_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "provider_flight_departure_id",
                table: "reservation_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "provider_flight_id",
                table: "reservation_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ticket_numbers",
                table: "reservation_items",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "can_sell_flights",
                table: "companies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "flight_seller_authorized_at",
                table: "companies",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "flight_seller_authorized_by",
                table: "companies",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "flight_seller_registration",
                table: "companies",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "provider_flights",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origin_iata = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    destination_iata = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    carrier_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    carrier_iata = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    flight_number = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    cabin_class = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "ECONOMY"),
                    checked_bags = table.Column<int>(type: "integer", nullable: false),
                    carry_on_bags = table.Column<int>(type: "integer", nullable: false),
                    cancellation_policy = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    fare_rules = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "DRAFT"),
                    suspended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_provider_flights", x => x.id);
                    table.CheckConstraint("ck_provider_flights_bags", "checked_bags BETWEEN 0 AND 3 AND carry_on_bags BETWEEN 0 AND 2");
                    table.CheckConstraint("ck_provider_flights_cancellation_policy", "cancellation_policy IS NULL OR cancellation_policy ~ '^[0-9]{1,3}:[0-9]{1,3}(;[0-9]{1,3}:[0-9]{1,3}){0,5}$'");
                    table.CheckConstraint("ck_provider_flights_iata", "origin_iata ~ '^[A-Z]{3}$' AND destination_iata ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("ck_provider_flights_route", "origin_iata <> destination_iata");
                    table.ForeignKey(
                        name: "FK_provider_flights_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reservation_passengers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    reservation_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    given_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    family_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    born_on = table.Column<DateOnly>(type: "date", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reservation_passengers", x => x.id);
                    table.ForeignKey(
                        name: "FK_reservation_passengers_reservation_items_reservation_item_id",
                        column: x => x.reservation_item_id,
                        principalTable: "reservation_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "provider_flight_departures",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    provider_flight_id = table.Column<Guid>(type: "uuid", nullable: false),
                    departure_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    arrival_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    total_seats = table.Column<int>(type: "integer", nullable: false),
                    reserved_seats = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    adult_price = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    child_price = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    infant_price = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "OPEN"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_provider_flight_departures", x => x.id);
                    table.CheckConstraint("ck_provider_flight_departures_currency", "currency ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("ck_provider_flight_departures_prices", "adult_price >= 0 AND child_price >= 0 AND infant_price >= 0");
                    table.CheckConstraint("ck_provider_flight_departures_seats", "total_seats > 0 AND reserved_seats >= 0 AND reserved_seats <= total_seats");
                    table.CheckConstraint("ck_provider_flight_departures_times", "arrival_at > departure_at");
                    table.ForeignKey(
                        name: "FK_provider_flight_departures_provider_flights_provider_flight~",
                        column: x => x.provider_flight_id,
                        principalTable: "provider_flights",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_reservation_items_issuance",
                table: "reservation_items",
                columns: new[] { "issuance_status", "product_type" });

            migrationBuilder.CreateIndex(
                name: "ix_reservation_items_provider_flight_departure",
                table: "reservation_items",
                column: "provider_flight_departure_id");

            migrationBuilder.CreateIndex(
                name: "IX_reservation_items_provider_flight_id",
                table: "reservation_items",
                column: "provider_flight_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reservation_items_issuance",
                table: "reservation_items",
                sql: "issuance_status = 'NOT_REQUIRED' OR product_type = 'FLIGHT'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reservation_items_issued_tickets",
                table: "reservation_items",
                sql: "issuance_status <> 'ISSUED' OR (ticket_numbers IS NOT NULL AND issued_at IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reservation_items_product_shape",
                table: "reservation_items",
                sql: "(product_type = 'EXPERIENCE' AND experience_id IS NOT NULL AND package_id IS NULL AND experience_availability_id IS NOT NULL AND package_availability_id IS NULL AND provider_flight_id IS NULL AND provider_flight_departure_id IS NULL) OR (product_type = 'PACKAGE' AND package_id IS NOT NULL AND experience_id IS NULL AND package_availability_id IS NOT NULL AND experience_availability_id IS NULL AND provider_flight_id IS NULL AND provider_flight_departure_id IS NULL) OR (product_type = 'FLIGHT' AND provider_flight_id IS NOT NULL AND provider_flight_departure_id IS NOT NULL AND experience_id IS NULL AND package_id IS NULL AND experience_availability_id IS NULL AND package_availability_id IS NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_provider_flight_departures_flight_date",
                table: "provider_flight_departures",
                columns: new[] { "provider_flight_id", "departure_at" });

            migrationBuilder.CreateIndex(
                name: "ix_provider_flights_company",
                table: "provider_flights",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_provider_flights_route",
                table: "provider_flights",
                columns: new[] { "origin_iata", "destination_iata", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_reservation_passengers_item_order",
                table: "reservation_passengers",
                columns: new[] { "reservation_item_id", "sort_order" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_reservation_items_provider_flight_departures_provider_fligh~",
                table: "reservation_items",
                column: "provider_flight_departure_id",
                principalTable: "provider_flight_departures",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_reservation_items_provider_flights_provider_flight_id",
                table: "reservation_items",
                column: "provider_flight_id",
                principalTable: "provider_flights",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_reservation_items_provider_flight_departures_provider_fligh~",
                table: "reservation_items");

            migrationBuilder.DropForeignKey(
                name: "FK_reservation_items_provider_flights_provider_flight_id",
                table: "reservation_items");

            migrationBuilder.DropTable(
                name: "provider_flight_departures");

            migrationBuilder.DropTable(
                name: "reservation_passengers");

            migrationBuilder.DropTable(
                name: "provider_flights");

            migrationBuilder.DropIndex(
                name: "ix_reservation_items_issuance",
                table: "reservation_items");

            migrationBuilder.DropIndex(
                name: "ix_reservation_items_provider_flight_departure",
                table: "reservation_items");

            migrationBuilder.DropIndex(
                name: "IX_reservation_items_provider_flight_id",
                table: "reservation_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reservation_items_issuance",
                table: "reservation_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reservation_items_issued_tickets",
                table: "reservation_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reservation_items_product_shape",
                table: "reservation_items");

            migrationBuilder.DropColumn(
                name: "booking_reference",
                table: "reservation_items");

            migrationBuilder.DropColumn(
                name: "issuance_note",
                table: "reservation_items");

            migrationBuilder.DropColumn(
                name: "issuance_status",
                table: "reservation_items");

            migrationBuilder.DropColumn(
                name: "issued_at",
                table: "reservation_items");

            migrationBuilder.DropColumn(
                name: "issued_by",
                table: "reservation_items");

            migrationBuilder.DropColumn(
                name: "provider_flight_departure_id",
                table: "reservation_items");

            migrationBuilder.DropColumn(
                name: "provider_flight_id",
                table: "reservation_items");

            migrationBuilder.DropColumn(
                name: "ticket_numbers",
                table: "reservation_items");

            migrationBuilder.DropColumn(
                name: "can_sell_flights",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "flight_seller_authorized_at",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "flight_seller_authorized_by",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "flight_seller_registration",
                table: "companies");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reservation_items_product_shape",
                table: "reservation_items",
                sql: "(product_type = 'EXPERIENCE' AND experience_id IS NOT NULL AND package_id IS NULL AND experience_availability_id IS NOT NULL AND package_availability_id IS NULL) OR (product_type = 'PACKAGE' AND package_id IS NOT NULL AND experience_id IS NULL AND package_availability_id IS NOT NULL AND experience_availability_id IS NULL)");
        }
    }
}
