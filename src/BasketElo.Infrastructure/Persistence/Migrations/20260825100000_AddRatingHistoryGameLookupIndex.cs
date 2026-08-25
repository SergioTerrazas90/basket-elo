using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260825100000_AddRatingHistoryGameLookupIndex")]
public partial class AddRatingHistoryGameLookupIndex : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_rating_history_EloPoolKey_RulesetVersion_GameId",
            table: "rating_history",
            columns: new[] { "EloPoolKey", "RulesetVersion", "GameId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_rating_history_EloPoolKey_RulesetVersion_GameId",
            table: "rating_history");
    }
}
