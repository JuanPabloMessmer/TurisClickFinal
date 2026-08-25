using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TurisClick.Api.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class _0003_Oleada2_Experiencias : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "experiences",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    destination_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    includes_text = table.Column<string>(type: "text", nullable: true),
                    excludes_text = table.Column<string>(type: "text", nullable: true),
                    duration_minutes = table.Column<int>(type: "integer", nullable: true),
                    duration_label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    price = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "DRAFT"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_experiences", x => x.id);
                    table.CheckConstraint("ck_experiences_currency", "currency ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("ck_experiences_duration", "duration_minutes IS NULL OR duration_minutes > 0");
                    table.CheckConstraint("ck_experiences_price", "price >= 0");
                    table.ForeignKey(
                        name: "FK_experiences_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_experiences_destinations_destination_id",
                        column: x => x.destination_id,
                        principalTable: "destinations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "experience_availabilities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    experience_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time", nullable: true),
                    total_slots = table.Column<int>(type: "integer", nullable: false),
                    reserved_slots = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "OPEN")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_experience_availabilities", x => x.id);
                    table.CheckConstraint("ck_experience_availabilities_slots", "total_slots > 0 AND reserved_slots >= 0 AND reserved_slots <= total_slots");
                    table.ForeignKey(
                        name: "FK_experience_availabilities_experiences_experience_id",
                        column: x => x.experience_id,
                        principalTable: "experiences",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "experience_categories",
                columns: table => new
                {
                    experience_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_experience_categories", x => new { x.experience_id, x.category_id });
                    table.ForeignKey(
                        name: "FK_experience_categories_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_experience_categories_experiences_experience_id",
                        column: x => x.experience_id,
                        principalTable: "experiences",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "experience_images",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    experience_id = table.Column<Guid>(type: "uuid", nullable: false),
                    url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    is_cover = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_experience_images", x => x.id);
                    table.ForeignKey(
                        name: "FK_experience_images_experiences_experience_id",
                        column: x => x.experience_id,
                        principalTable: "experiences",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_experience_availabilities_experience_date",
                table: "experience_availabilities",
                columns: new[] { "experience_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ux_experience_availabilities_fullday",
                table: "experience_availabilities",
                columns: new[] { "experience_id", "date" },
                unique: true,
                filter: "start_time IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_experience_availabilities_timed",
                table: "experience_availabilities",
                columns: new[] { "experience_id", "date", "start_time" },
                unique: true,
                filter: "start_time IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_experience_categories_category_id",
                table: "experience_categories",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_experience_images_experience_id",
                table: "experience_images",
                column: "experience_id");

            migrationBuilder.CreateIndex(
                name: "ux_experience_images_one_cover",
                table: "experience_images",
                column: "experience_id",
                unique: true,
                filter: "is_cover = true");

            migrationBuilder.CreateIndex(
                name: "ix_experiences_company_id",
                table: "experiences",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_experiences_destination_id",
                table: "experiences",
                column: "destination_id");

            migrationBuilder.CreateIndex(
                name: "ix_experiences_status_destination",
                table: "experiences",
                columns: new[] { "status", "destination_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "experience_availabilities");

            migrationBuilder.DropTable(
                name: "experience_categories");

            migrationBuilder.DropTable(
                name: "experience_images");

            migrationBuilder.DropTable(
                name: "experiences");
        }
    }
}
