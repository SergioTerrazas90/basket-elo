using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeRemainingInternationalTeamIdentities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                WITH national_team_ids AS (
                    SELECT DISTINCT g."HomeTeamId" AS "TeamId"
                    FROM games g
                    INNER JOIN competitions c ON c."Id" = g."CompetitionId"
                    WHERE c."EloPoolKey" = 'national-teams'
                    UNION
                    SELECT DISTINCT g."AwayTeamId" AS "TeamId"
                    FROM games g
                    INNER JOIN competitions c ON c."Id" = g."CompetitionId"
                    WHERE c."EloPoolKey" = 'national-teams'
                ), identities("Code", "Name") AS (
                    VALUES
                        ('AZE', 'Azerbaijan'), ('BOL', 'Bolivia'), ('FIJ', 'Fiji'), ('NOR', 'Norway'), ('WAL', 'Wales')
                )
                UPDATE teams t
                SET "CanonicalName" = identities."Name",
                    "CountryCode" = identities."Code"
                FROM national_team_ids n
                CROSS JOIN identities
                WHERE t."Id" = n."TeamId"
                  AND (
                    UPPER(t."CanonicalName") = identities."Code"
                    OR UPPER(t."CanonicalName") = UPPER(identities."Name")
                    OR EXISTS (
                        SELECT 1 FROM team_aliases a
                        WHERE a."TeamId" = t."Id"
                          AND (UPPER(a."SourceTeamId") = identities."Code" OR UPPER(a."AliasName") = UPPER(identities."Name"))
                    )
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The canonical names are intentionally not reverted.
        }
    }
}
