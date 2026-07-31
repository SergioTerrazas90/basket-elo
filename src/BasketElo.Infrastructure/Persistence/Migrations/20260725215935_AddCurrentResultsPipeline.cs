using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCurrentResultsPipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "current_results_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FromDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ToDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    PagesRead = table.Column<int>(type: "integer", nullable: false),
                    CandidatesRead = table.Column<int>(type: "integer", nullable: false),
                    GamesUpserted = table.Column<int>(type: "integer", nullable: false),
                    ReviewsOpened = table.Column<int>(type: "integer", nullable: false),
                    EloPoolsQueued = table.Column<int>(type: "integer", nullable: false),
                    DeferredEloPoolsJson = table.Column<string>(type: "jsonb", nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_current_results_runs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "current_result_reviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: true),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SourceGameId = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    SourceUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SourceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    GameDateTimeUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CountryName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CompetitionName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StageName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    HomeTeamName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AwayTeamName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    HomeTeamSourceId = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    AwayTeamSourceId = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    HomeScore = table.Column<short>(type: "smallint", nullable: true),
                    AwayScore = table.Column<short>(type: "smallint", nullable: true),
                    ResultStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SuggestedCompetitionName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SuggestedCompetitionCountryCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    ParserVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SourceRevision = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_current_result_reviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_current_result_reviews_current_results_runs_RunId",
                        column: x => x.RunId,
                        principalTable: "current_results_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_current_result_reviews_RunId",
                table: "current_result_reviews",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_current_result_reviews_Source_SourceGameId",
                table: "current_result_reviews",
                columns: new[] { "Source", "SourceGameId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_current_result_reviews_SourceDate",
                table: "current_result_reviews",
                column: "SourceDate");

            migrationBuilder.CreateIndex(
                name: "IX_current_result_reviews_Status_UpdatedAtUtc",
                table: "current_result_reviews",
                columns: new[] { "Status", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_current_results_runs_FromDate_ToDate_Provider",
                table: "current_results_runs",
                columns: new[] { "FromDate", "ToDate", "Provider" });

            migrationBuilder.CreateIndex(
                name: "IX_current_results_runs_Status_StartedAtUtc",
                table: "current_results_runs",
                columns: new[] { "Status", "StartedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "current_result_reviews");

            migrationBuilder.DropTable(
                name: "current_results_runs");
        }
    }
}
