using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260813110000_ReconcileNationalTeamIdentitiesAndInvalidDates")]
public partial class ReconcileNationalTeamIdentitiesAndInvalidDates : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            WITH national_team_ids AS (
                SELECT g."HomeTeamId" AS "TeamId"
                FROM games g
                INNER JOIN competitions c ON c."Id" = g."CompetitionId"
                WHERE c."EloPoolKey" = 'national-teams'
                UNION
                SELECT g."AwayTeamId" AS "TeamId"
                FROM games g
                INNER JOIN competitions c ON c."Id" = g."CompetitionId"
                WHERE c."EloPoolKey" = 'national-teams'
            )
            UPDATE teams t
            SET "CanonicalName" = CASE
                    WHEN UPPER(TRIM(t."CanonicalName")) IN ('PRK', 'KOREA DPR', 'NORTH KOREA')
                         OR UPPER(TRIM(t."CountryCode")) IN ('PRK', 'KP')
                         OR EXISTS (
                             SELECT 1 FROM team_aliases a
                             WHERE a."TeamId" = t."Id"
                               AND (UPPER(TRIM(a."SourceTeamId")) = 'PRK'
                                    OR UPPER(TRIM(a."AliasName")) IN ('KOREA DPR', 'NORTH KOREA')))
                        THEN 'North Korea'
                    WHEN UPPER(TRIM(t."CanonicalName")) IN ('TAI', 'TAIWAN', 'CHINESE TAIPEI')
                         OR UPPER(TRIM(t."CountryCode")) IN ('TAI', 'TPE', 'TW')
                         OR EXISTS (
                             SELECT 1 FROM team_aliases a
                             WHERE a."TeamId" = t."Id"
                               AND (UPPER(TRIM(a."SourceTeamId")) IN ('TAI', 'TPE')
                                    OR UPPER(TRIM(a."AliasName")) IN ('TAIWAN', 'CHINESE TAIPEI')))
                        THEN 'Chinese Taipei'
                    WHEN UPPER(TRIM(t."CanonicalName")) IN ('KGZ', 'KYRGYZ REPUBLIC')
                         OR UPPER(TRIM(t."CountryCode")) IN ('KGZ', 'KG')
                        THEN 'Kyrgyz Republic'
                    WHEN UPPER(TRIM(t."CanonicalName")) IN ('PAK', 'PAKISTAN')
                         OR UPPER(TRIM(t."CountryCode")) IN ('PAK', 'PK')
                        THEN 'Pakistan'
                    WHEN UPPER(TRIM(t."CanonicalName")) IN ('USSR', 'SOVIET UNION')
                         OR UPPER(TRIM(t."CountryCode")) IN ('URS', 'USSR')
                        THEN 'Soviet Union'
                    ELSE t."CanonicalName"
                END,
                "CountryCode" = CASE
                    WHEN UPPER(TRIM(t."CanonicalName")) IN ('PRK', 'KOREA DPR', 'NORTH KOREA')
                         OR UPPER(TRIM(t."CountryCode")) IN ('PRK', 'KP')
                         OR EXISTS (
                             SELECT 1 FROM team_aliases a
                             WHERE a."TeamId" = t."Id"
                               AND (UPPER(TRIM(a."SourceTeamId")) = 'PRK'
                                    OR UPPER(TRIM(a."AliasName")) IN ('KOREA DPR', 'NORTH KOREA')))
                        THEN 'KP'
                    WHEN UPPER(TRIM(t."CanonicalName")) IN ('TAI', 'TAIWAN', 'CHINESE TAIPEI')
                         OR UPPER(TRIM(t."CountryCode")) IN ('TAI', 'TPE', 'TW')
                         OR EXISTS (
                             SELECT 1 FROM team_aliases a
                             WHERE a."TeamId" = t."Id"
                               AND (UPPER(TRIM(a."SourceTeamId")) IN ('TAI', 'TPE')
                                    OR UPPER(TRIM(a."AliasName")) IN ('TAIWAN', 'CHINESE TAIPEI')))
                        THEN 'TW'
                    WHEN UPPER(TRIM(t."CanonicalName")) IN ('KGZ', 'KYRGYZ REPUBLIC')
                         OR UPPER(TRIM(t."CountryCode")) IN ('KGZ', 'KG')
                        THEN 'KG'
                    WHEN UPPER(TRIM(t."CanonicalName")) IN ('PAK', 'PAKISTAN')
                         OR UPPER(TRIM(t."CountryCode")) IN ('PAK', 'PK')
                        THEN 'PK'
                    WHEN UPPER(TRIM(t."CanonicalName")) IN ('USSR', 'SOVIET UNION')
                         OR UPPER(TRIM(t."CountryCode")) IN ('URS', 'USSR')
                        THEN 'URS'
                    ELSE t."CountryCode"
                END
            FROM national_team_ids n
            WHERE t."Id" = n."TeamId";
            """);

        migrationBuilder.Sql("""
            DO $$
            DECLARE merge_record record;
            BEGIN
                FOR merge_record IN
                    WITH national_team_stats AS (
                        SELECT t."Id", t."CanonicalName", count(*) AS "NationalGames"
                        FROM teams t
                        INNER JOIN (
                            SELECT "HomeTeamId" AS "TeamId", "CompetitionId" FROM games
                            UNION ALL
                            SELECT "AwayTeamId" AS "TeamId", "CompetitionId" FROM games
                        ) appearances ON appearances."TeamId" = t."Id"
                        INNER JOIN competitions c ON c."Id" = appearances."CompetitionId"
                        WHERE c."EloPoolKey" = 'national-teams'
                        GROUP BY t."Id", t."CanonicalName"
                    ), selected_targets AS (
                        SELECT duplicate."Id" AS duplicate_id,
                               (
                                   SELECT target."Id"
                                   FROM national_team_stats target
                                   WHERE target."CanonicalName" = duplicate."CanonicalName"
                                   ORDER BY target."NationalGames" DESC, target."Id"
                                   LIMIT 1
                               ) AS target_id
                        FROM national_team_stats duplicate
                    )
                    SELECT duplicate_id, target_id
                    FROM selected_targets
                    WHERE duplicate_id <> target_id
                LOOP
                    DELETE FROM team_ratings duplicate_rating
                    WHERE duplicate_rating."TeamId" = merge_record.duplicate_id
                      AND EXISTS (
                          SELECT 1 FROM team_ratings canonical_rating
                          WHERE canonical_rating."TeamId" = merge_record.target_id
                            AND canonical_rating."EloPoolKey" = duplicate_rating."EloPoolKey"
                            AND canonical_rating."RulesetVersion" = duplicate_rating."RulesetVersion");

                    DELETE FROM model_lab_run_ratings duplicate_rating
                    WHERE duplicate_rating."TeamId" = merge_record.duplicate_id
                      AND EXISTS (
                          SELECT 1 FROM model_lab_run_ratings canonical_rating
                          WHERE canonical_rating."TeamId" = merge_record.target_id
                            AND canonical_rating."RunId" = duplicate_rating."RunId");

                    DELETE FROM team_aliases duplicate_alias
                    WHERE duplicate_alias."TeamId" = merge_record.duplicate_id
                      AND EXISTS (
                          SELECT 1 FROM team_aliases canonical_alias
                          WHERE canonical_alias."TeamId" = merge_record.target_id
                            AND canonical_alias."Source" = duplicate_alias."Source"
                            AND canonical_alias."SourceTeamId" = duplicate_alias."SourceTeamId"
                            AND canonical_alias."AliasName" = duplicate_alias."AliasName");

                    UPDATE games
                    SET "HomeTeamId" = CASE WHEN "HomeTeamId" = merge_record.duplicate_id THEN merge_record.target_id ELSE "HomeTeamId" END,
                        "AwayTeamId" = CASE WHEN "AwayTeamId" = merge_record.duplicate_id THEN merge_record.target_id ELSE "AwayTeamId" END
                    WHERE "HomeTeamId" = merge_record.duplicate_id OR "AwayTeamId" = merge_record.duplicate_id;

                    UPDATE rating_history
                    SET "TeamId" = CASE WHEN "TeamId" = merge_record.duplicate_id THEN merge_record.target_id ELSE "TeamId" END,
                        "OpponentTeamId" = CASE WHEN "OpponentTeamId" = merge_record.duplicate_id THEN merge_record.target_id ELSE "OpponentTeamId" END
                    WHERE "TeamId" = merge_record.duplicate_id OR "OpponentTeamId" = merge_record.duplicate_id;

                    UPDATE model_lab_run_predictions
                    SET "HomeTeamId" = CASE WHEN "HomeTeamId" = merge_record.duplicate_id THEN merge_record.target_id ELSE "HomeTeamId" END,
                        "AwayTeamId" = CASE WHEN "AwayTeamId" = merge_record.duplicate_id THEN merge_record.target_id ELSE "AwayTeamId" END
                    WHERE "HomeTeamId" = merge_record.duplicate_id OR "AwayTeamId" = merge_record.duplicate_id;

                    UPDATE identity_health_check_findings
                    SET "AffectedTeamId" = CASE WHEN "AffectedTeamId" = merge_record.duplicate_id THEN merge_record.target_id ELSE "AffectedTeamId" END,
                        "RelatedTeamId" = CASE WHEN "RelatedTeamId" = merge_record.duplicate_id THEN merge_record.target_id ELSE "RelatedTeamId" END
                    WHERE "AffectedTeamId" = merge_record.duplicate_id OR "RelatedTeamId" = merge_record.duplicate_id;

                    UPDATE identity_review_decisions
                    SET "AffectedTeamId" = CASE WHEN "AffectedTeamId" = merge_record.duplicate_id THEN merge_record.target_id ELSE "AffectedTeamId" END,
                        "RelatedTeamId" = CASE WHEN "RelatedTeamId" = merge_record.duplicate_id THEN merge_record.target_id ELSE "RelatedTeamId" END
                    WHERE "AffectedTeamId" = merge_record.duplicate_id OR "RelatedTeamId" = merge_record.duplicate_id;

                    UPDATE teams
                    SET "PredecessorTeamId" = CASE WHEN "PredecessorTeamId" = merge_record.duplicate_id THEN merge_record.target_id ELSE "PredecessorTeamId" END,
                        "SuccessorTeamId" = CASE WHEN "SuccessorTeamId" = merge_record.duplicate_id THEN merge_record.target_id ELSE "SuccessorTeamId" END
                    WHERE "PredecessorTeamId" = merge_record.duplicate_id OR "SuccessorTeamId" = merge_record.duplicate_id;

                    UPDATE team_aliases SET "TeamId" = merge_record.target_id
                    WHERE "TeamId" = merge_record.duplicate_id;
                    UPDATE team_ratings SET "TeamId" = merge_record.target_id
                    WHERE "TeamId" = merge_record.duplicate_id;
                    UPDATE model_lab_run_ratings SET "TeamId" = merge_record.target_id
                    WHERE "TeamId" = merge_record.duplicate_id;
                    DELETE FROM teams WHERE "Id" = merge_record.duplicate_id;
                END LOOP;
            END $$;
            """);

        migrationBuilder.Sql("""
            WITH invalid_games AS (
                SELECT g."Id", s."StartDateUtc", s."EndDateUtc"
                FROM games g
                INNER JOIN seasons s ON s."Id" = g."SeasonId"
                INNER JOIN teams home_team ON home_team."Id" = g."HomeTeamId"
                INNER JOIN teams away_team ON away_team."Id" = g."AwayTeamId"
                WHERE g."GameDateTimeUtc" < TIMESTAMPTZ '1900-01-01'
                  AND (home_team."CanonicalName" ILIKE '%CSKA%'
                       OR away_team."CanonicalName" ILIKE '%CSKA%')
            )
            UPDATE games g
            SET "GameDateTimeUtc" = invalid_games."StartDateUtc"
                + (invalid_games."EndDateUtc" - invalid_games."StartDateUtc") / 2,
                "UpdatedAtUtc" = NOW()
            FROM invalid_games
            WHERE g."Id" = invalid_games."Id";

            UPDATE rating_history rh
            SET "GameDateTimeUtc" = g."GameDateTimeUtc"
            FROM games g
            WHERE rh."GameId" = g."Id"
              AND g."GameDateTimeUtc" >= TIMESTAMPTZ '1900-01-01'
              AND rh."GameDateTimeUtc" < TIMESTAMPTZ '1900-01-01';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Identity merges and corrected dates are intentionally not reversed.
    }
}
