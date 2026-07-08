using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRulesetVersionedEloRebuild : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_team_ratings",
                table: "team_ratings");

            migrationBuilder.DropIndex(
                name: "IX_rating_history_GameId_TeamId",
                table: "rating_history");

            migrationBuilder.DropIndex(
                name: "IX_rating_history_TeamId_GameDateTimeUtc",
                table: "rating_history");

            migrationBuilder.DropIndex(
                name: "IX_ranking_snapshots_SnapshotDate_Position",
                table: "ranking_snapshots");

            migrationBuilder.DropIndex(
                name: "IX_ranking_snapshots_SnapshotDate_TeamId",
                table: "ranking_snapshots");

            migrationBuilder.AddColumn<string>(
                name: "RulesetVersion",
                table: "team_ratings",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "basic-elo-v1");

            migrationBuilder.AddColumn<decimal>(
                name: "CompetitionWeight",
                table: "rating_history",
                type: "numeric(6,4)",
                precision: 6,
                scale: 4,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<decimal>(
                name: "MarginMultiplier",
                table: "rating_history",
                type: "numeric(6,4)",
                precision: 6,
                scale: 4,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<string>(
                name: "RulesetVersion",
                table: "rating_history",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "basic-elo-v1");

            migrationBuilder.AddColumn<string>(
                name: "RulesetVersion",
                table: "ranking_snapshots",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "basic-elo-v1");

            migrationBuilder.AddColumn<int>(
                name: "GamesProcessed",
                table: "elo_rebuild_runs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TeamsRated",
                table: "elo_rebuild_runs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddPrimaryKey(
                name: "PK_team_ratings",
                table: "team_ratings",
                columns: new[] { "TeamId", "RulesetVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_rating_history_GameId_TeamId_RulesetVersion",
                table: "rating_history",
                columns: new[] { "GameId", "TeamId", "RulesetVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rating_history_TeamId_RulesetVersion_GameDateTimeUtc",
                table: "rating_history",
                columns: new[] { "TeamId", "RulesetVersion", "GameDateTimeUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ranking_snapshots_SnapshotDate_RulesetVersion_Position",
                table: "ranking_snapshots",
                columns: new[] { "SnapshotDate", "RulesetVersion", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_ranking_snapshots_SnapshotDate_TeamId_RulesetVersion",
                table: "ranking_snapshots",
                columns: new[] { "SnapshotDate", "TeamId", "RulesetVersion" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_team_ratings",
                table: "team_ratings");

            migrationBuilder.DropIndex(
                name: "IX_rating_history_GameId_TeamId_RulesetVersion",
                table: "rating_history");

            migrationBuilder.DropIndex(
                name: "IX_rating_history_TeamId_RulesetVersion_GameDateTimeUtc",
                table: "rating_history");

            migrationBuilder.DropIndex(
                name: "IX_ranking_snapshots_SnapshotDate_RulesetVersion_Position",
                table: "ranking_snapshots");

            migrationBuilder.DropIndex(
                name: "IX_ranking_snapshots_SnapshotDate_TeamId_RulesetVersion",
                table: "ranking_snapshots");

            migrationBuilder.DropColumn(
                name: "RulesetVersion",
                table: "team_ratings");

            migrationBuilder.DropColumn(
                name: "CompetitionWeight",
                table: "rating_history");

            migrationBuilder.DropColumn(
                name: "MarginMultiplier",
                table: "rating_history");

            migrationBuilder.DropColumn(
                name: "RulesetVersion",
                table: "rating_history");

            migrationBuilder.DropColumn(
                name: "RulesetVersion",
                table: "ranking_snapshots");

            migrationBuilder.DropColumn(
                name: "GamesProcessed",
                table: "elo_rebuild_runs");

            migrationBuilder.DropColumn(
                name: "TeamsRated",
                table: "elo_rebuild_runs");

            migrationBuilder.AddPrimaryKey(
                name: "PK_team_ratings",
                table: "team_ratings",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_rating_history_GameId_TeamId",
                table: "rating_history",
                columns: new[] { "GameId", "TeamId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rating_history_TeamId_GameDateTimeUtc",
                table: "rating_history",
                columns: new[] { "TeamId", "GameDateTimeUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ranking_snapshots_SnapshotDate_Position",
                table: "ranking_snapshots",
                columns: new[] { "SnapshotDate", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_ranking_snapshots_SnapshotDate_TeamId",
                table: "ranking_snapshots",
                columns: new[] { "SnapshotDate", "TeamId" },
                unique: true);
        }
    }
}
