using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260720174500_RemoveObsoleteAfroBasketBackfills")]
public partial class RemoveObsoleteAfroBasketBackfills : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DELETE FROM backfill_jobs
            WHERE "Provider" = 'fiba'
              AND "LeagueName" = 'FIBA AfroBasket'
              AND "Season" IN ('1976', '1982', '1984', '1992')
              AND "Status" = 'completed_with_warnings'
              AND COALESCE(("SummaryJson"->>'GamesInserted')::int, 0) = 0;
            """);

        migrationBuilder.Sql("""
            DELETE FROM seasons s
            USING competitions c
            WHERE s."CompetitionId" = c."Id"
              AND c."Name" = 'FIBA AfroBasket'
              AND s."Label" IN ('1976', '1982', '1984', '1992')
              AND NOT EXISTS (SELECT 1 FROM games g WHERE g."SeasonId" = s."Id");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Obsolete failed backfills and empty seasons are intentionally not restored.
    }
}
