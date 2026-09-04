using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPremiumMonthlyModelRunLimit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "model_lab_monthly_run_usage",
                columns: table => new
                {
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    MonthStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SlotNumber = table.Column<int>(type: "integer", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsageType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_model_lab_monthly_run_usage", x => new { x.OwnerUserId, x.MonthStartUtc, x.SlotNumber });
                    table.ForeignKey(
                        name: "FK_model_lab_monthly_run_usage_application_users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "application_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_model_lab_monthly_run_usage_OwnerUserId_CreatedAtUtc",
                table: "model_lab_monthly_run_usage",
                columns: new[] { "OwnerUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_model_lab_monthly_run_usage_RunId",
                table: "model_lab_monthly_run_usage",
                column: "RunId");

            migrationBuilder.Sql(
                """
                INSERT INTO model_lab_monthly_run_usage
                    ("OwnerUserId", "MonthStartUtc", "SlotNumber", "RunId", "UsageType", "CreatedAtUtc")
                SELECT
                    "OwnerUserId",
                    date_trunc('month', "CreatedAtUtc"),
                    ROW_NUMBER() OVER (
                        PARTITION BY "OwnerUserId", date_trunc('month', "CreatedAtUtc")
                        ORDER BY "CreatedAtUtc", "Id")::integer,
                    "Id",
                    'run',
                    "CreatedAtUtc"
                FROM model_lab_runs;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "model_lab_monthly_run_usage");
        }
    }
}
