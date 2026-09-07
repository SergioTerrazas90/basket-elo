using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260907210000_AddEuroBasketDivisionB1987BookGames")]
public partial class AddEuroBasketDivisionB1987BookGames : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            DECLARE competition_id uuid; season_id uuid; cycle_id uuid;
            BEGIN
                SELECT "Id" INTO competition_id FROM competitions
                WHERE "Name" = 'FIBA EuroBasket Division B' AND "CountryCode" IS NULL;
                IF competition_id IS NULL THEN RAISE EXCEPTION 'FIBA EuroBasket Division B competition is missing'; END IF;
                INSERT INTO tournament_cycles ("Id", "Key", "Family", "EditionLabel", "DisplayName", "CreatedAtUtc")
                VALUES (md5('tournament-cycle:eurobasket-division-b:1987')::uuid, 'eurobasket-division-b-1987',
                        'EuroBasket Division B', '1987', 'EuroBasket Division B 1987', now()) ON CONFLICT ("Key") DO NOTHING;
                INSERT INTO seasons ("Id", "CompetitionId", "Label", "StartDateUtc", "EndDateUtc", "CreatedAtUtc")
                VALUES (md5('season:eurobasket-division-b:1987')::uuid, competition_id, '1987',
                        timestamptz '1987-01-01 00:00:00+00', timestamptz '1987-12-31 23:59:59+00', now())
                ON CONFLICT ("CompetitionId", "Label") DO NOTHING;
                SELECT "Id" INTO season_id FROM seasons WHERE "CompetitionId" = competition_id AND "Label" = '1987';
                SELECT "Id" INTO cycle_id FROM tournament_cycles WHERE "Key" = 'eurobasket-division-b-1987';
                IF cycle_id IS NULL THEN RAISE EXCEPTION 'eurobasket-division-b-1987 tournament cycle is missing'; END IF;

                INSERT INTO games
                    ("Id", "Source", "SourceGameId", "SourceUrl", "SourceSeasonKey", "SourceFetchedAtUtc",
                     "SourceRevision", "ParserVersion", "CompetitionId", "SeasonId", "TournamentCycleId",
                     "GameDateTimeUtc", "HomeTeamId", "AwayTeamId", "HomeScore", "AwayScore", "Status",
                     "CompetitionPhase", "CompetitionRound", "IsNeutralSite", "EloEligible", "EloExclusionReason",
                     "HasManualResultOverride", "IngestedAtUtc", "UpdatedAtUtc")
                SELECT md5('game:book:eurobasket-division-b-1987:' || v.slug)::uuid,
                       'Los campeonatos de Europa 1935-1995', 'eurobasket-division-b-1987-' || v.slug,
                       NULL, '1987', now(),
                       'Los campeonatos de Europa 1935-1995 — Carlos Jiménez',
                       'manual-book-photo-v1', competition_id, season_id, cycle_id,
                       timestamptz '1987-01-01 00:00:00+00', home."Id", away."Id", v.home_score, v.away_score,
                       'finished', v.phase, v.round_name, true, true, NULL, false, now(), now()
                FROM (VALUES
                    ('qual-a-eng-cyp', 'England', 'Cyprus', 95, 61, 'Qualification Round', 'Qualification Group A'),
                    ('qual-a-den-lux', 'Denmark', 'Luxembourg', 91, 56, 'Qualification Round', 'Qualification Group A'),
                    ('qual-a-aut-den', 'Austria', 'Denmark', 67, 46, 'Qualification Round', 'Qualification Group A'),
                    ('qual-a-lux-cyp', 'Luxembourg', 'Cyprus', 86, 81, 'Qualification Round', 'Qualification Group A'),
                    ('qual-a-eng-lux', 'England', 'Luxembourg', 96, 67, 'Qualification Round', 'Qualification Group A'),
                    ('qual-a-aut-cyp', 'Austria', 'Cyprus', 84, 49, 'Qualification Round', 'Qualification Group A'),
                    ('qual-a-den-cyp', 'Denmark', 'Cyprus', 99, 51, 'Qualification Round', 'Qualification Group A'),
                    ('qual-a-aut-eng', 'Austria', 'England', 64, 60, 'Qualification Round', 'Qualification Group A'),
                    ('qual-a-aut-lux', 'Austria', 'Luxembourg', 73, 60, 'Qualification Round', 'Qualification Group A'),
                    ('qual-a-eng-den', 'England', 'Denmark', 74, 65, 'Qualification Round', 'Qualification Group A'),

                    ('qual-b-isl-irl', 'Iceland', 'Ireland', 73, 72, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-por-sco', 'Portugal', 'Scotland', 67, 65, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-sco-isl', 'Scotland', 'Iceland', 91, 84, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-nor-por', 'Norway', 'Portugal', 83, 80, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-isl-sco', 'Iceland', 'Scotland', 73, 71, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-nor-irl', 'Norway', 'Ireland', 101, 77, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-isl-por', 'Iceland', 'Portugal', 81, 77, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-nor-sco', 'Norway', 'Scotland', 86, 83, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-isl-nor', 'Iceland', 'Norway', 75, 72, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-por-irl', 'Portugal', 'Ireland', 98, 75, 'Qualification Round', 'Qualification Group B'),

                    ('a-rom-fin', 'Romania', 'Finland', 85, 75, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-ned-bul', 'Netherlands', 'Bulgaria', 70, 63, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-bel-aut', 'Belgium', 'Austria', 80, 59, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-rom-ned', 'Romania', 'Netherlands', 80, 76, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-bul-aut', 'Bulgaria', 'Austria', 86, 67, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-fin-bel', 'Finland', 'Belgium', 66, 53, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-ned-bel', 'Netherlands', 'Belgium', 96, 74, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-bul-rom', 'Bulgaria', 'Romania', 76, 55, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-fin-aut', 'Finland', 'Austria', 83, 76, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-bul-bel', 'Bulgaria', 'Belgium', 84, 82, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-ned-fin', 'Netherlands', 'Finland', 72, 59, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-rom-aut', 'Romania', 'Austria', 74, 73, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-ned-aut', 'Netherlands', 'Austria', 86, 82, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-bul-fin', 'Bulgaria', 'Finland', 95, 75, 'Qualification Round', 'Challenge Round Group A'),

                    ('b-tur-hun', 'Turkey', 'Hungary', 73, 67, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-isr-swe', 'Israel', 'Sweden', 86, 70, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-pol-isl', 'Poland', 'Iceland', 95, 56, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-isr-hun', 'Israel', 'Hungary', 87, 58, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-swe-isl', 'Sweden', 'Iceland', 83, 65, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-pol-tur', 'Poland', 'Turkey', 80, 69, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-isr-pol', 'Israel', 'Poland', 98, 93, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-swe-hun', 'Sweden', 'Hungary', 74, 68, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-isl-tur', 'Iceland', 'Turkey', 63, 58, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-pol-swe', 'Poland', 'Sweden', 78, 60, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-isr-tur', 'Israel', 'Turkey', 81, 74, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-hun-isl', 'Hungary', 'Iceland', 75, 55, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-isr-isl', 'Israel', 'Iceland', 93, 64, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-tur-swe', 'Turkey', 'Sweden', 54, 52, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-pol-hun', 'Poland', 'Hungary', 74, 66, 'Qualification Round', 'Challenge Round Group B'),

                    ('final-bel-pol', 'Belgium', 'Poland', 82, 76, 'Final Phase', 'Final Phase'),
                    ('final-cze-bul', 'Czechoslovakia', 'Bulgaria', 77, 75, 'Final Phase', 'Final Phase'),
                    ('final-hun-swe', 'Hungary', 'Sweden', 80, 79, 'Final Phase', 'Final Phase'),
                    ('final-rom-fin', 'Romania', 'Finland', 76, 74, 'Final Phase', 'Final Phase'),
                    ('final-bul-bel', 'Bulgaria', 'Belgium', 77, 69, 'Final Phase', 'Final Phase'),
                    ('final-pol-rom', 'Poland', 'Romania', 83, 81, 'Final Phase', 'Final Phase'),
                    ('final-swe-fin', 'Sweden', 'Finland', 102, 81, 'Final Phase', 'Final Phase'),
                    ('final-cze-hun', 'Czechoslovakia', 'Hungary', 77, 75, 'Final Phase', 'Final Phase'),
                    ('final-rom-bul', 'Romania', 'Bulgaria', 83, 77, 'Final Phase', 'Final Phase'),
                    ('final-pol-swe', 'Poland', 'Sweden', 103, 102, 'Final Phase', 'Final Phase'),
                    ('final-cze-fin', 'Czechoslovakia', 'Finland', 109, 88, 'Final Phase', 'Final Phase'),
                    ('final-bel-hun', 'Belgium', 'Hungary', 92, 80, 'Final Phase', 'Final Phase'),
                    ('final-cze-pol', 'Czechoslovakia', 'Poland', 106, 91, 'Final Phase', 'Final Phase'),
                    ('final-bul-swe', 'Bulgaria', 'Sweden', 93, 68, 'Final Phase', 'Final Phase'),
                    ('final-rom-hun', 'Romania', 'Hungary', 73, 66, 'Final Phase', 'Final Phase'),
                    ('final-bel-fin', 'Belgium', 'Finland', 83, 70, 'Final Phase', 'Final Phase')
                ) AS v(slug, home_name, away_name, home_score, away_score, phase, round_name)
                JOIN teams home ON home."CanonicalName" = v.home_name
                JOIN teams away ON away."CanonicalName" = v.away_name
                ON CONFLICT ("Source", "SourceGameId") DO NOTHING;

                IF (SELECT count(*) FROM games WHERE "Source" = 'Los campeonatos de Europa 1935-1995'
                    AND "SourceGameId" LIKE 'eurobasket-division-b-1987-%') <> 65 THEN
                    RAISE EXCEPTION 'Expected sixty-five EuroBasket Division B 1987 book games';
                END IF;
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Historical book data is rolled back from the pre-deployment backup.
    }
}
