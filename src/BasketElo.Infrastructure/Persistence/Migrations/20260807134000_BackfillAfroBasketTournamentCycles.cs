using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

public partial class BackfillAfroBasketTournamentCycles : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            INSERT INTO tournament_cycles ("Id", "Key", "Family", "EditionLabel", "DisplayName", "CreatedAtUtc")
            SELECT md5('afrobasket:' || s."Label")::uuid,
                   'afrobasket-' || s."Label",
                   'AfroBasket',
                   s."Label",
                   'AfroBasket ' || s."Label",
                   CURRENT_TIMESTAMP
            FROM games g
            INNER JOIN seasons s ON s."Id" = g."SeasonId"
            INNER JOIN competitions c ON c."Id" = g."CompetitionId"
            WHERE lower(c."Name") IN (
                'afrobasket',
                'fiba afrobasket',
                'afrobasket qualifiers',
                'fiba afrobasket qualifiers',
                'afrobasket pre-qualifiers',
                'fiba afrobasket pre-qualifiers'
            )
            GROUP BY s."Label"
            ON CONFLICT ("Key") DO NOTHING;

            UPDATE games g
            SET "TournamentCycleId" = tc."Id"
            FROM seasons s, competitions c, tournament_cycles tc
            WHERE s."Id" = g."SeasonId"
              AND c."Id" = g."CompetitionId"
              AND tc."Key" = 'afrobasket-' || s."Label"
              AND lower(c."Name") IN (
                  'afrobasket',
                  'fiba afrobasket',
                  'afrobasket qualifiers',
                  'fiba afrobasket qualifiers',
                  'afrobasket pre-qualifiers',
                  'fiba afrobasket pre-qualifiers'
              );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE games
            SET "TournamentCycleId" = NULL
            WHERE "TournamentCycleId" IN (
                SELECT "Id" FROM tournament_cycles WHERE "Family" = 'AfroBasket'
            );
            DELETE FROM tournament_cycles WHERE "Family" = 'AfroBasket';
            """);
    }
}
