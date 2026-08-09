using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260808104500_ReconcileFibaAfroBasketGames")]
public partial class ReconcileFibaAfroBasketGames : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            DECLARE
                reconciled_count integer;
            BEGIN
                CREATE TEMP TABLE fiba_afrobasket_reconciliation_candidates ON COMMIT DROP AS
                SELECT fiba_game."Id" AS fiba_id,
                       gsa_game."Id" AS gsa_id
                FROM games fiba_game
                JOIN games gsa_game
                  ON fiba_game."Source" = 'fiba'
                 AND gsa_game."Source" = 'global-sports-archive'
                 AND fiba_game."GameDateTimeUtc"::date = gsa_game."GameDateTimeUtc"::date
                 AND fiba_game."HomeTeamId" = gsa_game."HomeTeamId"
                 AND fiba_game."AwayTeamId" = gsa_game."AwayTeamId"
                 AND fiba_game."HomeScore" = gsa_game."HomeScore"
                 AND fiba_game."AwayScore" = gsa_game."AwayScore"
                JOIN competitions fiba_competition
                  ON fiba_competition."Id" = fiba_game."CompetitionId"
                JOIN competitions gsa_competition
                  ON gsa_competition."Id" = gsa_game."CompetitionId"
                JOIN seasons fiba_season
                  ON fiba_season."Id" = fiba_game."SeasonId"
                JOIN seasons gsa_season
                  ON gsa_season."Id" = gsa_game."SeasonId"
                 AND gsa_season."Label" = fiba_season."Label"
                WHERE fiba_competition."Name" = 'FIBA AfroBasket'
                  AND gsa_competition."Name" = 'FIBA AfroBasket';

                IF EXISTS (
                    SELECT 1
                    FROM fiba_afrobasket_reconciliation_candidates
                    GROUP BY fiba_id
                    HAVING count(*) > 1
                ) OR EXISTS (
                    SELECT 1
                    FROM fiba_afrobasket_reconciliation_candidates
                    GROUP BY gsa_id
                    HAVING count(*) > 1
                ) THEN
                    RAISE EXCEPTION 'FIBA AfroBasket reconciliation is not one-to-one; no rows were removed';
                END IF;

                DELETE FROM games gsa_game
                USING fiba_afrobasket_reconciliation_candidates candidate
                WHERE gsa_game."Id" = candidate.gsa_id
                  AND gsa_game."Source" = 'global-sports-archive'
                  AND NOT gsa_game."HasManualResultOverride";

                GET DIAGNOSTICS reconciled_count = ROW_COUNT;
                RAISE NOTICE 'Removed % exact AfroBasket GSA duplicates; FIBA rows remain canonical', reconciled_count;
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Exact GSA duplicates are not restored automatically; the verified backup is the rollback path.
    }
}
