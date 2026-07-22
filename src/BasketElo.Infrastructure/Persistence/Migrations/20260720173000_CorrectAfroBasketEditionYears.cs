using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using BasketElo.Infrastructure.Persistence;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260720173000_CorrectAfroBasketEditionYears")]
public partial class CorrectAfroBasketEditionYears : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            WITH afro_competition AS (
                SELECT DISTINCT "CompetitionId"
                FROM competition_aliases
                WHERE "Source" = 'global-sports-archive'
                  AND "SourceCompetitionId" = 'fiba-afrobasket'
            ),
            season_pair AS (
                SELECT legacy."Id" AS legacy_id,
                       canonical."Id" AS canonical_id
                FROM seasons legacy
                JOIN seasons canonical
                  ON canonical."CompetitionId" = legacy."CompetitionId"
                 AND canonical."Label" = '1993'
                JOIN afro_competition ac ON ac."CompetitionId" = legacy."CompetitionId"
                WHERE legacy."Label" = '1992'
            )
            UPDATE games
            SET "SeasonId" = season_pair.canonical_id
            FROM season_pair
            WHERE games."SeasonId" = season_pair.legacy_id;
            """);

        migrationBuilder.Sql("""
            DELETE FROM seasons legacy
            USING (
                SELECT legacy."Id" AS legacy_id
                FROM seasons legacy
                JOIN seasons canonical
                  ON canonical."CompetitionId" = legacy."CompetitionId"
                 AND canonical."Label" = '1993'
                JOIN competition_aliases ca
                  ON ca."CompetitionId" = legacy."CompetitionId"
                 AND ca."Source" = 'global-sports-archive'
                 AND ca."SourceCompetitionId" = 'fiba-afrobasket'
                WHERE legacy."Label" = '1992'
            ) obsolete
            WHERE legacy."Id" = obsolete.legacy_id;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The archive identifies both editions as 1993; this correction is intentionally irreversible.
    }
}
