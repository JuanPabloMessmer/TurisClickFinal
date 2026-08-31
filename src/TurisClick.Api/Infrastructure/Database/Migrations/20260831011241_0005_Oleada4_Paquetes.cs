using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TurisClick.Api.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class _0005_Oleada4_Paquetes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "packages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    destination_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    conditions_text = table.Column<string>(type: "text", nullable: true),
                    duration_days = table.Column<int>(type: "integer", nullable: false),
                    price = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "DRAFT"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_packages", x => x.id);
                    table.CheckConstraint("ck_packages_currency", "currency ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("ck_packages_duration", "duration_days > 0");
                    table.CheckConstraint("ck_packages_price", "price >= 0");
                    table.ForeignKey(
                        name: "FK_packages_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_packages_destinations_destination_id",
                        column: x => x.destination_id,
                        principalTable: "destinations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "package_availabilities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    departure_date = table.Column<DateOnly>(type: "date", nullable: false),
                    total_slots = table.Column<int>(type: "integer", nullable: false),
                    reserved_slots = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "OPEN")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_package_availabilities", x => x.id);
                    table.CheckConstraint("ck_package_availabilities_slots", "total_slots > 0 AND reserved_slots >= 0 AND reserved_slots <= total_slots");
                    table.ForeignKey(
                        name: "FK_package_availabilities_packages_package_id",
                        column: x => x.package_id,
                        principalTable: "packages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "package_categories",
                columns: table => new
                {
                    package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_package_categories", x => new { x.package_id, x.category_id });
                    table.ForeignKey(
                        name: "FK_package_categories_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_package_categories_packages_package_id",
                        column: x => x.package_id,
                        principalTable: "packages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "package_images",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    is_cover = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_package_images", x => x.id);
                    table.ForeignKey(
                        name: "FK_package_images_packages_package_id",
                        column: x => x.package_id,
                        principalTable: "packages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "package_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day_number = table.Column<int>(type: "integer", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    experience_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    description = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_package_items", x => x.id);
                    table.CheckConstraint("ck_package_items_day", "day_number >= 1");
                    table.CheckConstraint("ck_package_items_descriptive_title", "kind <> 'DESCRIPTIVE' OR title IS NOT NULL");
                    table.CheckConstraint("ck_package_items_kind_shape", "(kind = 'EXPERIENCE_REFERENCE' AND experience_id IS NOT NULL) OR (kind = 'DESCRIPTIVE' AND experience_id IS NULL)");
                    table.ForeignKey(
                        name: "FK_package_items_experiences_experience_id",
                        column: x => x.experience_id,
                        principalTable: "experiences",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_package_items_packages_package_id",
                        column: x => x.package_id,
                        principalTable: "packages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_reservation_items_package_id",
                table: "reservation_items",
                column: "package_id");

            migrationBuilder.CreateIndex(
                name: "ix_package_availabilities_package_date",
                table: "package_availabilities",
                columns: new[] { "package_id", "departure_date" });

            migrationBuilder.CreateIndex(
                name: "uq_package_availabilities_departure",
                table: "package_availabilities",
                columns: new[] { "package_id", "departure_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_package_categories_category_id",
                table: "package_categories",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_package_images_package_id",
                table: "package_images",
                column: "package_id");

            migrationBuilder.CreateIndex(
                name: "ux_package_images_one_cover",
                table: "package_images",
                column: "package_id",
                unique: true,
                filter: "is_cover = true");

            migrationBuilder.CreateIndex(
                name: "ix_package_items_experience_id",
                table: "package_items",
                column: "experience_id");

            migrationBuilder.CreateIndex(
                name: "ix_package_items_package_id",
                table: "package_items",
                column: "package_id");

            migrationBuilder.CreateIndex(
                name: "ix_packages_company_id",
                table: "packages",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_packages_destination_id",
                table: "packages",
                column: "destination_id");

            migrationBuilder.CreateIndex(
                name: "ix_packages_status_destination",
                table: "packages",
                columns: new[] { "status", "destination_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_reservation_items_package_availabilities_package_availabili~",
                table: "reservation_items",
                column: "package_availability_id",
                principalTable: "package_availabilities",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_reservation_items_packages_package_id",
                table: "reservation_items",
                column: "package_id",
                principalTable: "packages",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_reservation_items_package_availabilities_package_availabili~",
                table: "reservation_items");

            migrationBuilder.DropForeignKey(
                name: "FK_reservation_items_packages_package_id",
                table: "reservation_items");

            migrationBuilder.DropTable(
                name: "package_availabilities");

            migrationBuilder.DropTable(
                name: "package_categories");

            migrationBuilder.DropTable(
                name: "package_images");

            migrationBuilder.DropTable(
                name: "package_items");

            migrationBuilder.DropTable(
                name: "packages");

            migrationBuilder.DropIndex(
                name: "IX_reservation_items_package_id",
                table: "reservation_items");
        }
    }
}
