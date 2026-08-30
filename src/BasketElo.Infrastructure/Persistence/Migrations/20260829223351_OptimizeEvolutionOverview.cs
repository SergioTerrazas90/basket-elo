using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OptimizeEvolutionOverview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_rating_history_EvolutionOverview",
                table: "rating_history",
                columns: new[] { "EloPoolKey", "RulesetVersion", "TeamId", "GameDateTimeUtc", "PostElo" })
                .Annotation("Npgsql:IndexInclude", new[] { "GameId", "EloDelta", "RatingPositionAfter" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_rating_history_EvolutionOverview",
                table: "rating_history");
        }
    }
}
