using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEloRatingPools : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
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
                name: "IX_elo_rebuild_runs_RulesetVersion_CompetitionName",
                table: "elo_rebuild_runs");

            migrationBuilder.AddColumn<string>(
                name: "EloPoolKey",
                table: "team_ratings",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "EloPoolKey",
                table: "rating_history",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "EloPoolKey",
                table: "elo_rebuild_runs",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EloPoolKey",
                table: "competitions",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE competitions
                SET "EloPoolKey" = CASE
                    WHEN lower("Name") = 'nba' OR "Name" ILIKE 'NBA %' THEN 'nba'
                    ELSE 'europe-clubs'
                END;

                UPDATE rating_history rh
                SET "EloPoolKey" = c."EloPoolKey"
                FROM games g
                INNER JOIN competitions c ON c."Id" = g."CompetitionId"
                WHERE g."Id" = rh."GameId";

                UPDATE team_ratings tr
                SET "EloPoolKey" = COALESCE(
                    (
                        SELECT c."EloPoolKey"
                        FROM games g
                        INNER JOIN competitions c ON c."Id" = g."CompetitionId"
                        WHERE g."Id" = tr."LastGameId"
                    ),
                    (
                        SELECT rh."EloPoolKey"
                        FROM rating_history rh
                        WHERE rh."TeamId" = tr."TeamId"
                          AND rh."RulesetVersion" = tr."RulesetVersion"
                        ORDER BY rh."GameDateTimeUtc" DESC, rh."Id" DESC
                        LIMIT 1
                    ),
                    'europe-clubs');

                UPDATE elo_rebuild_runs
                SET "EloPoolKey" = CASE
                    WHEN lower("CompetitionName") = 'nba' THEN 'nba'
                    WHEN "CompetitionName" <> '' THEN 'europe-clubs'
                    ELSE NULL
                END;

                ALTER TABLE team_ratings ALTER COLUMN "EloPoolKey" DROP DEFAULT;
                ALTER TABLE rating_history ALTER COLUMN "EloPoolKey" DROP DEFAULT;
                """);

            migrationBuilder.AddPrimaryKey(
                name: "PK_team_ratings",
                table: "team_ratings",
                columns: new[] { "EloPoolKey", "TeamId", "RulesetVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_team_ratings_TeamId",
                table: "team_ratings",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_rating_history_EloPoolKey_GameId_TeamId_RulesetVersion",
                table: "rating_history",
                columns: new[] { "EloPoolKey", "GameId", "TeamId", "RulesetVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rating_history_EloPoolKey_TeamId_RulesetVersion_GameDateTim~",
                table: "rating_history",
                columns: new[] { "EloPoolKey", "TeamId", "RulesetVersion", "GameDateTimeUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_rating_history_GameId",
                table: "rating_history",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_rating_history_TeamId",
                table: "rating_history",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_elo_rebuild_runs_EloPoolKey_RulesetVersion",
                table: "elo_rebuild_runs",
                columns: new[] { "EloPoolKey", "RulesetVersion" },
                unique: true,
                filter: "\"Status\" IN ('pending', 'running') AND \"EloPoolKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_competitions_EloPoolKey",
                table: "competitions",
                column: "EloPoolKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_team_ratings",
                table: "team_ratings");

            migrationBuilder.DropIndex(
                name: "IX_team_ratings_TeamId",
                table: "team_ratings");

            migrationBuilder.DropIndex(
                name: "IX_rating_history_EloPoolKey_GameId_TeamId_RulesetVersion",
                table: "rating_history");

            migrationBuilder.DropIndex(
                name: "IX_rating_history_EloPoolKey_TeamId_RulesetVersion_GameDateTim~",
                table: "rating_history");

            migrationBuilder.DropIndex(
                name: "IX_rating_history_GameId",
                table: "rating_history");

            migrationBuilder.DropIndex(
                name: "IX_rating_history_TeamId",
                table: "rating_history");

            migrationBuilder.DropIndex(
                name: "IX_elo_rebuild_runs_EloPoolKey_RulesetVersion",
                table: "elo_rebuild_runs");

            migrationBuilder.DropIndex(
                name: "IX_competitions_EloPoolKey",
                table: "competitions");

            migrationBuilder.DropColumn(
                name: "EloPoolKey",
                table: "team_ratings");

            migrationBuilder.DropColumn(
                name: "EloPoolKey",
                table: "rating_history");

            migrationBuilder.DropColumn(
                name: "EloPoolKey",
                table: "elo_rebuild_runs");

            migrationBuilder.DropColumn(
                name: "EloPoolKey",
                table: "competitions");

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
                name: "IX_elo_rebuild_runs_RulesetVersion_CompetitionName",
                table: "elo_rebuild_runs",
                columns: new[] { "RulesetVersion", "CompetitionName" },
                unique: true,
                filter: "\"Status\" IN ('pending', 'running')");
        }
    }
}
