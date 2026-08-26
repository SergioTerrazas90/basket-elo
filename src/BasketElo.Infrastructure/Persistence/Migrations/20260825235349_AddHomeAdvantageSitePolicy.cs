using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHomeAdvantageSitePolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsNeutralSite",
                table: "games",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HomeAdvantagePolicy",
                table: "competitions",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "automatic");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsNeutralSite",
                table: "games");

            migrationBuilder.DropColumn(
                name: "HomeAdvantagePolicy",
                table: "competitions");
        }
    }
}
