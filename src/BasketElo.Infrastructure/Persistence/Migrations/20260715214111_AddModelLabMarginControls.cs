using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddModelLabMarginControls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "MarginDampenerFactor",
                table: "model_lab_model_versions",
                type: "numeric(6,2)",
                precision: 6,
                scale: 2,
                nullable: false,
                defaultValue: 5m);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxMarginMultiplier",
                table: "model_lab_model_versions",
                type: "numeric(6,4)",
                precision: 6,
                scale: 4,
                nullable: false,
                defaultValue: 1.5m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MarginDampenerFactor",
                table: "model_lab_model_versions");

            migrationBuilder.DropColumn(
                name: "MaxMarginMultiplier",
                table: "model_lab_model_versions");
        }
    }
}
