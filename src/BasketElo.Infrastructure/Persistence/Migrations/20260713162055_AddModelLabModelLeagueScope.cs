using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddModelLabModelLeagueScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LeagueName",
                table: "model_lab_models",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "ACB");

            migrationBuilder.CreateIndex(
                name: "IX_model_lab_models_OwnerUserId_LeagueName",
                table: "model_lab_models",
                columns: new[] { "OwnerUserId", "LeagueName" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_model_lab_models_OwnerUserId_LeagueName",
                table: "model_lab_models");

            migrationBuilder.DropColumn(
                name: "LeagueName",
                table: "model_lab_models");
        }
    }
}
