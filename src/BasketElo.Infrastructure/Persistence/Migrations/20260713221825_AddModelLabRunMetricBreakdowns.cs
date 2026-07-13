using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddModelLabRunMetricBreakdowns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "model_lab_run_metric_breakdowns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SegmentType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SegmentKey = table.Column<string>(type: "character varying(220)", maxLength: 220, nullable: false),
                    Label = table.Column<string>(type: "character varying(220)", maxLength: 220, nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Season = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
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
                    BaselineAveragePredictedHomeWinProbability = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_model_lab_run_metric_breakdowns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_model_lab_run_metric_breakdowns_application_users_OwnerUser~",
                        column: x => x.OwnerUserId,
                        principalTable: "application_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_model_lab_run_metric_breakdowns_competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalTable: "competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_model_lab_run_metric_breakdowns_model_lab_runs_RunId",
                        column: x => x.RunId,
                        principalTable: "model_lab_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_model_lab_run_metric_breakdowns_CompetitionId",
                table: "model_lab_run_metric_breakdowns",
                column: "CompetitionId");

            migrationBuilder.CreateIndex(
                name: "IX_model_lab_run_metric_breakdowns_OwnerUserId_RunId",
                table: "model_lab_run_metric_breakdowns",
                columns: new[] { "OwnerUserId", "RunId" });

            migrationBuilder.CreateIndex(
                name: "IX_model_lab_run_metric_breakdowns_RunId_CompetitionId",
                table: "model_lab_run_metric_breakdowns",
                columns: new[] { "RunId", "CompetitionId" });

            migrationBuilder.CreateIndex(
                name: "IX_model_lab_run_metric_breakdowns_RunId_Season",
                table: "model_lab_run_metric_breakdowns",
                columns: new[] { "RunId", "Season" });

            migrationBuilder.CreateIndex(
                name: "IX_model_lab_run_metric_breakdowns_RunId_SegmentType",
                table: "model_lab_run_metric_breakdowns",
                columns: new[] { "RunId", "SegmentType" });

            migrationBuilder.CreateIndex(
                name: "IX_model_lab_run_metric_breakdowns_RunId_SegmentType_SegmentKey",
                table: "model_lab_run_metric_breakdowns",
                columns: new[] { "RunId", "SegmentType", "SegmentKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "model_lab_run_metric_breakdowns");
        }
    }
}
