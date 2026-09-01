using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddModelLabRunEloPool : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EloPoolKey",
                table: "model_lab_runs",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "nba");

            migrationBuilder.Sql(
                """
                UPDATE model_lab_runs AS run
                SET "EloPoolKey" = COALESCE(
                    (
                        SELECT competition."EloPoolKey"
                        FROM model_lab_run_scopes AS scope
                        INNER JOIN competitions AS competition
                            ON competition."Id" = scope."CompetitionId"
                        WHERE scope."RunId" = run."Id"
                          AND competition."EloPoolKey" IS NOT NULL
                        ORDER BY scope."Id"
                        LIMIT 1
                    ),
                    'nba');
                """);

            migrationBuilder.CreateIndex(
                name: "IX_model_lab_runs_OwnerUserId_EloPoolKey_CreatedAtUtc",
                table: "model_lab_runs",
                columns: new[] { "OwnerUserId", "EloPoolKey", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_model_lab_runs_OwnerUserId_EloPoolKey_CreatedAtUtc",
                table: "model_lab_runs");

            migrationBuilder.DropColumn(
                name: "EloPoolKey",
                table: "model_lab_runs");
        }
    }
}
