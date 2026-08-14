using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260813230000_FixFmpZeleznikCountryCode")]
public partial class FixFmpZeleznikCountryCode : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE teams
            SET "CountryCode" = 'RS'
            WHERE "Id" = '30db76ef-b8cd-47a3-9269-daa5457ac58f'::uuid
              AND UPPER("CountryCode") = 'FMP'
              AND EXISTS (
                  SELECT 1
                  FROM team_aliases
                  WHERE "TeamId" = teams."Id"
                    AND "Source" = 'fiba'
                    AND "SourceTeamId" = 'FMP'
              );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE teams
            SET "CountryCode" = 'FMP'
            WHERE "Id" = '30db76ef-b8cd-47a3-9269-daa5457ac58f'::uuid
              AND UPPER("CountryCode") = 'RS'
              AND EXISTS (
                  SELECT 1
                  FROM team_aliases
                  WHERE "TeamId" = teams."Id"
                    AND "Source" = 'fiba'
                    AND "SourceTeamId" = 'FMP'
              );
            """);
    }
}
