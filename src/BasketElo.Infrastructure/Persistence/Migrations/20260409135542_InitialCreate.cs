using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "competitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CountryCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    Tier = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_competitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elo_rebuild_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FromGameDateTimeUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RulesetVersion = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elo_rebuild_runs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "teams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CanonicalName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CountryCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_teams", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "seasons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StartDateUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndDateUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_seasons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_seasons_competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalTable: "competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ranking_snapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    Elo = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ranking_snapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ranking_snapshots_teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "team_aliases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SourceTeamId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AliasName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ValidFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ValidToUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_team_aliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_team_aliases_teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "games",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SourceGameId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeasonId = table.Column<Guid>(type: "uuid", nullable: false),
                    GameDateTimeUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    HomeTeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    AwayTeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    HomeScore = table.Column<short>(type: "smallint", nullable: true),
                    AwayScore = table.Column<short>(type: "smallint", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    IngestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_games", x => x.Id);
                    table.ForeignKey(
                        name: "FK_games_competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalTable: "competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_games_seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_games_teams_AwayTeamId",
                        column: x => x.AwayTeamId,
                        principalTable: "teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_games_teams_HomeTeamId",
                        column: x => x.HomeTeamId,
                        principalTable: "teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rating_history",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GameId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    OpponentTeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    GameDateTimeUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PreElo = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    PostElo = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    EloDelta = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    KFactorUsed = table.Column<int>(type: "integer", nullable: false),
                    ExpectedScore = table.Column<decimal>(type: "numeric(6,4)", precision: 6, scale: 4, nullable: false),
                    ActualScore = table.Column<decimal>(type: "numeric(4,2)", precision: 4, scale: 2, nullable: false),
                    GamesPlayedBefore = table.Column<int>(type: "integer", nullable: false),
                    RatingPositionAfter = table.Column<int>(type: "integer", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rating_history", x => x.Id);
                    table.ForeignKey(
                        name: "FK_rating_history_games_GameId",
                        column: x => x.GameId,
                        principalTable: "games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_rating_history_teams_OpponentTeamId",
                        column: x => x.OpponentTeamId,
                        principalTable: "teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_rating_history_teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "team_ratings",
                columns: table => new
                {
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    Elo = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    GamesPlayed = table.Column<int>(type: "integer", nullable: false),
                    LastGameId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_team_ratings", x => x.TeamId);
                    table.ForeignKey(
                        name: "FK_team_ratings_games_LastGameId",
                        column: x => x.LastGameId,
                        principalTable: "games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_team_ratings_teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_competitions_Name_CountryCode",
                table: "competitions",
                columns: new[] { "Name", "CountryCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elo_rebuild_runs_StartedAtUtc",
                table: "elo_rebuild_runs",
                column: "StartedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_games_AwayTeamId",
                table: "games",
                column: "AwayTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_games_CompetitionId_GameDateTimeUtc",
                table: "games",
                columns: new[] { "CompetitionId", "GameDateTimeUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_games_GameDateTimeUtc",
                table: "games",
                column: "GameDateTimeUtc");

            migrationBuilder.CreateIndex(
                name: "IX_games_HomeTeamId",
                table: "games",
                column: "HomeTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_games_SeasonId",
                table: "games",
                column: "SeasonId");

            migrationBuilder.CreateIndex(
                name: "IX_games_Source_SourceGameId",
                table: "games",
                columns: new[] { "Source", "SourceGameId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ranking_snapshots_SnapshotDate_Position",
                table: "ranking_snapshots",
                columns: new[] { "SnapshotDate", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_ranking_snapshots_SnapshotDate_TeamId",
                table: "ranking_snapshots",
                columns: new[] { "SnapshotDate", "TeamId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ranking_snapshots_TeamId",
                table: "ranking_snapshots",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_rating_history_GameId_TeamId",
                table: "rating_history",
                columns: new[] { "GameId", "TeamId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rating_history_OpponentTeamId",
                table: "rating_history",
                column: "OpponentTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_rating_history_TeamId_GameDateTimeUtc",
                table: "rating_history",
                columns: new[] { "TeamId", "GameDateTimeUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_seasons_CompetitionId_Label",
                table: "seasons",
                columns: new[] { "CompetitionId", "Label" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_team_aliases_Source_SourceTeamId",
                table: "team_aliases",
                columns: new[] { "Source", "SourceTeamId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_team_aliases_TeamId_AliasName",
                table: "team_aliases",
                columns: new[] { "TeamId", "AliasName" });

            migrationBuilder.CreateIndex(
                name: "IX_team_ratings_LastGameId",
                table: "team_ratings",
                column: "LastGameId");

            migrationBuilder.CreateIndex(
                name: "IX_teams_CanonicalName",
                table: "teams",
                column: "CanonicalName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "elo_rebuild_runs");

            migrationBuilder.DropTable(
                name: "ranking_snapshots");

            migrationBuilder.DropTable(
                name: "rating_history");

            migrationBuilder.DropTable(
                name: "team_aliases");

            migrationBuilder.DropTable(
                name: "team_ratings");

            migrationBuilder.DropTable(
                name: "games");

            migrationBuilder.DropTable(
                name: "seasons");

            migrationBuilder.DropTable(
                name: "teams");

            migrationBuilder.DropTable(
                name: "competitions");
        }
    }
}
