using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260808180000_MergeOlympicPreQualificationCompetition")]
public partial class MergeOlympicPreQualificationCompetition : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            DECLARE
                canonical_competition_id uuid := md5('competition:olympics-pre-qualification')::uuid;
                duplicate_competition_id uuid;
            BEGIN
                SELECT c."Id"
                INTO duplicate_competition_id
                FROM competitions c
                WHERE c."Name" = 'Olympics Pre-Qualification'
                  AND c."CountryCode" IS NULL
                  AND c."Id" <> canonical_competition_id
                  AND EXISTS (
                      SELECT 1
                      FROM games g
                      WHERE g."CompetitionId" = c."Id"
                        AND g."Source" = 'fiba'
                  )
                LIMIT 1;

                IF duplicate_competition_id IS NULL THEN
                    RETURN;
                END IF;

                INSERT INTO seasons
                    ("Id", "CompetitionId", "Label", "StartDateUtc", "EndDateUtc", "CreatedAtUtc")
                SELECT md5('season:olympics-pre-qualification:' || s."Label")::uuid,
                       canonical_competition_id,
                       s."Label",
                       s."StartDateUtc",
                       s."EndDateUtc",
                       CURRENT_TIMESTAMP
                FROM seasons s
                WHERE s."CompetitionId" = duplicate_competition_id
                  AND NOT EXISTS (
                      SELECT 1
                      FROM seasons target
                      WHERE target."CompetitionId" = canonical_competition_id
                        AND target."Label" = s."Label"
                  );

                UPDATE games g
                SET "CompetitionId" = canonical_competition_id,
                    "SeasonId" = target."Id"
                FROM seasons old_season
                JOIN seasons target
                  ON target."CompetitionId" = canonical_competition_id
                 AND target."Label" = old_season."Label"
                WHERE g."CompetitionId" = duplicate_competition_id
                  AND old_season."Id" = g."SeasonId";

                UPDATE competition_aliases
                SET "CompetitionId" = canonical_competition_id
                WHERE "CompetitionId" = duplicate_competition_id;

                UPDATE identity_health_check_runs
                SET "CompetitionId" = canonical_competition_id
                WHERE "CompetitionId" = duplicate_competition_id;

                UPDATE identity_health_check_findings
                SET "CompetitionId" = canonical_competition_id
                WHERE "CompetitionId" = duplicate_competition_id;

                UPDATE model_lab_run_scopes
                SET "CompetitionId" = canonical_competition_id
                WHERE "CompetitionId" = duplicate_competition_id;

                UPDATE model_lab_run_predictions
                SET "CompetitionId" = canonical_competition_id
                WHERE "CompetitionId" = duplicate_competition_id;

                DELETE FROM seasons
                WHERE "CompetitionId" = duplicate_competition_id;

                DELETE FROM competitions
                WHERE "Id" = duplicate_competition_id;
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The duplicate competition is intentionally not recreated.
    }
}
