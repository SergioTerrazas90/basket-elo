using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamAliasMappingConfidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MappingConfidence",
                table: "team_aliases",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MappingMethod",
                table: "team_aliases",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE team_aliases
                SET "MappingMethod" = 'existing'
                WHERE "Source" = 'livescore'
                  AND "CreatedAtUtc" >= CURRENT_TIMESTAMP - INTERVAL '10 days'
                  AND "MappingMethod" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MappingConfidence",
                table: "team_aliases");

            migrationBuilder.DropColumn(
                name: "MappingMethod",
                table: "team_aliases");
        }
    }
}
