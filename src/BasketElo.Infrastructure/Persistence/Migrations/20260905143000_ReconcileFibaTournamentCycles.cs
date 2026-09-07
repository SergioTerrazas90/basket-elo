using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260905143000_ReconcileFibaTournamentCycles")]
public partial class ReconcileFibaTournamentCycles : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            DECLARE
                old_cycle_id uuid;
                new_cycle_id uuid;
            BEGIN
                INSERT INTO tournament_cycles
                    ("Id", "Key", "Family", "EditionLabel", "DisplayName", "CreatedAtUtc")
                VALUES
                    (md5('tournament-cycle:asiacup:1986')::uuid, 'asiacup-1986', 'FIBA Asia Cup', '1986', 'FIBA Asia Cup 1986', now()),
                    (md5('tournament-cycle:eurobasket-division-b:2007')::uuid, 'eurobasket-division-b-2007', 'EuroBasket Division B', '2007', 'EuroBasket Division B 2007', now()),
                    (md5('tournament-cycle:eurobasket-division-b:2009')::uuid, 'eurobasket-division-b-2009', 'EuroBasket Division B', '2009', 'EuroBasket Division B 2009', now()),
                    (md5('tournament-cycle:eurobasket-division-b:2011')::uuid, 'eurobasket-division-b-2011', 'EuroBasket Division B', '2011', 'EuroBasket Division B 2011', now())
                ON CONFLICT ("Key") DO NOTHING;

                -- The 1986 Asian championship crossed New Year. GSA labels the
                -- archive 1985 and gives exactly six January knockout games the
                -- wrong year. Correct only those known source records.
                UPDATE games
                SET "GameDateTimeUtc" = "GameDateTimeUtc" + interval '1 year',
                    "UpdatedAtUtc" = now()
                WHERE "Source" = 'global-sports-archive'
                  AND "SourceGameId" IN (
                      'gsa-3275567', 'gsa-3275568', 'gsa-3275569',
                      'gsa-3275570', 'gsa-3275571', 'gsa-3275572')
                  AND "GameDateTimeUtc" >= timestamptz '1985-01-01 00:00:00+00'
                  AND "GameDateTimeUtc" < timestamptz '1985-02-01 00:00:00+00';

                UPDATE seasons s
                SET "Label" = '1986',
                    "StartDateUtc" = timestamptz '1985-12-28 00:00:00+00',
                    "EndDateUtc" = timestamptz '1986-01-05 23:59:59+00'
                FROM competitions c
                WHERE c."Id" = s."CompetitionId"
                  AND c."Name" = 'FIBA Asia Cup'
                  AND s."Label" = '1985';

                -- The qualifiers retained their source branding/year, but both
                -- campaigns qualify for finals postponed and played in 2022.
                UPDATE games g
                SET "TournamentCycleId" = target."Id"
                FROM competitions c, seasons s, tournament_cycles target
                WHERE c."Id" = g."CompetitionId"
                  AND s."Id" = g."SeasonId"
                  AND c."Name" IN (
                      'EuroBasket', 'FIBA EuroBasket',
                      'EuroBasket Qualifiers', 'FIBA EuroBasket Qualifiers',
                      'EuroBasket Pre-Qualifiers', 'FIBA EuroBasket Pre-Qualifiers')
                  AND s."Label" = '2021'
                  AND target."Key" = 'eurobasket-2022';

                UPDATE games g
                SET "TournamentCycleId" = target."Id"
                FROM competitions c, seasons s, tournament_cycles target
                WHERE c."Id" = g."CompetitionId"
                  AND s."Id" = g."SeasonId"
                  AND c."Name" = 'FIBA Asia Cup'
                  AND target."Key" = 'asiacup-' || CASE
                      WHEN s."Label" = '2021' THEN '2022'
                      WHEN s."Label" = '1985' THEN '1986'
                      ELSE s."Label"
                  END;

                UPDATE games g
                SET "TournamentCycleId" = target."Id"
                FROM competitions c, seasons s, tournament_cycles target
                WHERE c."Id" = g."CompetitionId"
                  AND s."Id" = g."SeasonId"
                  AND c."Name" IN (
                      'FIBA Asia Cup Qualification',
                      'FIBA Asia Cup Qualifiers',
                      'FIBA Asia Cup Pre-Qualifiers')
                  AND target."Key" = 'asiacup-' || CASE
                      WHEN s."Label" = '2021' THEN '2022'
                      ELSE s."Label"
                  END;

                -- Division B is an official national-team tournament, but is
                -- deliberately independent of the main EuroBasket family.
                UPDATE games g
                SET "TournamentCycleId" = target."Id"
                FROM competitions c, seasons s, tournament_cycles target
                WHERE c."Id" = g."CompetitionId"
                  AND s."Id" = g."SeasonId"
                  AND c."Name" = 'FIBA EuroBasket Division B'
                  AND target."Key" = 'eurobasket-division-b-' || s."Label";

                -- Some live feeds use a regional country and a multi-year
                -- season for the global World Cup qualifiers. The terminal year
                -- is the World Cup edition.
                UPDATE games g
                SET "TournamentCycleId" = target."Id"
                FROM competitions c, seasons s, tournament_cycles target
                WHERE c."Id" = g."CompetitionId"
                  AND s."Id" = g."SeasonId"
                  AND c."Name" IN (
                      'FIBA Basketball World Cup',
                      'FIBA Basketball World Cup Qualifiers',
                      'FIBA Basketball World Cup Pre-Qualifiers',
                      'FIBA World Cup',
                      'FIBA World Cup Qualifiers',
                      'FIBA World Cup Pre-Qualifiers',
                      'FIBA WC Qualification')
                  AND target."Key" = 'worldcup-' || CASE
                      WHEN s."Label" ~ '^[0-9]{4}-[0-9]{4}$' THEN right(s."Label", 4)
                      ELSE s."Label"
                  END;

                -- Move any non-primary references before removing superseded
                -- cycle rows. Duplicate route links are discarded safely.
                FOR old_cycle_id, new_cycle_id IN
                    SELECT old_cycle."Id", new_cycle."Id"
                    FROM (VALUES
                        ('eurobasket-2021', 'eurobasket-2022'),
                        ('asiacup-1985', 'asiacup-1986'),
                        ('asiacup-2021', 'asiacup-2022')) AS mapping(old_key, new_key)
                    JOIN tournament_cycles old_cycle ON old_cycle."Key" = mapping.old_key
                    JOIN tournament_cycles new_cycle ON new_cycle."Key" = mapping.new_key
                LOOP
                    DELETE FROM game_tournament_cycle_links old_link
                    WHERE old_link."TournamentCycleId" = old_cycle_id
                      AND EXISTS (
                          SELECT 1
                          FROM game_tournament_cycle_links new_link
                          WHERE new_link."GameId" = old_link."GameId"
                            AND new_link."TournamentCycleId" = new_cycle_id);

                    UPDATE game_tournament_cycle_links
                    SET "TournamentCycleId" = new_cycle_id
                    WHERE "TournamentCycleId" = old_cycle_id;

                    UPDATE current_result_reviews
                    SET "TournamentCycleId" = new_cycle_id
                    WHERE "TournamentCycleId" = old_cycle_id;
                END LOOP;

                DELETE FROM tournament_cycles
                WHERE "Key" IN ('eurobasket-2021', 'asiacup-1985', 'asiacup-2021', 'asiacup-2002')
                  AND NOT EXISTS (
                      SELECT 1 FROM games
                      WHERE games."TournamentCycleId" = tournament_cycles."Id")
                  AND NOT EXISTS (
                      SELECT 1 FROM game_tournament_cycle_links
                      WHERE game_tournament_cycle_links."TournamentCycleId" = tournament_cycles."Id")
                  AND NOT EXISTS (
                      SELECT 1 FROM current_result_reviews
                      WHERE current_result_reviews."TournamentCycleId" = tournament_cycles."Id");

                IF EXISTS (
                    SELECT 1 FROM tournament_cycles
                    WHERE "Key" IN ('eurobasket-2021', 'asiacup-1985', 'asiacup-2021', 'asiacup-2002')
                ) THEN
                    RAISE EXCEPTION 'Superseded FIBA tournament cycles are still referenced';
                END IF;

                IF EXISTS (
                    SELECT 1
                    FROM games g
                    JOIN competitions c ON c."Id" = g."CompetitionId"
                    JOIN seasons s ON s."Id" = g."SeasonId"
                    LEFT JOIN tournament_cycles tc ON tc."Id" = g."TournamentCycleId"
                    WHERE c."Name" IN (
                        'EuroBasket', 'FIBA EuroBasket',
                        'EuroBasket Qualifiers', 'FIBA EuroBasket Qualifiers',
                        'EuroBasket Pre-Qualifiers', 'FIBA EuroBasket Pre-Qualifiers')
                      AND tc."Key" IS DISTINCT FROM 'eurobasket-' || CASE WHEN s."Label" = '2021' THEN '2022' ELSE s."Label" END
                ) THEN
                    RAISE EXCEPTION 'EuroBasket games remain outside their canonical primary cycle';
                END IF;

                IF EXISTS (
                    SELECT 1
                    FROM games g
                    JOIN competitions c ON c."Id" = g."CompetitionId"
                    JOIN seasons s ON s."Id" = g."SeasonId"
                    LEFT JOIN tournament_cycles tc ON tc."Id" = g."TournamentCycleId"
                    WHERE c."Name" IN (
                        'FIBA Asia Cup', 'FIBA Asia Cup Qualification',
                        'FIBA Asia Cup Qualifiers', 'FIBA Asia Cup Pre-Qualifiers')
                      AND tc."Key" IS DISTINCT FROM 'asiacup-' || CASE
                          WHEN s."Label" = '2021' THEN '2022'
                          WHEN s."Label" = '1985' THEN '1986'
                          ELSE s."Label"
                      END
                ) THEN
                    RAISE EXCEPTION 'FIBA Asia Cup games remain outside their canonical primary cycle';
                END IF;

                IF EXISTS (
                    SELECT 1
                    FROM games g
                    JOIN competitions c ON c."Id" = g."CompetitionId"
                    JOIN seasons s ON s."Id" = g."SeasonId"
                    LEFT JOIN tournament_cycles tc ON tc."Id" = g."TournamentCycleId"
                    WHERE c."Name" = 'FIBA EuroBasket Division B'
                      AND tc."Key" IS DISTINCT FROM 'eurobasket-division-b-' || s."Label"
                ) THEN
                    RAISE EXCEPTION 'EuroBasket Division B games remain outside their independent primary cycle';
                END IF;

                IF EXISTS (
                    SELECT 1
                    FROM games g
                    JOIN competitions c ON c."Id" = g."CompetitionId"
                    JOIN seasons s ON s."Id" = g."SeasonId"
                    LEFT JOIN tournament_cycles tc ON tc."Id" = g."TournamentCycleId"
                    WHERE c."Name" IN (
                        'FIBA Basketball World Cup',
                        'FIBA Basketball World Cup Qualifiers',
                        'FIBA Basketball World Cup Pre-Qualifiers',
                        'FIBA World Cup',
                        'FIBA World Cup Qualifiers',
                        'FIBA World Cup Pre-Qualifiers',
                        'FIBA WC Qualification')
                      AND tc."Key" IS DISTINCT FROM 'worldcup-' || CASE
                          WHEN s."Label" ~ '^[0-9]{4}-[0-9]{4}$' THEN right(s."Label", 4)
                          ELSE s."Label"
                      END
                ) THEN
                    RAISE EXCEPTION 'World Cup games remain outside their canonical primary cycle';
                END IF;

                IF EXISTS (
                    SELECT 1 FROM games
                    WHERE "Source" = 'global-sports-archive'
                      AND "SourceGameId" IN (
                          'gsa-3275567', 'gsa-3275568', 'gsa-3275569',
                          'gsa-3275570', 'gsa-3275571', 'gsa-3275572')
                      AND EXTRACT(YEAR FROM "GameDateTimeUtc") <> 1986
                ) THEN
                    RAISE EXCEPTION 'Known January 1986 Asia Cup dates were not corrected';
                END IF;
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // This is an audited data repair. The verified pre-deployment database
        // backup is the exact rollback path, as in the other reconciliation migrations.
    }
}
