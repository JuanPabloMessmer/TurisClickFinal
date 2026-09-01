using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TurisClick.Api.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class _0006_Oleada5_AiItineraries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_conversations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    tourist_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "ACTIVE"),
                    preferred_destination_id = table.Column<Guid>(type: "uuid", nullable: true),
                    start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    travelers_count = table.Column<int>(type: "integer", nullable: true),
                    budget_total = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    budget_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    duration_days = table.Column<int>(type: "integer", nullable: true),
                    restrictions_notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_conversations", x => x.id);
                    table.CheckConstraint("ck_ai_conversations_budget", "budget_total IS NULL OR budget_total >= 0");
                    table.CheckConstraint("ck_ai_conversations_currency", "budget_currency IS NULL OR budget_currency ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("ck_ai_conversations_dates", "end_date IS NULL OR start_date IS NULL OR end_date >= start_date");
                    table.CheckConstraint("ck_ai_conversations_travelers", "travelers_count IS NULL OR travelers_count > 0");
                    table.ForeignKey(
                        name: "FK_ai_conversations_destinations_preferred_destination_id",
                        column: x => x.preferred_destination_id,
                        principalTable: "destinations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ai_conversations_users_tourist_id",
                        column: x => x.tourist_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ai_conversation_categories",
                columns: table => new
                {
                    ai_conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_conversation_categories", x => new { x.ai_conversation_id, x.category_id });
                    table.ForeignKey(
                        name: "FK_ai_conversation_categories_ai_conversations_ai_conversation~",
                        column: x => x.ai_conversation_id,
                        principalTable: "ai_conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ai_conversation_categories_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ai_itineraries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ai_conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tourist_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "DRAFT"),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_itineraries", x => x.id);
                    table.ForeignKey(
                        name: "FK_ai_itineraries_ai_conversations_ai_conversation_id",
                        column: x => x.ai_conversation_id,
                        principalTable: "ai_conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ai_itineraries_users_tourist_id",
                        column: x => x.tourist_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ai_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ai_conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_messages", x => x.id);
                    table.ForeignKey(
                        name: "FK_ai_messages_ai_conversations_ai_conversation_id",
                        column: x => x.ai_conversation_id,
                        principalTable: "ai_conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ai_itinerary_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ai_itinerary_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day_number = table.Column<int>(type: "integer", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    product_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    experience_id = table.Column<Guid>(type: "uuid", nullable: true),
                    package_id = table.Column<Guid>(type: "uuid", nullable: true),
                    experience_availability_id = table.Column<Guid>(type: "uuid", nullable: true),
                    package_availability_id = table.Column<Guid>(type: "uuid", nullable: true),
                    estimated_unit_price = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_itinerary_items", x => x.id);
                    table.CheckConstraint("ck_ai_itinerary_items_currency", "currency ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("ck_ai_itinerary_items_day", "day_number >= 1");
                    table.CheckConstraint("ck_ai_itinerary_items_product_shape", "(product_type = 'EXPERIENCE' AND experience_id IS NOT NULL AND package_id IS NULL AND package_availability_id IS NULL) OR (product_type = 'PACKAGE' AND package_id IS NOT NULL AND experience_id IS NULL AND experience_availability_id IS NULL)");
                    table.ForeignKey(
                        name: "FK_ai_itinerary_items_ai_itineraries_ai_itinerary_id",
                        column: x => x.ai_itinerary_id,
                        principalTable: "ai_itineraries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ai_itinerary_items_experience_availabilities_experience_ava~",
                        column: x => x.experience_availability_id,
                        principalTable: "experience_availabilities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ai_itinerary_items_experiences_experience_id",
                        column: x => x.experience_id,
                        principalTable: "experiences",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ai_itinerary_items_package_availabilities_package_availabil~",
                        column: x => x.package_availability_id,
                        principalTable: "package_availabilities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ai_itinerary_items_packages_package_id",
                        column: x => x.package_id,
                        principalTable: "packages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ai_conversation_categories_category_id",
                table: "ai_conversation_categories",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "IX_ai_conversations_preferred_destination_id",
                table: "ai_conversations",
                column: "preferred_destination_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_conversations_tourist_id",
                table: "ai_conversations",
                column: "tourist_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_itineraries_conversation_id",
                table: "ai_itineraries",
                column: "ai_conversation_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_itineraries_status",
                table: "ai_itineraries",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_ai_itineraries_tourist_id",
                table: "ai_itineraries",
                column: "tourist_id");

            migrationBuilder.CreateIndex(
                name: "IX_ai_itinerary_items_experience_availability_id",
                table: "ai_itinerary_items",
                column: "experience_availability_id");

            migrationBuilder.CreateIndex(
                name: "IX_ai_itinerary_items_experience_id",
                table: "ai_itinerary_items",
                column: "experience_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_itinerary_items_itinerary_id",
                table: "ai_itinerary_items",
                column: "ai_itinerary_id");

            migrationBuilder.CreateIndex(
                name: "IX_ai_itinerary_items_package_availability_id",
                table: "ai_itinerary_items",
                column: "package_availability_id");

            migrationBuilder.CreateIndex(
                name: "IX_ai_itinerary_items_package_id",
                table: "ai_itinerary_items",
                column: "package_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_messages_conversation_created",
                table: "ai_messages",
                columns: new[] { "ai_conversation_id", "created_at" });

            migrationBuilder.AddForeignKey(
                name: "FK_reservations_ai_itineraries_ai_itinerary_id",
                table: "reservations",
                column: "ai_itinerary_id",
                principalTable: "ai_itineraries",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_reservations_ai_itineraries_ai_itinerary_id",
                table: "reservations");

            migrationBuilder.DropTable(
                name: "ai_conversation_categories");

            migrationBuilder.DropTable(
                name: "ai_itinerary_items");

            migrationBuilder.DropTable(
                name: "ai_messages");

            migrationBuilder.DropTable(
                name: "ai_itineraries");

            migrationBuilder.DropTable(
                name: "ai_conversations");
        }
    }
}
