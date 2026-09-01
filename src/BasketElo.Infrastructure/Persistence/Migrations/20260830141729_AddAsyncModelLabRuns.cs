using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAsyncModelLabRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HangfireJobId",
                table: "model_lab_runs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProgressPercent",
                table: "model_lab_runs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ProgressStage",
                table: "model_lab_runs",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "Completed");

            migrationBuilder.AddColumn<string>(
                name: "RequestCompetitionIdsJson",
                table: "model_lab_runs",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StartedAtUtc",
                table: "model_lab_runs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_model_lab_runs_OwnerUserId",
                table: "model_lab_runs",
                column: "OwnerUserId",
                unique: true,
                filter: "\"Status\" IN ('queued', 'running')");

            migrationBuilder.CreateIndex(
                name: "IX_model_lab_runs_Status_HangfireJobId_CreatedAtUtc",
                table: "model_lab_runs",
                columns: new[] { "Status", "HangfireJobId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_model_lab_runs_OwnerUserId",
                table: "model_lab_runs");

            migrationBuilder.DropIndex(
                name: "IX_model_lab_runs_Status_HangfireJobId_CreatedAtUtc",
                table: "model_lab_runs");

            migrationBuilder.DropColumn(
                name: "HangfireJobId",
                table: "model_lab_runs");

            migrationBuilder.DropColumn(
                name: "ProgressPercent",
                table: "model_lab_runs");

            migrationBuilder.DropColumn(
                name: "ProgressStage",
                table: "model_lab_runs");

            migrationBuilder.DropColumn(
                name: "RequestCompetitionIdsJson",
                table: "model_lab_runs");

            migrationBuilder.DropColumn(
                name: "StartedAtUtc",
                table: "model_lab_runs");
        }
    }
}
