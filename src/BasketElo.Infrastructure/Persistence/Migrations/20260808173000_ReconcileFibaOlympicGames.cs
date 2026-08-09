using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260808173000_ReconcileFibaOlympicGames")]
public partial class ReconcileFibaOlympicGames : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            WITH candidate_pairs AS (
                SELECT g."Id" AS gsa_id,
                       f."Id" AS fiba_id,
                       COUNT(*) OVER (PARTITION BY g."Id") AS gsa_match_count,
                       COUNT(*) OVER (PARTITION BY f."Id") AS fiba_match_count
                FROM games g
                JOIN competitions gc ON gc."Id" = g."CompetitionId"
                JOIN seasons gs ON gs."Id" = g."SeasonId"
                JOIN teams g_home ON g_home."Id" = g."HomeTeamId"
                JOIN teams g_away ON g_away."Id" = g."AwayTeamId"
                JOIN games f
                  ON f."Source" = 'fiba'
                 AND f."CompetitionId" = g."CompetitionId"
                 AND f."SeasonId" = g."SeasonId"
                 AND f."GameDateTimeUtc"::date = g."GameDateTimeUtc"::date
                 AND f."HomeScore" = g."HomeScore"
                 AND f."AwayScore" = g."AwayScore"
                JOIN teams f_home ON f_home."Id" = f."HomeTeamId"
                JOIN teams f_away ON f_away."Id" = f."AwayTeamId"
                 AND f_home."CountryCode" = g_home."CountryCode"
                 AND f_away."CountryCode" = g_away."CountryCode"
                WHERE g."Source" = 'global-sports-archive'
                  AND gc."Name" IN ('Summer Olympics', 'Olympics Qualification', 'Olympics Pre-Qualification')
            )
            DELETE FROM games g
            USING candidate_pairs pair
            WHERE g."Id" = pair.gsa_id
              AND pair.gsa_match_count = 1
              AND pair.fiba_match_count = 1;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Duplicate source rows are intentionally not recreated.
    }
}
