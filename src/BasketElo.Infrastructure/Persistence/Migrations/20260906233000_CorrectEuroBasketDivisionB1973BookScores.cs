using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260906233000_CorrectEuroBasketDivisionB1973BookScores")]
public partial class CorrectEuroBasketDivisionB1973BookScores : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE games
            SET "HomeScore" = 73, "AwayScore" = 67, "UpdatedAtUtc" = now()
            WHERE "Source" = 'Los campeonatos de Europa 1935-1995'
              AND "SourceGameId" = 'eurobasket-division-b-1973-grc-bel';

            UPDATE games
            SET "HomeScore" = 99, "AwayScore" = 83, "UpdatedAtUtc" = now()
            WHERE "Source" = 'Los campeonatos de Europa 1935-1995'
              AND "SourceGameId" = 'eurobasket-division-b-1973-hun-eng';

            DO $$
            BEGIN
                IF (SELECT "HomeScore" FROM games
                    WHERE "SourceGameId" = 'eurobasket-division-b-1973-grc-bel') <> 73
                   OR (SELECT "AwayScore" FROM games
                    WHERE "SourceGameId" = 'eurobasket-division-b-1973-grc-bel') <> 67
                   OR (SELECT "HomeScore" FROM games
                    WHERE "SourceGameId" = 'eurobasket-division-b-1973-hun-eng') <> 99
                   OR (SELECT "AwayScore" FROM games
                    WHERE "SourceGameId" = 'eurobasket-division-b-1973-hun-eng') <> 83 THEN
                    RAISE EXCEPTION 'EuroBasket Division B 1973 score correction failed';
                END IF;
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Restore from the pre-deployment backup if this audited correction is
        // ever rolled back.
    }
}
