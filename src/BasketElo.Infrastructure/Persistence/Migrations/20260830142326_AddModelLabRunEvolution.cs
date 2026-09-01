using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddModelLabRunEvolution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "model_lab_run_evolution_points",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    GameId = table.Column<Guid>(type: "uuid", nullable: false),
                    GameDateTimeUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompetitionName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Season = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Elo = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    EloDelta = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Rank = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_model_lab_run_evolution_points", x => x.Id);
                    table.ForeignKey(
                        name: "FK_model_lab_run_evolution_points_application_users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "application_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_model_lab_run_evolution_points_games_GameId",
                        column: x => x.GameId,
                        principalTable: "games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_model_lab_run_evolution_points_model_lab_runs_RunId",
                        column: x => x.RunId,
                        principalTable: "model_lab_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_model_lab_run_evolution_points_teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_model_lab_run_evolution_points_GameId",
                table: "model_lab_run_evolution_points",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_model_lab_run_evolution_points_OwnerUserId_RunId",
                table: "model_lab_run_evolution_points",
                columns: new[] { "OwnerUserId", "RunId" });

            migrationBuilder.CreateIndex(
                name: "IX_model_lab_run_evolution_points_RunId_TeamId_GameDateTimeUtc",
                table: "model_lab_run_evolution_points",
                columns: new[] { "RunId", "TeamId", "GameDateTimeUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_model_lab_run_evolution_points_RunId_TeamId_GameId",
                table: "model_lab_run_evolution_points",
                columns: new[] { "RunId", "TeamId", "GameId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_model_lab_run_evolution_points_TeamId",
                table: "model_lab_run_evolution_points",
                column: "TeamId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "model_lab_run_evolution_points");
        }
    }
}
