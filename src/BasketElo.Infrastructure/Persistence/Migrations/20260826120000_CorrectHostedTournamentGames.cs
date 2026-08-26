using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(BasketEloDbContext))]
[Migration("20260826120000_CorrectHostedTournamentGames")]
public partial class CorrectHostedTournamentGames : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // These competitions are always played as a centralized cup,
        // supercup, or final tournament in the imported catalogue.
        migrationBuilder.Sql("""
            UPDATE competitions
            SET "HomeAdvantagePolicy" = 'neutral'
            WHERE "Name" IN (
                'ABA Supercup',
                'Italian Cup',
                'Korac cup',
                'Lega A - Super Cup',
                'LNB Super Cup',
                'Polish Cup',
                'Semaine Des As',
                'Spanish Cup',
                'Super Cup',
                'Supercopa ACB',
                'Supercup',
                'VTB Super Cup');
            """);

        migrationBuilder.Sql("""
            UPDATE games AS g
            SET "IsNeutralSite" = TRUE,
                "UpdatedAtUtc" = CURRENT_TIMESTAMP
            FROM competitions AS c
            WHERE c."Id" = g."CompetitionId"
              AND c."Name" IN (
                  'ABA Supercup',
                  'Italian Cup',
                  'Korac cup',
                  'Lega A - Super Cup',
                  'LNB Super Cup',
                  'Polish Cup',
                  'Semaine Des As',
                  'Spanish Cup',
                  'Super Cup',
                  'Supercopa ACB',
                  'Supercup',
                  'VTB Super Cup')
              AND g."HomeScore" IS NOT NULL
              AND g."AwayScore" IS NOT NULL
              AND g."IsNeutralSite" IS NULL;
            """);

        // These Belgian finals were played at a separately appointed venue;
        // the 2025 final in Oostende is deliberately not included because
        // Oostende was the listed home venue for that game.
        migrationBuilder.Sql("""
            UPDATE games AS g
            SET "IsNeutralSite" = TRUE,
                "UpdatedAtUtc" = CURRENT_TIMESTAMP
            WHERE g."Source" = 'api-sports'
              AND g."SourceGameId" IN ('387677', '492543')
              AND g."IsNeutralSite" IS NULL;
            """);

        // API-Sports does not reliably expose the hosted stage/location for
        // these domestic competitions. Their final-day windows are stable in
        // the imported catalogue, so only the documented closing event is
        // corrected; earlier distributed rounds remain home/away.
        migrationBuilder.Sql("""
            WITH season_max AS (
                SELECT g."CompetitionId", g."SeasonId", max(g."GameDateTimeUtc"::date) AS max_date
                FROM games AS g
                JOIN competitions AS c ON c."Id" = g."CompetitionId"
                WHERE c."Name" IN (
                    'Czech Cup',
                    'Croatian Cup',
                    'French Cup',
                    'German Cup',
                    'Greek Cup',
                    'King Mindaugas Cup',
                    'Russian Cup',
                    'Slovenian Cup',
                    'Turkish Cup')
                  AND g."HomeScore" IS NOT NULL
                  AND g."AwayScore" IS NOT NULL
                GROUP BY g."CompetitionId", g."SeasonId")
            UPDATE games AS g
            SET "IsNeutralSite" = TRUE,
                "UpdatedAtUtc" = CURRENT_TIMESTAMP
            FROM season_max AS m
            JOIN competitions AS c ON c."Id" = m."CompetitionId"
            WHERE g."CompetitionId" = m."CompetitionId"
              AND g."SeasonId" = m."SeasonId"
              AND g."HomeScore" IS NOT NULL
              AND g."AwayScore" IS NOT NULL
              AND g."IsNeutralSite" IS NULL
              AND g."GameDateTimeUtc"::date >= m.max_date - CASE c."Name"
                  WHEN 'Croatian Cup' THEN 4
                  WHEN 'Greek Cup' THEN 4
                  WHEN 'Russian Cup' THEN 5
                  WHEN 'German Cup' THEN 1
                  WHEN 'King Mindaugas Cup' THEN 1
                  WHEN 'Slovenian Cup' THEN 1
                  WHEN 'Turkish Cup' THEN 2
                  ELSE 0
              END;
            """);

        // The Latvian final was hosted in Daugavpils in 2024-25 and in
        // Ventspils in 2025-26. The latter had Ventspils listed as home, so
        // only the confirmed neutral Daugavpils final is overridden here.
        migrationBuilder.Sql("""
            UPDATE games AS g
            SET "IsNeutralSite" = TRUE,
                "UpdatedAtUtc" = CURRENT_TIMESTAMP
            FROM competitions AS c
            WHERE c."Id" = g."CompetitionId"
              AND c."Name" = 'Latvian Cup'
              AND g."Source" = 'api-sports'
              AND g."SourceGameId" IN ('392552', '442041')
              AND g."IsNeutralSite" IS NULL;
            """);

        // The European club final events are the last hosted tournament
        // window in each completed season. Two-leg EuroCup/FIBA Europe Cup
        // finals are intentionally excluded.
        migrationBuilder.Sql("""
            WITH season_max AS (
                SELECT g."CompetitionId", g."SeasonId", max(g."GameDateTimeUtc"::date) AS max_date
                FROM games AS g
                JOIN competitions AS c ON c."Id" = g."CompetitionId"
                WHERE c."Name" IN ('Champions League', 'ENBL', 'Euroleague')
                  AND g."HomeScore" IS NOT NULL
                  AND g."AwayScore" IS NOT NULL
                GROUP BY g."CompetitionId", g."SeasonId")
            UPDATE games AS g
            SET "IsNeutralSite" = TRUE,
                "UpdatedAtUtc" = CURRENT_TIMESTAMP
            FROM season_max AS m
            JOIN competitions AS c ON c."Id" = m."CompetitionId"
            WHERE g."CompetitionId" = m."CompetitionId"
              AND g."SeasonId" = m."SeasonId"
              AND g."HomeScore" IS NOT NULL
              AND g."AwayScore" IS NOT NULL
              AND g."IsNeutralSite" IS NULL
              AND (
                  (c."Name" = 'Euroleague'
                   AND EXTRACT(MONTH FROM m.max_date) IN (4, 5)
                   AND g."GameDateTimeUtc"::date >= m.max_date - 4)
                  OR
                  (c."Name" = 'Champions League'
                   AND EXTRACT(MONTH FROM m.max_date) IN (4, 5, 10)
                   AND g."GameDateTimeUtc"::date >= m.max_date - 4)
                  OR
                  (c."Name" = 'ENBL'
                   AND EXTRACT(MONTH FROM m.max_date) = 4
                   AND g."GameDateTimeUtc"::date >= m.max_date - 4));
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE competitions
            SET "HomeAdvantagePolicy" = 'automatic'
            WHERE "Name" IN (
                'ABA Supercup',
                'Italian Cup',
                'Korac cup',
                'Lega A - Super Cup',
                'LNB Super Cup',
                'Polish Cup',
                'Semaine Des As',
                'Spanish Cup',
                'Super Cup',
                'Supercopa ACB',
                'Supercup',
                'VTB Super Cup');
            """);

        migrationBuilder.Sql("""
            UPDATE games AS g
            SET "IsNeutralSite" = NULL,
                "UpdatedAtUtc" = CURRENT_TIMESTAMP
            FROM competitions AS c
            WHERE c."Id" = g."CompetitionId"
              AND c."Name" IN (
                  'ABA Supercup',
                  'Italian Cup',
                  'Korac cup',
                  'Lega A - Super Cup',
                  'LNB Super Cup',
                  'Polish Cup',
                  'Semaine Des As',
                  'Spanish Cup',
                  'Super Cup',
                  'Supercopa ACB',
                  'Supercup',
                  'VTB Super Cup')
              AND g."IsNeutralSite" IS TRUE;
            """);

        migrationBuilder.Sql("""
            WITH season_max AS (
                SELECT g."CompetitionId", g."SeasonId", max(g."GameDateTimeUtc"::date) AS max_date
                FROM games AS g
                JOIN competitions AS c ON c."Id" = g."CompetitionId"
                WHERE c."Name" IN (
                    'Czech Cup',
                    'Croatian Cup',
                    'French Cup',
                    'German Cup',
                    'Greek Cup',
                    'King Mindaugas Cup',
                    'Russian Cup',
                    'Slovenian Cup',
                    'Turkish Cup')
                GROUP BY g."CompetitionId", g."SeasonId")
            UPDATE games AS g
            SET "IsNeutralSite" = NULL,
                "UpdatedAtUtc" = CURRENT_TIMESTAMP
            FROM season_max AS m
            JOIN competitions AS c ON c."Id" = m."CompetitionId"
            WHERE g."CompetitionId" = m."CompetitionId"
              AND g."SeasonId" = m."SeasonId"
              AND g."IsNeutralSite" IS TRUE
              AND g."GameDateTimeUtc"::date >= m.max_date - CASE c."Name"
                  WHEN 'Croatian Cup' THEN 4
                  WHEN 'Greek Cup' THEN 4
                  WHEN 'Russian Cup' THEN 5
                  WHEN 'German Cup' THEN 1
                  WHEN 'King Mindaugas Cup' THEN 1
                  WHEN 'Slovenian Cup' THEN 1
                  WHEN 'Turkish Cup' THEN 2
                  ELSE 0
              END;
            """);

        migrationBuilder.Sql("""
            UPDATE games AS g
            SET "IsNeutralSite" = NULL,
                "UpdatedAtUtc" = CURRENT_TIMESTAMP
            WHERE g."Source" = 'api-sports'
              AND g."SourceGameId" IN ('392552', '442041')
              AND g."IsNeutralSite" IS TRUE;
            """);

        migrationBuilder.Sql("""
            UPDATE games AS g
            SET "IsNeutralSite" = NULL,
                "UpdatedAtUtc" = CURRENT_TIMESTAMP
            WHERE g."Source" = 'api-sports'
              AND g."SourceGameId" IN ('387677', '492543')
              AND g."IsNeutralSite" IS TRUE;
            """);

        migrationBuilder.Sql("""
            WITH season_max AS (
                SELECT g."CompetitionId", g."SeasonId", max(g."GameDateTimeUtc"::date) AS max_date
                FROM games AS g
                JOIN competitions AS c ON c."Id" = g."CompetitionId"
                WHERE c."Name" IN ('Champions League', 'ENBL', 'Euroleague')
                GROUP BY g."CompetitionId", g."SeasonId")
            UPDATE games AS g
            SET "IsNeutralSite" = NULL,
                "UpdatedAtUtc" = CURRENT_TIMESTAMP
            FROM season_max AS m
            JOIN competitions AS c ON c."Id" = m."CompetitionId"
            WHERE g."CompetitionId" = m."CompetitionId"
              AND g."SeasonId" = m."SeasonId"
              AND g."IsNeutralSite" IS TRUE
              AND (
                  (c."Name" = 'Euroleague'
                   AND EXTRACT(MONTH FROM m.max_date) IN (4, 5)
                   AND g."GameDateTimeUtc"::date >= m.max_date - 4)
                  OR
                  (c."Name" = 'Champions League'
                   AND EXTRACT(MONTH FROM m.max_date) IN (4, 5, 10)
                   AND g."GameDateTimeUtc"::date >= m.max_date - 4)
                  OR
                  (c."Name" = 'ENBL'
                   AND EXTRACT(MONTH FROM m.max_date) = 4
                   AND g."GameDateTimeUtc"::date >= m.max_date - 4));
            """);
    }
}
