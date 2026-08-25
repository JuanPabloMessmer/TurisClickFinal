using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TurisClick.Api.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class _0004_Oleada2_Reservas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reservations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    tourist_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ai_itinerary_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "PENDING_PAYMENT"),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reservations", x => x.id);
                    table.ForeignKey(
                        name: "FK_reservations_users_tourist_id",
                        column: x => x.tourist_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reservation_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    reservation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    experience_id = table.Column<Guid>(type: "uuid", nullable: true),
                    package_id = table.Column<Guid>(type: "uuid", nullable: true),
                    experience_availability_id = table.Column<Guid>(type: "uuid", nullable: true),
                    package_availability_id = table.Column<Guid>(type: "uuid", nullable: true),
                    travelers = table.Column<int>(type: "integer", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    subtotal = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "PENDING_PAYMENT"),
                    day_number = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reservation_items", x => x.id);
                    table.CheckConstraint("ck_reservation_items_amounts", "unit_price >= 0 AND subtotal >= 0");
                    table.CheckConstraint("ck_reservation_items_currency", "currency ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("ck_reservation_items_product_shape", "(product_type = 'EXPERIENCE' AND experience_id IS NOT NULL AND package_id IS NULL AND experience_availability_id IS NOT NULL AND package_availability_id IS NULL) OR (product_type = 'PACKAGE' AND package_id IS NOT NULL AND experience_id IS NULL AND package_availability_id IS NOT NULL AND experience_availability_id IS NULL)");
                    table.CheckConstraint("ck_reservation_items_travelers", "travelers > 0");
                    table.ForeignKey(
                        name: "FK_reservation_items_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reservation_items_experience_availabilities_experience_avai~",
                        column: x => x.experience_availability_id,
                        principalTable: "experience_availabilities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reservation_items_experiences_experience_id",
                        column: x => x.experience_id,
                        principalTable: "experiences",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reservation_items_reservations_reservation_id",
                        column: x => x.reservation_id,
                        principalTable: "reservations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_reservation_items_company_status",
                table: "reservation_items",
                columns: new[] { "company_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_reservation_items_experience_availability",
                table: "reservation_items",
                column: "experience_availability_id");

            migrationBuilder.CreateIndex(
                name: "IX_reservation_items_experience_id",
                table: "reservation_items",
                column: "experience_id");

            migrationBuilder.CreateIndex(
                name: "ix_reservation_items_package_availability",
                table: "reservation_items",
                column: "package_availability_id");

            migrationBuilder.CreateIndex(
                name: "ix_reservation_items_reservation_id",
                table: "reservation_items",
                column: "reservation_id");

            migrationBuilder.CreateIndex(
                name: "ix_reservations_ai_itinerary_id",
                table: "reservations",
                column: "ai_itinerary_id");

            migrationBuilder.CreateIndex(
                name: "ix_reservations_status",
                table: "reservations",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_reservations_tourist_id",
                table: "reservations",
                column: "tourist_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reservation_items");

            migrationBuilder.DropTable(
                name: "reservations");
        }
    }
}
