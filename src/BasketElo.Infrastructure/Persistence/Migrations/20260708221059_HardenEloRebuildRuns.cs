using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HardenEloRebuildRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_elo_rebuild_runs_StartedAtUtc",
                table: "elo_rebuild_runs");

            migrationBuilder.AddColumn<DateTime>(
                name: "QueuedAtUtc",
                table: "elo_rebuild_runs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "elo_rebuild_runs"
                SET "QueuedAtUtc" = "StartedAtUtc"
                WHERE "QueuedAtUtc" IS NULL
                """);

            migrationBuilder.AlterColumn<DateTime>(
                name: "QueuedAtUtc",
                table: "elo_rebuild_runs",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "StartedAtUtc",
                table: "elo_rebuild_runs",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.CreateIndex(
                name: "IX_elo_rebuild_runs_QueuedAtUtc",
                table: "elo_rebuild_runs",
                column: "QueuedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_elo_rebuild_runs_RulesetVersion",
                table: "elo_rebuild_runs",
                column: "RulesetVersion",
                unique: true,
                filter: "\"Status\" IN ('pending', 'running')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_elo_rebuild_runs_QueuedAtUtc",
                table: "elo_rebuild_runs");

            migrationBuilder.DropIndex(
                name: "IX_elo_rebuild_runs_RulesetVersion",
                table: "elo_rebuild_runs");

            migrationBuilder.Sql(
                """
                UPDATE "elo_rebuild_runs"
                SET "StartedAtUtc" = COALESCE("StartedAtUtc", "QueuedAtUtc", "CreatedAtUtc")
                WHERE "StartedAtUtc" IS NULL
                """);

            migrationBuilder.DropColumn(
                name: "QueuedAtUtc",
                table: "elo_rebuild_runs");

            migrationBuilder.AlterColumn<DateTime>(
                name: "StartedAtUtc",
                table: "elo_rebuild_runs",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_elo_rebuild_runs_StartedAtUtc",
                table: "elo_rebuild_runs",
                column: "StartedAtUtc");
        }
    }
}
