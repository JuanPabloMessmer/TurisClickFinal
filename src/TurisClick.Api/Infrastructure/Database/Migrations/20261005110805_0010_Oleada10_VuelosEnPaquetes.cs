using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TurisClick.Api.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class _0010_Oleada10_VuelosEnPaquetes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "includes_flight",
                table: "packages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "flight_quotes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    package_availability_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tourist_id = table.Column<Guid>(type: "uuid", nullable: true),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    provider_offer_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    origin_iata = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    destination_iata = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    outbound_date = table.Column<DateOnly>(type: "date", nullable: false),
                    inbound_date = table.Column<DateOnly>(type: "date", nullable: true),
                    travelers = table.Column<int>(type: "integer", nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    initial_amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    quoted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    revalidated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    itinerary_summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flight_quotes", x => x.id);
                    table.CheckConstraint("ck_flight_quotes_amounts", "total_amount >= 0 AND initial_amount >= 0");
                    table.CheckConstraint("ck_flight_quotes_currency", "currency ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("ck_flight_quotes_inbound", "inbound_date IS NULL OR inbound_date >= outbound_date");
                    table.CheckConstraint("ck_flight_quotes_travelers", "travelers > 0");
                    table.ForeignKey(
                        name: "FK_flight_quotes_package_availabilities_package_availability_id",
                        column: x => x.package_availability_id,
                        principalTable: "package_availabilities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_flight_quotes_packages_package_id",
                        column: x => x.package_id,
                        principalTable: "packages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "package_flight_rules",
                columns: table => new
                {
                    package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    destination_iata = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    allowed_origin_iatas = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    cabin_class = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    outbound_offset_days = table.Column<int>(type: "integer", nullable: false),
                    inbound_offset_days = table.Column<int>(type: "integer", nullable: false),
                    round_trip = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_package_flight_rules", x => x.package_id);
                    table.CheckConstraint("ck_package_flight_rules_destination", "destination_iata ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("ck_package_flight_rules_offsets", "outbound_offset_days BETWEEN -7 AND 7 AND inbound_offset_days BETWEEN -7 AND 7");
                    table.CheckConstraint("ck_package_flight_rules_origins", "allowed_origin_iatas ~ '^[A-Z]{3}(,[A-Z]{3})*$'");
                    table.ForeignKey(
                        name: "FK_package_flight_rules_packages_package_id",
                        column: x => x.package_id,
                        principalTable: "packages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "flight_bookings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    reservation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    flight_quote_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    provider_order_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    booking_reference = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flight_bookings", x => x.id);
                    table.CheckConstraint("ck_flight_bookings_amount", "total_amount >= 0");
                    table.CheckConstraint("ck_flight_bookings_currency", "currency ~ '^[A-Z]{3}$'");
                    table.ForeignKey(
                        name: "FK_flight_bookings_flight_quotes_flight_quote_id",
                        column: x => x.flight_quote_id,
                        principalTable: "flight_quotes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_flight_bookings_reservations_reservation_id",
                        column: x => x.reservation_id,
                        principalTable: "reservations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_flight_bookings_flight_quote_id",
                table: "flight_bookings",
                column: "flight_quote_id");

            migrationBuilder.CreateIndex(
                name: "ux_flight_bookings_idempotency_key",
                table: "flight_bookings",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_flight_bookings_reservation_id",
                table: "flight_bookings",
                column: "reservation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_flight_quotes_package_availability_id",
                table: "flight_quotes",
                column: "package_availability_id");

            migrationBuilder.CreateIndex(
                name: "ix_flight_quotes_package_quoted",
                table: "flight_quotes",
                columns: new[] { "package_id", "quoted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_flight_quotes_tourist_id",
                table: "flight_quotes",
                column: "tourist_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "flight_bookings");

            migrationBuilder.DropTable(
                name: "package_flight_rules");

            migrationBuilder.DropTable(
                name: "flight_quotes");

            migrationBuilder.DropColumn(
                name: "includes_flight",
                table: "packages");
        }
    }
}
