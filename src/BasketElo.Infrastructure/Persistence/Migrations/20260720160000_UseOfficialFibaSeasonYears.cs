using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using BasketElo.Infrastructure.Persistence;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260720160000_UseOfficialFibaSeasonYears")]
public partial class UseOfficialFibaSeasonYears : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            WITH fiba_competitions AS (
                SELECT DISTINCT "CompetitionId"
                FROM competition_aliases
                WHERE "Source" = 'fiba'
            ),
            official_years AS (
                SELECT s."Id",
                       COALESCE(
                           MIN(NULLIF(substring(g."SourceSeasonKey" FROM '^([0-9]{4})'), '')::int)
                               FILTER (WHERE g."Source" = 'fiba'),
                           NULLIF(substring(s."Label" FROM '^([0-9]{4})'), '')::int
                       ) AS official_year
                FROM seasons s
                JOIN fiba_competitions f ON f."CompetitionId" = s."CompetitionId"
                LEFT JOIN games g ON g."SeasonId" = s."Id"
                WHERE s."Label" ~ '^[0-9]{4}-[0-9]{4}$'
                GROUP BY s."Id", s."Label"
            )
            UPDATE seasons s
            SET "Label" = official_years.official_year::text,
                "StartDateUtc" = make_timestamptz(official_years.official_year, 1, 1, 0, 0, 0),
                "EndDateUtc" = make_timestamptz(official_years.official_year, 12, 31, 23, 59, 59)
            FROM official_years
            WHERE s."Id" = official_years."Id";
            """);

        migrationBuilder.Sql("""
            UPDATE backfill_jobs
            SET "Season" = substring("Season" FROM '^([0-9]{4})')
            WHERE "Provider" = 'fiba'
              AND "Season" ~ '^[0-9]{4}-[0-9]{4}$';

            UPDATE backfill_inspection_decisions
            SET "Season" = substring("Season" FROM '^([0-9]{4})')
            WHERE "Provider" = 'fiba'
              AND "Season" ~ '^[0-9]{4}-[0-9]{4}$';

            UPDATE identity_health_check_runs
            SET "Season" = substring("Season" FROM '^([0-9]{4})')
            WHERE "Source" = 'fiba'
              AND "Season" ~ '^[0-9]{4}-[0-9]{4}$';

            UPDATE identity_health_check_findings
            SET "Season" = substring("Season" FROM '^([0-9]{4})')
            WHERE "Source" = 'fiba'
              AND "Season" ~ '^[0-9]{4}-[0-9]{4}$';
            """);

        migrationBuilder.Sql("""
            WITH fiba_competitions AS (
                SELECT DISTINCT "CompetitionId"
                FROM competition_aliases
                WHERE "Source" = 'fiba'
            )
            UPDATE model_lab_run_predictions p
            SET "Season" = s."Label"
            FROM games g
            JOIN seasons s ON s."Id" = g."SeasonId"
            JOIN fiba_competitions f ON f."CompetitionId" = s."CompetitionId"
            WHERE p."GameId" = g."Id"
              AND p."Season" ~ '^[0-9]{4}-[0-9]{4}$';

            WITH fiba_competitions AS (
                SELECT DISTINCT "CompetitionId"
                FROM competition_aliases
                WHERE "Source" = 'fiba'
            )
            UPDATE model_lab_run_metric_breakdowns m
            SET "Season" = s."Label",
                "SegmentKey" = CASE
                    WHEN m."SegmentType" = 'season' THEN s."Label"
                    ELSE m."SegmentKey"
                END,
                "Label" = CASE
                    WHEN m."SegmentType" = 'season' THEN s."Label"
                    ELSE m."Label"
                END
            FROM seasons s
            JOIN fiba_competitions f ON f."CompetitionId" = s."CompetitionId"
            WHERE m."CompetitionId" = s."CompetitionId"
              AND m."Season" ~ '^[0-9]{4}-[0-9]{4}$'
              AND substring(m."Season" FROM '^([0-9]{4})') = s."Label";
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The official edition-year labels intentionally do not have a reversible mapping.
    }
}
