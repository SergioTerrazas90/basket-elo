using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260822160000_AddCurrentResultCycleAssignment")]
public partial class AddCurrentResultCycleAssignment : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "TournamentCycleId",
            table: "current_result_reviews",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_current_result_reviews_TournamentCycleId",
            table: "current_result_reviews",
            column: "TournamentCycleId");

        migrationBuilder.AddForeignKey(
            name: "FK_current_result_reviews_tournament_cycles_TournamentCycleId",
            table: "current_result_reviews",
            column: "TournamentCycleId",
            principalTable: "tournament_cycles",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_current_result_reviews_tournament_cycles_TournamentCycleId",
            table: "current_result_reviews");

        migrationBuilder.DropIndex(
            name: "IX_current_result_reviews_TournamentCycleId",
            table: "current_result_reviews");

        migrationBuilder.DropColumn(
            name: "TournamentCycleId",
            table: "current_result_reviews");
    }
}
