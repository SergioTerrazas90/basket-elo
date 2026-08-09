using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260808204500_BackfillWorldCupTournamentCycles")]
public partial class BackfillWorldCupTournamentCycles : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            INSERT INTO tournament_cycles
                ("Id", "Key", "Family", "EditionLabel", "DisplayName", "CreatedAtUtc")
            SELECT md5('worldcup:' || s."Label")::uuid,
                   'worldcup-' || s."Label",
                   'FIBA Basketball World Cup',
                   s."Label",
                   'FIBA Basketball World Cup ' || s."Label",
                   CURRENT_TIMESTAMP
            FROM games g
            JOIN seasons s ON s."Id" = g."SeasonId"
            JOIN competitions c ON c."Id" = g."CompetitionId"
            WHERE c."Name" IN (
                'FIBA Basketball World Cup',
                'FIBA Basketball World Cup Qualifiers',
                'FIBA WC Qualification'
            )
            GROUP BY s."Label"
            ON CONFLICT ("Key") DO NOTHING;

            UPDATE games g
            SET "TournamentCycleId" = tc."Id"
            FROM seasons s
            JOIN competitions c ON c."Id" = s."CompetitionId"
            JOIN tournament_cycles tc ON tc."Key" = 'worldcup-' || s."Label"
            WHERE g."SeasonId" = s."Id"
              AND c."Name" IN (
                  'FIBA Basketball World Cup',
                  'FIBA Basketball World Cup Qualifiers',
                  'FIBA WC Qualification'
              );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE games
            SET "TournamentCycleId" = NULL
            WHERE "TournamentCycleId" IN (
                SELECT "Id" FROM tournament_cycles WHERE "Family" = 'FIBA Basketball World Cup'
            );
            """);
    }
}
