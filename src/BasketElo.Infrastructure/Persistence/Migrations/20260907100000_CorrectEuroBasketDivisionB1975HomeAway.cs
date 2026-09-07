using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260907100000_CorrectEuroBasketDivisionB1975HomeAway")]
public partial class CorrectEuroBasketDivisionB1975HomeAway : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE games g
            SET "HomeTeamId" = home."Id",
                "AwayTeamId" = away."Id",
                "HomeScore" = 76,
                "AwayScore" = 68,
                "UpdatedAtUtc" = now()
            FROM teams home, teams away
            WHERE g."Source" = 'Los campeonatos de Europa 1935-1995'
              AND g."SourceGameId" = 'eurobasket-division-b-1975-f-aut-hun'
              AND home."CanonicalName" = 'Hungary'
              AND away."CanonicalName" = 'Austria';

            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM games g
                    JOIN teams home ON home."Id" = g."HomeTeamId"
                    JOIN teams away ON away."Id" = g."AwayTeamId"
                    WHERE g."SourceGameId" = 'eurobasket-division-b-1975-f-aut-hun'
                      AND home."CanonicalName" = 'Hungary'
                      AND away."CanonicalName" = 'Austria'
                      AND g."HomeScore" = 76 AND g."AwayScore" = 68) THEN
                    RAISE EXCEPTION 'EuroBasket Division B 1975 home/away correction failed';
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
