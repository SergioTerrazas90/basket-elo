using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCompetitionScopedEloRebuild : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_elo_rebuild_runs_RulesetVersion",
                table: "elo_rebuild_runs");

            migrationBuilder.AddColumn<string>(
                name: "CompetitionName",
                table: "elo_rebuild_runs",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_elo_rebuild_runs_RulesetVersion_CompetitionName",
                table: "elo_rebuild_runs",
                columns: new[] { "RulesetVersion", "CompetitionName" },
                unique: true,
                filter: "\"Status\" IN ('pending', 'running')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_elo_rebuild_runs_RulesetVersion_CompetitionName",
                table: "elo_rebuild_runs");

            migrationBuilder.DropColumn(
                name: "CompetitionName",
                table: "elo_rebuild_runs");

            migrationBuilder.CreateIndex(
                name: "IX_elo_rebuild_runs_RulesetVersion",
                table: "elo_rebuild_runs",
                column: "RulesetVersion",
                unique: true,
                filter: "\"Status\" IN ('pending', 'running')");
        }
    }
}
