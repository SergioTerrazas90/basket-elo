using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260808162000_SplitGsaOlympicQualificationStages")]
public partial class SplitGsaOlympicQualificationStages : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            DECLARE
                pre_qualifier_competition_id uuid := md5('competition:olympics-pre-qualification')::uuid;
            BEGIN
                INSERT INTO competitions
                    ("Id", "Name", "Type", "CountryCode", "Tier", "IsActive", "CreatedAtUtc", "EloPoolKey")
                VALUES
                    (pre_qualifier_competition_id, 'Olympics Pre-Qualification', 'qualifier', 'WOR', 1, TRUE, CURRENT_TIMESTAMP, 'national-teams')
                ON CONFLICT ("Id") DO NOTHING;

                INSERT INTO seasons
                    ("Id", "CompetitionId", "Label", "StartDateUtc", "EndDateUtc", "CreatedAtUtc")
                SELECT md5('season:olympics-pre-qualification:' || s."Label")::uuid,
                       pre_qualifier_competition_id,
                       s."Label",
                       s."StartDateUtc",
                       s."EndDateUtc",
                       CURRENT_TIMESTAMP
                FROM seasons s
                JOIN competitions c ON c."Id" = s."CompetitionId"
                WHERE c."Name" = 'Olympics Qualification'
                  AND s."Label" = '2024'
                  AND NOT EXISTS (
                      SELECT 1
                      FROM seasons target
                      WHERE target."CompetitionId" = pre_qualifier_competition_id
                        AND target."Label" = s."Label"
                  );

                INSERT INTO tournament_cycles
                    ("Id", "Key", "Family", "EditionLabel", "DisplayName", "CreatedAtUtc")
                SELECT md5('tournament-cycle:olympics:' || labels."Label")::uuid,
                       'olympics-' || labels."Label",
                       'Olympics',
                       labels."Label",
                       'Olympics ' || labels."Label",
                       CURRENT_TIMESTAMP
                FROM (
                    SELECT DISTINCT s."Label"
                    FROM seasons s
                    JOIN competitions c ON c."Id" = s."CompetitionId"
                    WHERE c."Name" IN ('Summer Olympics', 'Olympics Qualification', 'Olympics Pre-Qualification')
                ) labels
                ON CONFLICT ("Key") DO NOTHING;

                INSERT INTO competition_aliases
                    ("Id", "CompetitionId", "Source", "SourceCompetitionId", "AliasName", "CreatedAtUtc")
                VALUES
                    (md5('competition-alias:global-sports-archive:olympics-pre-qualification')::uuid,
                     pre_qualifier_competition_id,
                     'global-sports-archive',
                     'olympics-pre-qualification',
                     'Olympics Pre-Qualification',
                     CURRENT_TIMESTAMP)
                ON CONFLICT ("Id") DO NOTHING;

                UPDATE games g
                SET "CompetitionId" = pre_qualifier_competition_id,
                    "SeasonId" = target_season."Id"
                FROM seasons source_season
                JOIN competitions source_competition ON source_competition."Id" = source_season."CompetitionId"
                JOIN seasons target_season ON target_season."CompetitionId" = pre_qualifier_competition_id
                                           AND target_season."Label" = source_season."Label"
                WHERE g."SeasonId" = source_season."Id"
                  AND source_competition."Name" = 'Olympics Qualification'
                  AND source_season."Label" = '2024'
                  AND g."Source" = 'global-sports-archive'
                  AND (
                      lower(COALESCE(g."CompetitionPhase", '')) LIKE '%pre-qual%'
                      OR lower(COALESCE(g."CompetitionPhase", '')) LIKE '%prequal%'
                      OR lower(COALESCE(g."CompetitionRound", '')) LIKE '%pre-qual%'
                      OR lower(COALESCE(g."CompetitionRound", '')) LIKE '%prequal%'
                  );

                UPDATE games g
                SET "TournamentCycleId" = tc."Id"
                FROM seasons s
                JOIN competitions c ON c."Id" = s."CompetitionId"
                JOIN tournament_cycles tc ON tc."Key" = 'olympics-' || s."Label"
                WHERE g."SeasonId" = s."Id"
                  AND c."Name" IN ('Summer Olympics', 'Olympics Qualification', 'Olympics Pre-Qualification');
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The split is retained; restoring mixed Olympic qualification rows
        // would reintroduce an ambiguous stage boundary.
    }
}
