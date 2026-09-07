using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260906090000_CorrectEuroBasket1963BookSourceTitle")]
public partial class CorrectEuroBasket1963BookSourceTitle : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE games
            SET "Source" = 'Los campeonatos de Europa 1935-1995',
                "SourceRevision" = 'Los campeonatos de Europa 1935-1995 — Carlos Jiménez',
                "UpdatedAtUtc" = now()
            WHERE "Source" = 'Los campeonatos de Europa 1935-1955'
              AND "SourceGameId" LIKE 'eurobasket-1963-qualifier-%';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // This is an audited provenance correction. The pre-deployment backup
        // is the rollback path, consistent with the historical data migrations.
    }
}
