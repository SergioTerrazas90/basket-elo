using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260808213000_AddCurrentResultReviewResolution")]
public partial class AddCurrentResultReviewResolution : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "AssignedGameId",
            table: "current_result_reviews",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ResolutionAction",
            table: "current_result_reviews",
            type: "character varying(30)",
            maxLength: 30,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ResolutionNote",
            table: "current_result_reviews",
            type: "character varying(1000)",
            maxLength: 1000,
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "ResolvedAtUtc",
            table: "current_result_reviews",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_current_result_reviews_AssignedGameId",
            table: "current_result_reviews",
            column: "AssignedGameId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_current_result_reviews_AssignedGameId",
            table: "current_result_reviews");

        migrationBuilder.DropColumn(
            name: "AssignedGameId",
            table: "current_result_reviews");

        migrationBuilder.DropColumn(
            name: "ResolutionAction",
            table: "current_result_reviews");

        migrationBuilder.DropColumn(
            name: "ResolutionNote",
            table: "current_result_reviews");

        migrationBuilder.DropColumn(
            name: "ResolvedAtUtc",
            table: "current_result_reviews");
    }
}
