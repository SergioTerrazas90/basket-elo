using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260813124500_NormalizeInvalidHistoricalGameDates")]
public partial class NormalizeInvalidHistoricalGameDates : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            WITH invalid_games AS (
                SELECT g."Id",
                       s."StartDateUtc" + (s."EndDateUtc" - s."StartDateUtc") / 2 AS "CorrectedDate"
                FROM games g
                INNER JOIN seasons s ON s."Id" = g."SeasonId"
                WHERE g."GameDateTimeUtc" < TIMESTAMPTZ '1900-01-01'
                  AND s."StartDateUtc" >= TIMESTAMPTZ '1900-01-01'
                  AND s."EndDateUtc" >= s."StartDateUtc"
            )
            UPDATE games g
            SET "GameDateTimeUtc" = invalid_games."CorrectedDate",
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
        // Corrected dates are intentionally not reverted.
    }
}
