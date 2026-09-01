using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllowModelLabComparisons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_model_lab_runs_OwnerUserId",
                table: "model_lab_runs");

            migrationBuilder.AddColumn<Guid>(
                name: "ComparisonGroupId",
                table: "model_lab_runs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_model_lab_runs_OwnerUserId",
                table: "model_lab_runs",
                column: "OwnerUserId",
                unique: true,
                filter: "\"Status\" IN ('queued', 'running') AND \"ComparisonGroupId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_model_lab_runs_OwnerUserId_ComparisonGroupId",
                table: "model_lab_runs",
                columns: new[] { "OwnerUserId", "ComparisonGroupId" },
                filter: "\"Status\" IN ('queued', 'running') AND \"ComparisonGroupId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_model_lab_runs_OwnerUserId",
                table: "model_lab_runs");

            migrationBuilder.DropIndex(
                name: "IX_model_lab_runs_OwnerUserId_ComparisonGroupId",
                table: "model_lab_runs");

            migrationBuilder.DropColumn(
                name: "ComparisonGroupId",
                table: "model_lab_runs");

            migrationBuilder.CreateIndex(
                name: "IX_model_lab_runs_OwnerUserId",
                table: "model_lab_runs",
                column: "OwnerUserId",
                unique: true,
                filter: "\"Status\" IN ('queued', 'running')");
        }
    }
}
