using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddModelLabRunRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAtUtc",
                table: "model_lab_runs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsRetained",
                table: "model_lab_runs",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateIndex(
                name: "IX_model_lab_runs_IsRetained_ExpiresAtUtc",
                table: "model_lab_runs",
                columns: new[] { "IsRetained", "ExpiresAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_model_lab_runs_IsRetained_ExpiresAtUtc",
                table: "model_lab_runs");

            migrationBuilder.DropColumn(
                name: "ExpiresAtUtc",
                table: "model_lab_runs");

            migrationBuilder.DropColumn(
                name: "IsRetained",
                table: "model_lab_runs");
        }
    }
}
