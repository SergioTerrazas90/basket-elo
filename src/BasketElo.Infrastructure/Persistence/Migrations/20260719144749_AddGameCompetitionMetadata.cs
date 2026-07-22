using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGameCompetitionMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CompetitionPhase",
                table: "games",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompetitionRound",
                table: "games",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EloEligible",
                table: "games",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "EloExclusionReason",
                table: "games",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompetitionPhase",
                table: "games");

            migrationBuilder.DropColumn(
                name: "CompetitionRound",
                table: "games");

            migrationBuilder.DropColumn(
                name: "EloEligible",
                table: "games");

            migrationBuilder.DropColumn(
                name: "EloExclusionReason",
                table: "games");
        }
    }
}
