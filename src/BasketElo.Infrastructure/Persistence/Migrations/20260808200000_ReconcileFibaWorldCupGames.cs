using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260808200000_ReconcileFibaWorldCupGames")]
public partial class ReconcileFibaWorldCupGames : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            DECLARE
                reconciled_count integer;
            BEGIN
                CREATE TEMP TABLE fiba_world_cup_reconciliation_candidates ON COMMIT DROP AS
                SELECT fiba_game."Id" AS fiba_id,
                       gsa_game."Id" AS gsa_id
                FROM games fiba_game
                JOIN competitions fiba_competition
                  ON fiba_competition."Id" = fiba_game."CompetitionId"
                JOIN seasons fiba_season
                  ON fiba_season."Id" = fiba_game."SeasonId"
                JOIN teams fiba_home
                  ON fiba_home."Id" = fiba_game."HomeTeamId"
                JOIN teams fiba_away
                  ON fiba_away."Id" = fiba_game."AwayTeamId"
                JOIN games gsa_game
                  ON gsa_game."Source" = 'global-sports-archive'
                 AND gsa_game."GameDateTimeUtc"::date BETWEEN fiba_game."GameDateTimeUtc"::date - 1
                                                          AND fiba_game."GameDateTimeUtc"::date + 1
                 AND gsa_game."HomeScore" = fiba_game."HomeScore"
                 AND gsa_game."AwayScore" = fiba_game."AwayScore"
                JOIN competitions gsa_competition
                  ON gsa_competition."Id" = gsa_game."CompetitionId"
                JOIN seasons gsa_season
                  ON gsa_season."Id" = gsa_game."SeasonId"
                 AND gsa_season."Label" = fiba_season."Label"
                JOIN teams gsa_home
                  ON gsa_home."Id" = gsa_game."HomeTeamId"
                 AND gsa_home."CountryCode" = fiba_home."CountryCode"
                JOIN teams gsa_away
                  ON gsa_away."Id" = gsa_game."AwayTeamId"
                 AND gsa_away."CountryCode" = fiba_away."CountryCode"
                WHERE fiba_game."Source" = 'fiba'
                  AND (
                        (fiba_competition."Name" = 'FIBA Basketball World Cup'
                         AND gsa_competition."Name" = 'FIBA Basketball World Cup')
                     OR (fiba_competition."Name" = 'FIBA Basketball World Cup Qualifiers'
                         AND gsa_competition."Name" = 'FIBA WC Qualification')
                  );

                IF EXISTS (
                    SELECT 1
                    FROM fiba_world_cup_reconciliation_candidates
                    GROUP BY fiba_id
                    HAVING count(*) > 1
                ) OR EXISTS (
                    SELECT 1
                    FROM fiba_world_cup_reconciliation_candidates
                    GROUP BY gsa_id
                    HAVING count(*) > 1
                ) THEN
                    RAISE EXCEPTION 'FIBA World Cup reconciliation is not one-to-one; no rows were removed';
                END IF;

                DELETE FROM games gsa_game
                USING fiba_world_cup_reconciliation_candidates candidate
                WHERE gsa_game."Id" = candidate.gsa_id
                  AND gsa_game."Source" = 'global-sports-archive'
                  AND NOT gsa_game."HasManualResultOverride";

                GET DIAGNOSTICS reconciled_count = ROW_COUNT;
                RAISE NOTICE 'Removed % exact World Cup GSA duplicates; FIBA rows remain canonical', reconciled_count;
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Exact GSA duplicates are intentionally not recreated automatically;
        // the verified backup is the rollback path.
    }
}
