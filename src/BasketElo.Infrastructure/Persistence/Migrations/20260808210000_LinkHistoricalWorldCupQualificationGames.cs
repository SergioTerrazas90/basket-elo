using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260808210000_LinkHistoricalWorldCupQualificationGames")]
public partial class LinkHistoricalWorldCupQualificationGames : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "game_tournament_cycle_links",
            columns: table => new
            {
                GameId = table.Column<Guid>(type: "uuid", nullable: false),
                TournamentCycleId = table.Column<Guid>(type: "uuid", nullable: false),
                Stage = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_game_tournament_cycle_links", x => new { x.GameId, x.TournamentCycleId });
                table.ForeignKey(
                    name: "FK_game_tournament_cycle_links_games_GameId",
                    column: x => x.GameId,
                    principalTable: "games",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_game_tournament_cycle_links_tournament_cycles_TournamentCycleId",
                    column: x => x.TournamentCycleId,
                    principalTable: "tournament_cycles",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_game_tournament_cycle_links_TournamentCycleId_Stage_GameId",
            table: "game_tournament_cycle_links",
            columns: new[] { "TournamentCycleId", "Stage", "GameId" });

        migrationBuilder.Sql("""
            WITH routes("TargetYear", "SourceSeason", "CompetitionName") AS (
                VALUES
                    (1950, '1948', 'Summer Olympics'),
                    (1954, '1952', 'Summer Olympics'),
                    (1959, '1956', 'Summer Olympics'),
                    (1963, '1960', 'Summer Olympics'),
                    (1967, '1965', 'EuroBasket'),
                    (1967, '1965', 'FIBA Asia Cup'),
                    (1967, '1965', 'FIBA AfroBasket'),
                    (1970, '1969', 'EuroBasket'),
                    (1970, '1969', 'FIBA Asia Cup'),
                    (1970, '1969', 'FIBA AfroBasket'),
                    (1974, '1973', 'EuroBasket'),
                    (1974, '1973', 'FIBA Asia Cup'),
                    (1974, '1973', 'FIBA AfroBasket'),
                    (1978, '1977', 'EuroBasket'),
                    (1978, '1977', 'FIBA Asia Cup'),
                    (1978, '1977', 'FIBA AfroBasket'),
                    (1982, '1981', 'EuroBasket'),
                    (1982, '1981', 'FIBA Asia Cup'),
                    (1982, '1981', 'FIBA AfroBasket'),
                    (1982, '1981', 'FIBA Oceania Championship'),
                    (1982, '1980', 'Summer Olympics'),
                    (1986, '1985', 'EuroBasket'),
                    (1986, '1985', 'FIBA Asia Cup'),
                    (1986, '1985', 'FIBA AfroBasket'),
                    (1986, '1985', 'FIBA Oceania Championship'),
                    (1986, '1984', 'Summer Olympics'),
                    (1990, '1989', 'EuroBasket'),
                    (1990, '1989', 'FIBA Asia Cup'),
                    (1990, '1989', 'FIBA AmeriCup'),
                    (1990, '1989', 'FIBA AfroBasket'),
                    (1990, '1989', 'FIBA Oceania Championship'),
                    (1990, '1988', 'Summer Olympics'),
                    (1994, '1993', 'EuroBasket'),
                    (1994, '1993', 'FIBA Asia Cup'),
                    (1994, '1993', 'FIBA AmeriCup'),
                    (1994, '1993', 'FIBA AfroBasket'),
                    (1994, '1993', 'FIBA Oceania Championship'),
                    (1994, '1992', 'Summer Olympics'),
                    (1998, '1997', 'EuroBasket'),
                    (1998, '1997', 'FIBA Asia Cup'),
                    (1998, '1997', 'FIBA AmeriCup'),
                    (1998, '1997', 'FIBA AfroBasket'),
                    (1998, '1997', 'FIBA Oceania Championship'),
                    (1998, '1996', 'Summer Olympics'),
                    (2002, '2001', 'EuroBasket'),
                    (2002, '2001', 'FIBA Asia Cup'),
                    (2002, '2001', 'FIBA AmeriCup'),
                    (2002, '2001', 'FIBA AfroBasket'),
                    (2002, '2001', 'FIBA Oceania Championship'),
                    (2002, '2000', 'Summer Olympics'),
                    (2006, '2005', 'EuroBasket'),
                    (2006, '2005', 'FIBA Asia Cup'),
                    (2006, '2005', 'FIBA AmeriCup'),
                    (2006, '2005', 'FIBA AfroBasket'),
                    (2006, '2005', 'FIBA Oceania Championship'),
                    (2006, '2004', 'Summer Olympics'),
                    (2010, '2009', 'EuroBasket'),
                    (2010, '2009', 'FIBA Asia Cup'),
                    (2010, '2009', 'FIBA AmeriCup'),
                    (2010, '2009', 'FIBA AfroBasket'),
                    (2010, '2009', 'FIBA Oceania Championship'),
                    (2010, '2008', 'Summer Olympics'),
                    (2014, '2013', 'EuroBasket'),
                    (2014, '2013', 'FIBA Asia Cup'),
                    (2014, '2013', 'FIBA AmeriCup'),
                    (2014, '2013', 'FIBA AfroBasket'),
                    (2014, '2013', 'FIBA Oceania Championship'),
                    (2014, '2012', 'Summer Olympics')
            ),
            available_routes AS (
                SELECT DISTINCT r."TargetYear"
                FROM routes r
                JOIN seasons s ON s."Label" = r."SourceSeason"
                JOIN competitions c ON c."Id" = s."CompetitionId"
                    AND c."Name" = r."CompetitionName"
                JOIN games g ON g."SeasonId" = s."Id"
            )
            INSERT INTO tournament_cycles
                ("Id", "Key", "Family", "EditionLabel", "DisplayName", "CreatedAtUtc")
            SELECT md5('worldcup:' || "TargetYear"::text)::uuid,
                   'worldcup-' || "TargetYear"::text,
                   'FIBA Basketball World Cup',
                   "TargetYear"::text,
                   'FIBA Basketball World Cup ' || "TargetYear"::text,
                   CURRENT_TIMESTAMP
            FROM available_routes
            ON CONFLICT ("Key") DO NOTHING;

            WITH routes("TargetYear", "SourceSeason", "CompetitionName") AS (
                VALUES
                    (1950, '1948', 'Summer Olympics'), (1954, '1952', 'Summer Olympics'),
                    (1959, '1956', 'Summer Olympics'), (1963, '1960', 'Summer Olympics'),
                    (1967, '1965', 'EuroBasket'), (1967, '1965', 'FIBA Asia Cup'), (1967, '1965', 'FIBA AfroBasket'),
                    (1970, '1969', 'EuroBasket'), (1970, '1969', 'FIBA Asia Cup'), (1970, '1969', 'FIBA AfroBasket'),
                    (1974, '1973', 'EuroBasket'), (1974, '1973', 'FIBA Asia Cup'), (1974, '1973', 'FIBA AfroBasket'),
                    (1978, '1977', 'EuroBasket'), (1978, '1977', 'FIBA Asia Cup'), (1978, '1977', 'FIBA AfroBasket'),
                    (1982, '1981', 'EuroBasket'), (1982, '1981', 'FIBA Asia Cup'), (1982, '1981', 'FIBA AfroBasket'), (1982, '1981', 'FIBA Oceania Championship'),
                    (1982, '1980', 'Summer Olympics'), (1986, '1985', 'EuroBasket'), (1986, '1985', 'FIBA Asia Cup'), (1986, '1985', 'FIBA AfroBasket'), (1986, '1985', 'FIBA Oceania Championship'),
                    (1986, '1984', 'Summer Olympics'), (1990, '1989', 'EuroBasket'), (1990, '1989', 'FIBA Asia Cup'), (1990, '1989', 'FIBA AmeriCup'), (1990, '1989', 'FIBA AfroBasket'), (1990, '1989', 'FIBA Oceania Championship'),
                    (1990, '1988', 'Summer Olympics'), (1994, '1993', 'EuroBasket'), (1994, '1993', 'FIBA Asia Cup'), (1994, '1993', 'FIBA AmeriCup'), (1994, '1993', 'FIBA AfroBasket'), (1994, '1993', 'FIBA Oceania Championship'),
                    (1994, '1992', 'Summer Olympics'), (1998, '1997', 'EuroBasket'), (1998, '1997', 'FIBA Asia Cup'), (1998, '1997', 'FIBA AmeriCup'), (1998, '1997', 'FIBA AfroBasket'), (1998, '1997', 'FIBA Oceania Championship'),
                    (1998, '1996', 'Summer Olympics'), (2002, '2001', 'EuroBasket'), (2002, '2001', 'FIBA Asia Cup'), (2002, '2001', 'FIBA AmeriCup'), (2002, '2001', 'FIBA AfroBasket'), (2002, '2001', 'FIBA Oceania Championship'),
                    (2002, '2000', 'Summer Olympics'), (2006, '2005', 'EuroBasket'), (2006, '2005', 'FIBA Asia Cup'), (2006, '2005', 'FIBA AmeriCup'), (2006, '2005', 'FIBA AfroBasket'), (2006, '2005', 'FIBA Oceania Championship'),
                    (2006, '2004', 'Summer Olympics'), (2010, '2009', 'EuroBasket'), (2010, '2009', 'FIBA Asia Cup'), (2010, '2009', 'FIBA AmeriCup'), (2010, '2009', 'FIBA AfroBasket'), (2010, '2009', 'FIBA Oceania Championship'),
                    (2010, '2008', 'Summer Olympics'), (2014, '2013', 'EuroBasket'), (2014, '2013', 'FIBA Asia Cup'), (2014, '2013', 'FIBA AmeriCup'), (2014, '2013', 'FIBA AfroBasket'), (2014, '2013', 'FIBA Oceania Championship'), (2014, '2012', 'Summer Olympics')
            ),
            candidates AS (
                SELECT
                    g."Id" AS "GameId",
                    tc."Id" AS "TournamentCycleId",
                    ROW_NUMBER() OVER (
                        PARTITION BY r."TargetYear", g."GameDateTimeUtc"::date,
                            LOWER(ht."CanonicalName"), LOWER(at."CanonicalName"),
                            g."HomeScore", g."AwayScore"
                        ORDER BY CASE g."Source"
                            WHEN 'fiba' THEN 1
                            WHEN 'global-sports-archive' THEN 2
                            WHEN 'wikipedia' THEN 3
                            ELSE 9
                        END, g."Id"
                    ) AS "RowNumber"
                FROM routes r
                JOIN seasons s ON s."Label" = r."SourceSeason"
                JOIN competitions c ON c."Id" = s."CompetitionId"
                    AND c."Name" = r."CompetitionName"
                JOIN games g ON g."SeasonId" = s."Id"
                JOIN tournament_cycles tc ON tc."Key" = 'worldcup-' || r."TargetYear"::text
                JOIN teams ht ON ht."Id" = g."HomeTeamId"
                JOIN teams at ON at."Id" = g."AwayTeamId"
                WHERE g."Source" IN ('fiba', 'global-sports-archive', 'wikipedia')
            )
            INSERT INTO game_tournament_cycle_links
                ("GameId", "TournamentCycleId", "Stage", "Source")
            SELECT "GameId", "TournamentCycleId", 'qualifier',
                   'historical-world-cup-qualification'
            FROM candidates
            WHERE "RowNumber" = 1
            ON CONFLICT ("GameId", "TournamentCycleId") DO NOTHING;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "game_tournament_cycle_links");
    }
}
