using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260905173000_NameEuroBasket1963BookSource")]
public partial class NameEuroBasket1963BookSource : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE games
            SET "Source" = 'Los campeonatos de Europa 1935-1955',
                "SourceUrl" = NULL,
                "SourceRevision" = 'Los campeonatos de Europa 1935-1955 — Carlos Jiménez',
                "UpdatedAtUtc" = now()
            WHERE "Source" = 'book'
              AND "SourceGameId" LIKE 'eurobasket-1963-qualifier-%';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // This is an audited provenance correction. The pre-deployment backup
        // is the rollback path, consistent with the historical data migrations.
    }
}
