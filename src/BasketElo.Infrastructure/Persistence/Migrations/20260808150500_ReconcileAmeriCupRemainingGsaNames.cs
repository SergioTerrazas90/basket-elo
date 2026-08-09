using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260808150500_ReconcileAmeriCupRemainingGsaNames")]
public partial class ReconcileAmeriCupRemainingGsaNames : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            DECLARE
                reconciled_count integer;
            BEGIN
                CREATE TEMP TABLE fiba_americup_remaining_name_candidates ON COMMIT DROP AS
                WITH fiba AS (
                    SELECT g."Id" AS game_id, s."Label" AS season, g."GameDateTimeUtc"::date AS game_date,
                           CASE WHEN lower(regexp_replace(ht."CanonicalName", '[^[:alnum:]]', '', 'g')) IN ('antigua', 'antiguaandbarbuda') THEN 'antiguaandbarbuda'
                                ELSE lower(regexp_replace(ht."CanonicalName", '[^[:alnum:]]', '', 'g')) END AS home_key,
                           CASE WHEN lower(regexp_replace(at."CanonicalName", '[^[:alnum:]]', '', 'g')) IN ('antigua', 'antiguaandbarbuda') THEN 'antiguaandbarbuda'
                                ELSE lower(regexp_replace(at."CanonicalName", '[^[:alnum:]]', '', 'g')) END AS away_key,
                           g."HomeScore" AS home_score, g."AwayScore" AS away_score
                    FROM games g
                    JOIN competitions c ON c."Id" = g."CompetitionId"
                    JOIN seasons s ON s."Id" = g."SeasonId"
                    JOIN teams ht ON ht."Id" = g."HomeTeamId"
                    JOIN teams at ON at."Id" = g."AwayTeamId"
                    WHERE g."Source" = 'fiba'
                      AND c."Name" = 'FIBA AmeriCup Pre-Qualifiers'
                ), gsa AS (
                    SELECT g."Id" AS game_id, s."Label" AS season, g."GameDateTimeUtc"::date AS game_date,
                           CASE WHEN lower(regexp_replace(ht."CanonicalName", '[^[:alnum:]]', '', 'g')) IN ('antigua', 'antiguaandbarbuda') THEN 'antiguaandbarbuda'
                                ELSE lower(regexp_replace(ht."CanonicalName", '[^[:alnum:]]', '', 'g')) END AS home_key,
                           CASE WHEN lower(regexp_replace(at."CanonicalName", '[^[:alnum:]]', '', 'g')) IN ('antigua', 'antiguaandbarbuda') THEN 'antiguaandbarbuda'
                                ELSE lower(regexp_replace(at."CanonicalName", '[^[:alnum:]]', '', 'g')) END AS away_key,
                           g."HomeScore" AS home_score, g."AwayScore" AS away_score
                    FROM games g
                    JOIN competitions c ON c."Id" = g."CompetitionId"
                    JOIN seasons s ON s."Id" = g."SeasonId"
                    JOIN teams ht ON ht."Id" = g."HomeTeamId"
                    JOIN teams at ON at."Id" = g."AwayTeamId"
                    WHERE g."Source" = 'global-sports-archive'
                      AND NOT g."HasManualResultOverride"
                      AND c."Name" = 'FIBA AmeriCup Pre-Qualifiers'
                ), candidates AS (
                    SELECT fiba.game_id AS fiba_id,
                           gsa.game_id AS gsa_id,
                           abs(fiba.game_date - gsa.game_date) AS date_distance,
                           CASE WHEN (fiba.home_score = gsa.home_score AND fiba.away_score = gsa.away_score)
                                      OR (fiba.home_score = gsa.away_score AND fiba.away_score = gsa.home_score)
                                THEN 1 ELSE 0 END AS score_match
                    FROM fiba
                    JOIN gsa ON gsa.season = fiba.season
                       AND abs(fiba.game_date - gsa.game_date) <= 31
                       AND ((fiba.home_key = gsa.home_key AND fiba.away_key = gsa.away_key)
                            OR (fiba.home_key = gsa.away_key AND fiba.away_key = gsa.home_key))
                ), ranked AS (
                    SELECT *,
                           rank() OVER (PARTITION BY gsa_id ORDER BY date_distance, score_match DESC) AS gsa_rank,
                           rank() OVER (PARTITION BY fiba_id ORDER BY date_distance, score_match DESC) AS fiba_rank
                    FROM candidates
                )
                SELECT fiba_id, gsa_id
                FROM ranked
                WHERE gsa_rank = 1
                  AND fiba_rank = 1;

                IF EXISTS (
                    SELECT 1 FROM fiba_americup_remaining_name_candidates
                    GROUP BY fiba_id HAVING count(*) > 1
                ) OR EXISTS (
                    SELECT 1 FROM fiba_americup_remaining_name_candidates
                    GROUP BY gsa_id HAVING count(*) > 1
                ) THEN
                    RAISE EXCEPTION 'AmeriCup remaining-name reconciliation is not one-to-one; no rows were removed';
                END IF;

                DELETE FROM games gsa_game
                USING fiba_americup_remaining_name_candidates candidate
                WHERE gsa_game."Id" = candidate.gsa_id
                  AND gsa_game."Source" = 'global-sports-archive'
                  AND NOT gsa_game."HasManualResultOverride";

                GET DIAGNOSTICS reconciled_count = ROW_COUNT;
                RAISE NOTICE 'Removed % remaining AmeriCup GSA name-variant duplicates; FIBA rows remain canonical', reconciled_count;
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Reconciled GSA rows are not restored automatically; the verified backup is the rollback path.
    }
}
