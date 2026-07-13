using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddModelLabRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "model_lab_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    LeagueName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ScopeType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    InitializationFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    InitializationToUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    InitializationGames = table.Column<int>(type: "integer", nullable: false),
                    ScoredFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ScoredToUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ScoredGames = table.Column<int>(type: "integer", nullable: false),
                    CorrectWinners = table.Column<int>(type: "integer", nullable: false),
                    WinnerAccuracy = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    BrierScore = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    LogLoss = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    AverageMarginError = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    AveragePredictedHomeWinProbability = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    BaselineScoredGames = table.Column<int>(type: "integer", nullable: false),
                    BaselineCorrectWinners = table.Column<int>(type: "integer", nullable: false),
                    BaselineWinnerAccuracy = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    BaselineBrierScore = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    BaselineLogLoss = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    BaselineAverageMarginError = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    BaselineAveragePredictedHomeWinProbability = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_model_lab_runs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_model_lab_runs_application_users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "application_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_model_lab_runs_model_lab_model_versions_ModelVersionId",
                        column: x => x.ModelVersionId,
                        principalTable: "model_lab_model_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_model_lab_runs_model_lab_models_ModelId",
                        column: x => x.ModelId,
                        principalTable: "model_lab_models",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "model_lab_run_period_metrics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodKey = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Games = table.Column<int>(type: "integer", nullable: false),
                    WinnerAccuracy = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    AverageMarginError = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_model_lab_run_period_metrics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_model_lab_run_period_metrics_application_users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "application_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_model_lab_run_period_metrics_model_lab_runs_RunId",
                        column: x => x.RunId,
                        principalTable: "model_lab_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "model_lab_run_predictions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    GameId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetitionName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    GameDateTimeUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Season = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    HomeTeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    AwayTeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    HomeTeamName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AwayTeamName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    HomeScore = table.Column<short>(type: "smallint", nullable: false),
                    AwayScore = table.Column<short>(type: "smallint", nullable: false),
                    PredictedHomeWinProbability = table.Column<decimal>(type: "numeric(6,4)", precision: 6, scale: 4, nullable: false),
                    PredictedHomeMargin = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    ActualHomeMargin = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    MarginError = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    PickedWinner = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_model_lab_run_predictions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_model_lab_run_predictions_application_users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "application_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_model_lab_run_predictions_competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalTable: "competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_model_lab_run_predictions_games_GameId",
                        column: x => x.GameId,
                        principalTable: "games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_model_lab_run_predictions_model_lab_runs_RunId",
                        column: x => x.RunId,
                        principalTable: "model_lab_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_model_lab_run_predictions_teams_AwayTeamId",
                        column: x => x.AwayTeamId,
                        principalTable: "teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_model_lab_run_predictions_teams_HomeTeamId",
                        column: x => x.HomeTeamId,
                        principalTable: "teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "model_lab_run_ratings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rank = table.Column<int>(type: "integer", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Elo = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    GamesPlayed = table.Column<int>(type: "integer", nullable: false),
                    RecentMovement = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_model_lab_run_ratings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_model_lab_run_ratings_application_users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "application_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_model_lab_run_ratings_model_lab_runs_RunId",
                        column: x => x.RunId,
                        principalTable: "model_lab_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_model_lab_run_ratings_teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "model_lab_run_scopes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetitionName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CountryCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_model_lab_run_scopes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_model_lab_run_scopes_competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalTable: "competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_model_lab_run_scopes_model_lab_runs_RunId",
                        column: x => x.RunId,
                        principalTable: "model_lab_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_model_lab_run_period_metrics_OwnerUserId_RunId",
                table: "model_lab_run_period_metrics",
                columns: new[] { "OwnerUserId", "RunId" });

            migrationBuilder.CreateIndex(
                name: "IX_model_lab_run_period_metrics_RunId_PeriodKey",
                table: "model_lab_run_period_metrics",
                columns: new[] { "RunId", "PeriodKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_model_lab_run_predictions_AwayTeamId",
                table: "model_lab_run_predictions",
                column: "AwayTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_model_lab_run_predictions_CompetitionId",
                table: "model_lab_run_predictions",
                column: "CompetitionId");

            migrationBuilder.CreateIndex(
                name: "IX_model_lab_run_predictions_GameId",
                table: "model_lab_run_predictions",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_model_lab_run_predictions_HomeTeamId",
                table: "model_lab_run_predictions",
                column: "HomeTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_model_lab_run_predictions_OwnerUserId_RunId",
                table: "model_lab_run_predictions",
                columns: new[] { "OwnerUserId", "RunId" });

            migrationBuilder.CreateIndex(
                name: "IX_model_lab_run_predictions_RunId_CompetitionId_GameDateTimeU~",
                table: "model_lab_run_predictions",
                columns: new[] { "RunId", "CompetitionId", "GameDateTimeUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_model_lab_run_predictions_RunId_GameDateTimeUtc",
                table: "model_lab_run_predictions",
                columns: new[] { "RunId", "GameDateTimeUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_model_lab_run_predictions_RunId_GameId",
                table: "model_lab_run_predictions",
                columns: new[] { "RunId", "GameId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_model_lab_run_predictions_RunId_MarginError",
                table: "model_lab_run_predictions",
                columns: new[] { "RunId", "MarginError" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_model_lab_run_ratings_OwnerUserId_RunId",
                table: "model_lab_run_ratings",
                columns: new[] { "OwnerUserId", "RunId" });

            migrationBuilder.CreateIndex(
                name: "IX_model_lab_run_ratings_RunId_Elo",
                table: "model_lab_run_ratings",
                columns: new[] { "RunId", "Elo" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_model_lab_run_ratings_RunId_Rank",
                table: "model_lab_run_ratings",
                columns: new[] { "RunId", "Rank" });

            migrationBuilder.CreateIndex(
                name: "IX_model_lab_run_ratings_RunId_TeamId",
                table: "model_lab_run_ratings",
                columns: new[] { "RunId", "TeamId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_model_lab_run_ratings_TeamId",
                table: "model_lab_run_ratings",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_model_lab_run_scopes_CompetitionId",
                table: "model_lab_run_scopes",
                column: "CompetitionId");

            migrationBuilder.CreateIndex(
                name: "IX_model_lab_run_scopes_RunId_CompetitionId",
                table: "model_lab_run_scopes",
                columns: new[] { "RunId", "CompetitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_model_lab_runs_ModelId_CreatedAtUtc",
                table: "model_lab_runs",
                columns: new[] { "ModelId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_model_lab_runs_ModelVersionId",
                table: "model_lab_runs",
                column: "ModelVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_model_lab_runs_OwnerUserId_CreatedAtUtc",
                table: "model_lab_runs",
                columns: new[] { "OwnerUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_model_lab_runs_OwnerUserId_Status_CreatedAtUtc",
                table: "model_lab_runs",
                columns: new[] { "OwnerUserId", "Status", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "model_lab_run_period_metrics");

            migrationBuilder.DropTable(
                name: "model_lab_run_predictions");

            migrationBuilder.DropTable(
                name: "model_lab_run_ratings");

            migrationBuilder.DropTable(
                name: "model_lab_run_scopes");

            migrationBuilder.DropTable(
                name: "model_lab_runs");
        }
    }
}
