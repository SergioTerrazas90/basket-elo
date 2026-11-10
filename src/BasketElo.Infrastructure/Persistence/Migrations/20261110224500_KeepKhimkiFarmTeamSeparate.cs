using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20261110224500_KeepKhimkiFarmTeamSeparate")]
public partial class KeepKhimkiFarmTeamSeparate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            INSERT INTO identity_review_decisions (
                "Id", "DecisionKey", "FindingType", "ResolutionAction",
                "AffectedTeamId", "RelatedTeamId", "Note", "CreatedBy", "CreatedAtUtc")
            SELECT
                '711be337-06ec-44df-aa7f-111111111208'::uuid,
                'distinct_teams|teams=b4fba78cfd5b45658a18dfadc70502d7:d19ddad5e6fc47bebf1a98b23f7574f8',
                'possible_cross_season_split', 'keep_separate',
                'b4fba78c-fd5b-4565-8a18-dfadc70502d7'::uuid,
                'd19ddad5-e6fc-47be-bf1a-98b23f7574f8'::uuid,
                'Khimki-Podmoskovie is BK Khimki''s professional farm team and competes as a separate team.',
                'curated-migration', NOW()
            WHERE EXISTS (SELECT 1 FROM teams WHERE "Id" = 'b4fba78c-fd5b-4565-8a18-dfadc70502d7')
              AND EXISTS (SELECT 1 FROM teams WHERE "Id" = 'd19ddad5-e6fc-47be-bf1a-98b23f7574f8')
            ON CONFLICT ("DecisionKey") DO UPDATE
            SET "ResolutionAction" = EXCLUDED."ResolutionAction",
                "AffectedTeamId" = EXCLUDED."AffectedTeamId",
                "RelatedTeamId" = EXCLUDED."RelatedTeamId",
                "Note" = EXCLUDED."Note",
                "CreatedBy" = EXCLUDED."CreatedBy";

            UPDATE identity_health_check_findings
            SET "Status" = 'resolved',
                "ResolutionAction" = 'keep_separate',
                "ResolvedBy" = 'curated-migration',
                "ResolvedAtUtc" = NOW(),
                "ResolutionNote" = 'Khimki-Podmoskovie is BK Khimki''s professional farm team and competes as a separate team.'
            WHERE "Status" = 'open'
              AND "AffectedTeamId" IN ('b4fba78c-fd5b-4565-8a18-dfadc70502d7', 'd19ddad5-e6fc-47be-bf1a-98b23f7574f8')
              AND "RelatedTeamId" IN ('b4fba78c-fd5b-4565-8a18-dfadc70502d7', 'd19ddad5-e6fc-47be-bf1a-98b23f7574f8');

            UPDATE identity_health_check_runs run
            SET "UnresolvedBlockersCount" = summary.open_blockers,
                "Status" = CASE
                    WHEN summary.open_blockers > 0 THEN 'blockers'
                    WHEN summary.open_findings > 0 THEN 'warnings'
                    ELSE 'clean'
                END
            FROM (
                SELECT r."Id",
                       COUNT(f.*) FILTER (WHERE f."Status" = 'open')::integer AS open_findings,
                       COUNT(f.*) FILTER (WHERE f."Status" = 'open' AND f."Severity" = 'blocker')::integer AS open_blockers
                FROM identity_health_check_runs r
                LEFT JOIN identity_health_check_findings f ON f."RunId" = r."Id"
                WHERE r."InvalidatedAtUtc" IS NULL AND r."ScopeKey" LIKE '%pool=europe-clubs%'
                GROUP BY r."Id"
            ) summary
            WHERE run."Id" = summary."Id";
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The reviewed distinct-team decision is intentionally retained.
    }
}
