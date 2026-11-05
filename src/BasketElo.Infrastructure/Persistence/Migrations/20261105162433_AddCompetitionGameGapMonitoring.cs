using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCompetitionGameGapMonitoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MonitorGameGaps",
                table: "competitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("""
                UPDATE competitions
                SET "MonitorGameGaps" = TRUE
                WHERE "Name" IN (
                    'A1',
                    'ABA League',
                    'ACB',
                    'Basket Ligaen',
                    'BBL',
                    'BLNO',
                    'BNXT League',
                    'Champions League',
                    'Cyprus Basket League',
                    'Eurocup',
                    'Euroleague',
                    'FBU Superleague',
                    'FIBA Europe Cup',
                    'First League',
                    'Korisliiga',
                    'Latvia-Estonian League',
                    'Liga UPC',
                    'LKL',
                    'LNB',
                    'NBA',
                    'NBL',
                    'NB I. A',
                    'Premijer liga',
                    'Prva Liga',
                    'Serie A',
                    'SLB',
                    'Slovakia Basketball League',
                    'Super League',
                    'Super Ligi',
                    'Superliga',
                    'Tauron Basket Liga',
                    'VTB United League'
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MonitorGameGaps",
                table: "competitions");
        }
    }
}
