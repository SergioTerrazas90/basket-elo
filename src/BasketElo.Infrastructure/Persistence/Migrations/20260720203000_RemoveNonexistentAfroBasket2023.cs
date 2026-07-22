using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260720203000_RemoveNonexistentAfroBasket2023")]
public partial class RemoveNonexistentAfroBasket2023 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DELETE FROM seasons s
            USING competitions c
            WHERE s."CompetitionId" = c."Id"
              AND c."Name" = 'FIBA AfroBasket'
              AND s."Label" = '2023'
              AND NOT EXISTS (SELECT 1 FROM games g WHERE g."SeasonId" = s."Id");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The FIBA archive has no 2023 AfroBasket edition.
    }
}
