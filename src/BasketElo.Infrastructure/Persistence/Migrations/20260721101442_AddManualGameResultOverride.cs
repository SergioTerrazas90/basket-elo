using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

public partial class AddManualGameResultOverride : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "HasManualResultOverride",
            table: "games",
            type: "boolean",
            nullable: false,
            defaultValue: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "HasManualResultOverride",
            table: "games");
    }
}
