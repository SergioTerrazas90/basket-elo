using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260825091911_OptimizeRatingHistoryReads")]
public partial class OptimizeRatingHistoryReads : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_rating_history_EloPoolKey_GameDateTimeUtc_TeamId",
            table: "rating_history",
            columns: new[] { "EloPoolKey", "GameDateTimeUtc", "TeamId" });

        migrationBuilder.CreateIndex(
            name: "IX_rating_history_EloPoolKey_RulesetVersion_TeamId_GameDateTimeUtc_Id",
            table: "rating_history",
            columns: new[] { "EloPoolKey", "RulesetVersion", "TeamId", "GameDateTimeUtc", "Id" },
            descending: new[] { false, false, false, true, true })
            .Annotation("Npgsql:IndexInclude", new[] { "EloDelta", "OpponentTeamId", "ActualScore" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_rating_history_EloPoolKey_RulesetVersion_TeamId_GameDateTimeUtc_Id",
            table: "rating_history");

        migrationBuilder.DropIndex(
            name: "IX_rating_history_EloPoolKey_GameDateTimeUtc_TeamId",
            table: "rating_history");
    }
}
