using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTournamentCycles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TournamentCycleId",
                table: "games",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "tournament_cycles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Family = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EditionLabel = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    StartDateUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndDateUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tournament_cycles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_games_TournamentCycleId_GameDateTimeUtc",
                table: "games",
                columns: new[] { "TournamentCycleId", "GameDateTimeUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_tournament_cycles_Family_EditionLabel",
                table: "tournament_cycles",
                columns: new[] { "Family", "EditionLabel" });

            migrationBuilder.CreateIndex(
                name: "IX_tournament_cycles_Key",
                table: "tournament_cycles",
                column: "Key",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_games_tournament_cycles_TournamentCycleId",
                table: "games",
                column: "TournamentCycleId",
                principalTable: "tournament_cycles",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Existing EuroBasket stages use separate Competition/Season rows,
            // but their single-year season label is the target edition. Seed
            // the shared cycle and attach those rows during the migration.
            migrationBuilder.Sql(
                """
                INSERT INTO tournament_cycles ("Id", "Key", "Family", "EditionLabel", "DisplayName", "CreatedAtUtc")
                SELECT md5('eurobasket:' || s."Label")::uuid,
                       'eurobasket-' || s."Label",
                       'EuroBasket',
                       s."Label",
                       'EuroBasket ' || s."Label",
                       CURRENT_TIMESTAMP
                FROM games g
                INNER JOIN seasons s ON s."Id" = g."SeasonId"
                INNER JOIN competitions c ON c."Id" = g."CompetitionId"
                WHERE lower(c."Name") IN (
                    'eurobasket',
                    'fiba eurobasket',
                    'eurobasket qualifiers',
                    'fiba eurobasket qualifiers',
                    'eurobasket pre-qualifiers',
                    'fiba eurobasket pre-qualifiers'
                )
                GROUP BY s."Label"
                ON CONFLICT ("Key") DO NOTHING;

                UPDATE games g
                SET "TournamentCycleId" = tc."Id"
                FROM seasons s, competitions c, tournament_cycles tc
                WHERE s."Id" = g."SeasonId"
                  AND c."Id" = g."CompetitionId"
                  AND tc."Key" = 'eurobasket-' || s."Label"
                  AND lower(c."Name") IN (
                      'eurobasket',
                      'fiba eurobasket',
                      'eurobasket qualifiers',
                      'fiba eurobasket qualifiers',
                      'eurobasket pre-qualifiers',
                      'fiba eurobasket pre-qualifiers'
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_games_tournament_cycles_TournamentCycleId",
                table: "games");

            migrationBuilder.DropTable(
                name: "tournament_cycles");

            migrationBuilder.DropIndex(
                name: "IX_games_TournamentCycleId_GameDateTimeUtc",
                table: "games");

            migrationBuilder.DropColumn(
                name: "TournamentCycleId",
                table: "games");
        }
    }
}
