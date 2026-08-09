using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260808112000_SplitGsaAmeriCupQualificationStages")]
public partial class SplitGsaAmeriCupQualificationStages : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            DECLARE
                pre_qualifier_competition_id uuid := md5('competition:fiba-americup-pre-qualifiers')::uuid;
            BEGIN
                INSERT INTO competitions
                    ("Id", "Name", "Type", "CountryCode", "Tier", "IsActive", "CreatedAtUtc", "EloPoolKey")
                VALUES
                    (pre_qualifier_competition_id, 'FIBA AmeriCup Pre-Qualifiers', 'qualifier', NULL, 1, TRUE, CURRENT_TIMESTAMP, 'national-teams')
                ON CONFLICT ("Id") DO NOTHING;

                INSERT INTO seasons
                    ("Id", "CompetitionId", "Label", "StartDateUtc", "EndDateUtc", "CreatedAtUtc")
                SELECT md5('season:fiba-americup-pre-qualifiers:' || s."Label")::uuid,
                       pre_qualifier_competition_id,
                       s."Label",
                       s."StartDateUtc",
                       s."EndDateUtc",
                       CURRENT_TIMESTAMP
                FROM seasons s
                JOIN competitions c ON c."Id" = s."CompetitionId"
                WHERE c."Name" = 'FIBA AmeriCup Qualification'
                  AND s."Label" IN ('2022', '2025')
                  AND NOT EXISTS (
                      SELECT 1
                      FROM seasons target
                      WHERE target."CompetitionId" = pre_qualifier_competition_id
                        AND target."Label" = s."Label"
                  );

                INSERT INTO tournament_cycles
                    ("Id", "Key", "Family", "EditionLabel", "DisplayName", "CreatedAtUtc")
                SELECT md5('tournament-cycle:americup:' || s."Label")::uuid,
                       'americup-' || s."Label",
                       'FIBA AmeriCup',
                       s."Label",
                       'FIBA AmeriCup ' || s."Label",
                       CURRENT_TIMESTAMP
                FROM seasons s
                WHERE s."CompetitionId" = pre_qualifier_competition_id
                ON CONFLICT ("Key") DO NOTHING;

                INSERT INTO competition_aliases
                    ("Id", "CompetitionId", "Source", "SourceCompetitionId", "AliasName", "CreatedAtUtc")
                VALUES
                    (md5('competition-alias:global-sports-archive:fiba-americup-pre-qualifiers')::uuid,
                     pre_qualifier_competition_id,
                     'global-sports-archive',
                     'fiba-americup-pre-qualifiers',
                     'FIBA AmeriCup Pre-Qualifiers',
                     CURRENT_TIMESTAMP)
                ON CONFLICT ("Id") DO NOTHING;

                UPDATE games g
                SET "CompetitionId" = pre_qualifier_competition_id,
                    "SeasonId" = target_season."Id",
                    "TournamentCycleId" = md5('tournament-cycle:americup:' || target_season."Label")::uuid
                FROM seasons source_season
                JOIN competitions source_competition ON source_competition."Id" = source_season."CompetitionId"
                JOIN seasons target_season ON target_season."CompetitionId" = pre_qualifier_competition_id
                                           AND target_season."Label" = source_season."Label"
                WHERE g."SeasonId" = source_season."Id"
                  AND source_competition."Name" = 'FIBA AmeriCup Qualification'
                  AND g."Source" = 'global-sports-archive'
                  AND lower(COALESCE(g."CompetitionRound", '')) IN (
                      'pre-qualifiers',
                      'caribbean',
                      'central-america',
                      'south-america'
                  );
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The split is retained; restoring the mixed GSA qualification rows would
        // reintroduce an ambiguous competition boundary.
    }
}
