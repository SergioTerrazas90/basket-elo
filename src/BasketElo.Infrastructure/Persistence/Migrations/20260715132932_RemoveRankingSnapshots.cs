using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveRankingSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ranking_snapshots");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ranking_snapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Elo = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    RulesetVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SnapshotDate = table.Column<DateOnly>(type: "date", nullable: false)
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

            migrationBuilder.CreateIndex(
                name: "IX_ranking_snapshots_SnapshotDate_RulesetVersion_Position",
                table: "ranking_snapshots",
                columns: new[] { "SnapshotDate", "RulesetVersion", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_ranking_snapshots_SnapshotDate_TeamId_RulesetVersion",
                table: "ranking_snapshots",
                columns: new[] { "SnapshotDate", "TeamId", "RulesetVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ranking_snapshots_TeamId",
                table: "ranking_snapshots",
                column: "TeamId");
        }
    }
}
