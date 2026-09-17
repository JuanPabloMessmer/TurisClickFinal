using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TurisClick.Api.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class _0009_Oleada9_DestinationImagesAndTouristPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "image_url",
                table: "destinations",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "travel_pace",
                table: "ai_conversations",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "tourist_preferences",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    travel_pace = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    travel_party = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    budget_level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    onboarding_completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tourist_preferences", x => x.user_id);
                    table.ForeignKey(
                        name: "FK_tourist_preferences_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tourist_preference_categories",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tourist_preference_categories", x => new { x.user_id, x.category_id });
                    table.ForeignKey(
                        name: "FK_tourist_preference_categories_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_tourist_preference_categories_tourist_preferences_user_id",
                        column: x => x.user_id,
                        principalTable: "tourist_preferences",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tourist_preference_categories_category_id",
                table: "tourist_preference_categories",
                column: "category_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tourist_preference_categories");

            migrationBuilder.DropTable(
                name: "tourist_preferences");

            migrationBuilder.DropColumn(
                name: "image_url",
                table: "destinations");

            migrationBuilder.DropColumn(
                name: "travel_pace",
                table: "ai_conversations");
        }
    }
}
