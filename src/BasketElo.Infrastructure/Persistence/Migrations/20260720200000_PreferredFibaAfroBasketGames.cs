using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260720200000_PreferredFibaAfroBasketGames")]
public partial class PreferredFibaAfroBasketGames : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DELETE FROM games g
            USING seasons s, competitions c
            WHERE g."SeasonId" = s."Id"
              AND s."CompetitionId" = c."Id"
              AND c."Name" = 'FIBA AfroBasket'
              AND g."Source" = 'global-sports-archive'
              AND s."Label" <> '2003'
              AND EXISTS (
                  SELECT 1
                  FROM games fiba_game
                  WHERE fiba_game."SeasonId" = g."SeasonId"
                    AND fiba_game."Source" = 'fiba'
              );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // FIBA is the canonical source for these editions; fallback rows are not restored.
    }
}
