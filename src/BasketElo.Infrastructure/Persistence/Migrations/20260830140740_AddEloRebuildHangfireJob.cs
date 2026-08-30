using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEloRebuildHangfireJob : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HangfireJobId",
                table: "elo_rebuild_runs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_elo_rebuild_runs_Status_HangfireJobId_QueuedAtUtc",
                table: "elo_rebuild_runs",
                columns: new[] { "Status", "HangfireJobId", "QueuedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_elo_rebuild_runs_Status_HangfireJobId_QueuedAtUtc",
                table: "elo_rebuild_runs");

            migrationBuilder.DropColumn(
                name: "HangfireJobId",
                table: "elo_rebuild_runs");
        }
    }
}
