using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260823100000_ConsolidateWorldCupPreQualifierSourceAndRename")]
public partial class ConsolidateWorldCupPreQualifierSourceAndRename : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            DECLARE
                world_cup_id uuid;
                qualifier_id uuid;
                pre_qualifier_id uuid;
                duplicate_id uuid;
                candidate_count integer;
                deleted_count integer;
            BEGIN
                SELECT "Id" INTO world_cup_id
                FROM competitions
                WHERE "Name" = 'FIBA Basketball World Cup'
                  AND "CountryCode" IS NULL
                ORDER BY "CreatedAtUtc", "Id"
                LIMIT 1;

                SELECT "Id" INTO qualifier_id
                FROM competitions
                WHERE "Name" = 'FIBA Basketball World Cup Qualifiers'
                  AND "CountryCode" IS NULL
                ORDER BY "CreatedAtUtc", "Id"
                LIMIT 1;

                SELECT "Id" INTO pre_qualifier_id
                FROM competitions
                WHERE "Name" = 'FIBA Basketball World Cup Pre-Qualifiers'
                  AND "CountryCode" IS NULL
                ORDER BY "CreatedAtUtc", "Id"
                LIMIT 1;

                SELECT "Id" INTO duplicate_id
                FROM competitions
                WHERE "Name" = 'FIBA WC Qualification'
                  AND "CountryCode" IS NULL
                ORDER BY "CreatedAtUtc", "Id"
                LIMIT 1;

                IF world_cup_id IS NULL OR qualifier_id IS NULL OR pre_qualifier_id IS NULL OR duplicate_id IS NULL THEN
                    RAISE EXCEPTION 'Expected World Cup competition set was not found.';
                END IF;

                IF EXISTS (
                    SELECT 1
                    FROM games g
                    JOIN model_lab_run_predictions p ON p."GameId" = g."Id"
                    WHERE g."CompetitionId" = duplicate_id
                ) THEN
                    RAISE EXCEPTION 'FIBA WC Qualification has model-lab predictions; manual reconciliation is required.';
                END IF;

                IF EXISTS (
                    SELECT 1
                    FROM current_result_reviews r
                    JOIN games g ON g."Id" = r."AssignedGameId"
                    WHERE g."CompetitionId" = duplicate_id
                ) THEN
                    RAISE EXCEPTION 'FIBA WC Qualification has assigned current-result reviews; manual reconciliation is required.';
                END IF;

                CREATE TEMP TABLE world_cup_pre_qualifier_duplicates ON COMMIT DROP AS
                SELECT gsa."Id" AS duplicate_game_id,
                       fiba."Id" AS canonical_game_id
                FROM games gsa
                JOIN seasons gsa_season ON gsa_season."Id" = gsa."SeasonId"
                JOIN games fiba
                  ON fiba."CompetitionId" = pre_qualifier_id
                 AND fiba."HomeTeamId" = gsa."HomeTeamId"
                 AND fiba."AwayTeamId" = gsa."AwayTeamId"
                 AND fiba."HomeScore" IS NOT DISTINCT FROM gsa."HomeScore"
                 AND fiba."AwayScore" IS NOT DISTINCT FROM gsa."AwayScore"
                 AND fiba."GameDateTimeUtc"::date BETWEEN gsa."GameDateTimeUtc"::date - 2
                                                        AND gsa."GameDateTimeUtc"::date + 2
                JOIN seasons fiba_season
                  ON fiba_season."Id" = fiba."SeasonId"
                 AND fiba_season."Label" = gsa_season."Label"
                WHERE gsa."CompetitionId" = duplicate_id;

                SELECT count(*) INTO candidate_count
                FROM games
                WHERE "CompetitionId" = duplicate_id;

                IF candidate_count <> (SELECT count(*) FROM world_cup_pre_qualifier_duplicates) THEN
                    RAISE EXCEPTION 'Not every FIBA WC Qualification game matches a pre-qualifier game (% of %).',
                        (SELECT count(*) FROM world_cup_pre_qualifier_duplicates), candidate_count;
                END IF;

                IF EXISTS (
                    SELECT 1
                    FROM world_cup_pre_qualifier_duplicates
                    GROUP BY duplicate_game_id
                    HAVING count(*) > 1
                ) OR EXISTS (
                    SELECT 1
                    FROM world_cup_pre_qualifier_duplicates
                    GROUP BY canonical_game_id
                    HAVING count(*) > 1
                ) THEN
                    RAISE EXCEPTION 'World Cup pre-qualifier reconciliation is not one-to-one.';
                END IF;

                IF EXISTS (
                    SELECT 1
                    FROM games
                    WHERE "CompetitionId" = duplicate_id
                      AND "HasManualResultOverride"
                ) THEN
                    RAISE EXCEPTION 'FIBA WC Qualification contains manual-result games; no rows were removed.';
                END IF;

                DELETE FROM competition_aliases
                WHERE "CompetitionId" = duplicate_id
                  AND EXISTS (
                      SELECT 1
                      FROM competition_aliases existing
                      WHERE existing."CompetitionId" = pre_qualifier_id
                        AND existing."Source" = competition_aliases."Source"
                        AND existing."SourceCompetitionId" = competition_aliases."SourceCompetitionId"
                  );

                UPDATE competition_aliases
                SET "CompetitionId" = pre_qualifier_id
                WHERE "CompetitionId" = duplicate_id;

                DELETE FROM games
                WHERE "CompetitionId" = duplicate_id;
                GET DIAGNOSTICS deleted_count = ROW_COUNT;

                DELETE FROM seasons
                WHERE "CompetitionId" = duplicate_id;

                DELETE FROM competitions
                WHERE "Id" = duplicate_id;

                UPDATE competitions
                SET "Name" = 'FIBA World Cup'
                WHERE "Id" = world_cup_id;

                UPDATE competitions
                SET "Name" = 'FIBA World Cup Qualifiers'
                WHERE "Id" = qualifier_id;

                UPDATE competitions
                SET "Name" = 'FIBA World Cup Pre-Qualifiers'
                WHERE "Id" = pre_qualifier_id;

                RAISE NOTICE 'Removed % duplicate GSA World Cup pre-qualifier games and renamed the three canonical competitions.', deleted_count;
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The verified production backup is the rollback path for deleted duplicate rows.
        migrationBuilder.Sql("""
            UPDATE competitions SET "Name" = 'FIBA Basketball World Cup'
            WHERE "Name" = 'FIBA World Cup';
            UPDATE competitions SET "Name" = 'FIBA Basketball World Cup Qualifiers'
            WHERE "Name" = 'FIBA World Cup Qualifiers';
            UPDATE competitions SET "Name" = 'FIBA Basketball World Cup Pre-Qualifiers'
            WHERE "Name" = 'FIBA World Cup Pre-Qualifiers';
            """);
    }
}
