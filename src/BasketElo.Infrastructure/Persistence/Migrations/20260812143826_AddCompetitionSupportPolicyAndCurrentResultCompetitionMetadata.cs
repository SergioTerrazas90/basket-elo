using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCompetitionSupportPolicyAndCurrentResultCompetitionMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_competition_aliases_Source_SourceCompetitionId",
                table: "competition_aliases");

            migrationBuilder.AddColumn<int>(
                name: "UnsupportedSkipped",
                table: "current_results_runs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SourceCompetitionId",
                table: "current_result_reviews",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupportPolicy",
                table: "competitions",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "supported");

            migrationBuilder.CreateIndex(
                name: "IX_competition_aliases_Source_SourceCompetitionId",
                table: "competition_aliases",
                columns: new[] { "Source", "SourceCompetitionId" });

            migrationBuilder.CreateIndex(
                name: "IX_competition_aliases_Source_SourceCompetitionId_AliasName",
                table: "competition_aliases",
                columns: new[] { "Source", "SourceCompetitionId", "AliasName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_competition_aliases_Source_SourceCompetitionId",
                table: "competition_aliases");

            migrationBuilder.DropIndex(
                name: "IX_competition_aliases_Source_SourceCompetitionId_AliasName",
                table: "competition_aliases");

            migrationBuilder.DropColumn(
                name: "UnsupportedSkipped",
                table: "current_results_runs");

            migrationBuilder.DropColumn(
                name: "SourceCompetitionId",
                table: "current_result_reviews");

            migrationBuilder.DropColumn(
                name: "SupportPolicy",
                table: "competitions");

            migrationBuilder.CreateIndex(
                name: "IX_competition_aliases_Source_SourceCompetitionId",
                table: "competition_aliases",
                columns: new[] { "Source", "SourceCompetitionId" },
                unique: true);
        }
    }
}
