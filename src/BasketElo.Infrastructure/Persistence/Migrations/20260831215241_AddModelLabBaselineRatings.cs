using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddModelLabBaselineRatings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "BaselineElo",
                table: "model_lab_run_ratings",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BaselineRank",
                table: "model_lab_run_ratings",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BaselineElo",
                table: "model_lab_run_ratings");

            migrationBuilder.DropColumn(
                name: "BaselineRank",
                table: "model_lab_run_ratings");
        }
    }
}
