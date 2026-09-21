using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260920120000_ExcludeItalySerieA2FromCurrentResults")]
public partial class ExcludeItalySerieA2FromCurrentResults : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DELETE FROM current_result_reviews
            WHERE lower(btrim("CountryName")) = 'italy'
              AND regexp_replace(lower("CompetitionName"), '[^a-z0-9]+', ' ', 'g')
                    ~ '(^| )serie a *2( |$)';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Deleted transient review rows cannot be reconstructed safely.
    }
}
