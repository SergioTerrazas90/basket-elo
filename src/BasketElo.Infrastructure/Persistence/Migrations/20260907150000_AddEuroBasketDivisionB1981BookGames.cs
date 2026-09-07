using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260907150000_AddEuroBasketDivisionB1981BookGames")]
public partial class AddEuroBasketDivisionB1981BookGames : Migration
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
                VALUES (md5('tournament-cycle:eurobasket-division-b:1981')::uuid,
                        'eurobasket-division-b-1981', 'EuroBasket Division B', '1981',
                        'EuroBasket Division B 1981', now()) ON CONFLICT ("Key") DO NOTHING;
                INSERT INTO seasons ("Id", "CompetitionId", "Label", "StartDateUtc", "EndDateUtc", "CreatedAtUtc")
                VALUES (md5('season:eurobasket-division-b:1981')::uuid, competition_id, '1981',
                        timestamptz '1981-01-01 00:00:00+00', timestamptz '1981-12-31 23:59:59+00', now())
                ON CONFLICT ("CompetitionId", "Label") DO NOTHING;
                SELECT "Id" INTO season_id FROM seasons WHERE "CompetitionId" = competition_id AND "Label" = '1981';
                SELECT "Id" INTO cycle_id FROM tournament_cycles WHERE "Key" = 'eurobasket-division-b-1981';
                IF cycle_id IS NULL THEN RAISE EXCEPTION 'eurobasket-division-b-1981 tournament cycle is missing'; END IF;

                INSERT INTO games
                    ("Id", "Source", "SourceGameId", "SourceUrl", "SourceSeasonKey", "SourceFetchedAtUtc",
                     "SourceRevision", "ParserVersion", "CompetitionId", "SeasonId", "TournamentCycleId",
                     "GameDateTimeUtc", "HomeTeamId", "AwayTeamId", "HomeScore", "AwayScore", "Status",
                     "CompetitionPhase", "CompetitionRound", "IsNeutralSite", "EloEligible", "EloExclusionReason",
                     "HasManualResultOverride", "IngestedAtUtc", "UpdatedAtUtc")
                SELECT md5('game:book:eurobasket-division-b-1981:' || v.slug)::uuid,
                       'Los campeonatos de Europa 1935-1995', 'eurobasket-division-b-1981-' || v.slug,
                       NULL, '1981', now(),
                       CASE WHEN v.slug IN ('qual-a-por-isl', 'a-swe-rom', 'b-tur-fin', 'b-fin-hun')
                            THEN 'Los campeonatos de Europa 1935-1995 — Carlos Jiménez (overtime noted in source)'
                            ELSE 'Los campeonatos de Europa 1935-1995 — Carlos Jiménez' END,
                       'manual-book-photo-v1', competition_id, season_id, cycle_id,
                       timestamptz '1981-01-01 00:00:00+00', home."Id", away."Id", v.home_score, v.away_score,
                       'finished', v.phase, v.round_name, true, true, NULL, false, now(), now()
                FROM (VALUES
                    ('qual-a-isl-sco', 'Iceland', 'Scotland', 82, 69, 'Qualification Round', 'Qualification Group A'),
                    ('qual-a-sui-alg', 'Switzerland', 'Algeria', 101, 72, 'Qualification Round', 'Qualification Group A'),
                    ('qual-a-alg-sco', 'Algeria', 'Scotland', 80, 79, 'Qualification Round', 'Qualification Group A'),
                    ('qual-a-por-isl', 'Portugal', 'Iceland', 94, 91, 'Qualification Round', 'Qualification Group A'),
                    ('qual-a-por-sui', 'Portugal', 'Switzerland', 91, 80, 'Qualification Round', 'Qualification Group A'),
                    ('qual-a-isl-alg', 'Iceland', 'Algeria', 72, 70, 'Qualification Round', 'Qualification Group A'),
                    ('qual-a-por-sco', 'Portugal', 'Scotland', 81, 71, 'Qualification Round', 'Qualification Group A'),
                    ('qual-a-isl-sui', 'Iceland', 'Switzerland', 90, 83, 'Qualification Round', 'Qualification Group A'),
                    ('qual-a-por-alg', 'Portugal', 'Algeria', 73, 57, 'Qualification Round', 'Qualification Group A'),
                    ('qual-a-sui-sco', 'Switzerland', 'Scotland', 96, 94, 'Qualification Round', 'Qualification Group A'),

                    ('qual-b-den-nor', 'Denmark', 'Norway', 82, 65, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-aut-irl', 'Austria', 'Ireland', 91, 40, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-aut-nor', 'Austria', 'Norway', 80, 55, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-eng-irl', 'England', 'Ireland', 73, 60, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-nor-irl', 'Norway', 'Ireland', 60, 55, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-eng-den', 'England', 'Denmark', 86, 78, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-aut-den', 'Austria', 'Denmark', 96, 78, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-eng-nor', 'England', 'Norway', 88, 45, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-den-irl', 'Denmark', 'Ireland', 80, 56, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-eng-aut', 'England', 'Austria', 71, 57, 'Qualification Round', 'Qualification Group B'),

                    ('a-swe-por', 'Sweden', 'Portugal', 70, 56, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-ned-bul', 'Netherlands', 'Bulgaria', 86, 65, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-rom-frg', 'Romania', 'West Germany', 95, 84, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-ned-por', 'Netherlands', 'Portugal', 79, 65, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-frg-bul', 'West Germany', 'Bulgaria', 78, 71, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-swe-rom', 'Sweden', 'Romania', 90, 85, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-frg-ned', 'West Germany', 'Netherlands', 81, 79, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-rom-por', 'Romania', 'Portugal', 91, 75, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-bul-swe', 'Bulgaria', 'Sweden', 80, 70, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-por-bul', 'Portugal', 'Bulgaria', 78, 73, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-frg-swe', 'West Germany', 'Sweden', 75, 49, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-ned-rom', 'Netherlands', 'Romania', 87, 84, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-rom-bul', 'Romania', 'Bulgaria', 112, 87, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-frg-por', 'West Germany', 'Portugal', 90, 55, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-swe-ned', 'Sweden', 'Netherlands', 73, 72, 'Qualification Round', 'Challenge Round Group A'),

                    ('b-hun-eng', 'Hungary', 'England', 84, 83, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-grc-fin', 'Greece', 'Finland', 101, 86, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-tur-bel', 'Turkey', 'Belgium', 71, 70, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-fin-bel', 'Finland', 'Belgium', 88, 76, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-grc-hun', 'Greece', 'Hungary', 87, 84, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-eng-tur', 'England', 'Turkey', 79, 75, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-bel-hun', 'Belgium', 'Hungary', 95, 92, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-grc-eng', 'Greece', 'England', 80, 74, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-tur-fin', 'Turkey', 'Finland', 90, 81, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-eng-fin', 'England', 'Finland', 91, 80, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-grc-bel', 'Greece', 'Belgium', 90, 78, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-tur-hun', 'Turkey', 'Hungary', 78, 77, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-eng-bel', 'England', 'Belgium', 103, 87, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-fin-hun', 'Finland', 'Hungary', 84, 82, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-grc-tur', 'Greece', 'Turkey', 85, 84, 'Qualification Round', 'Challenge Round Group B'),

                    ('final-eng-swe', 'England', 'Sweden', 84, 69, 'Final Phase', 'Final Phase'),
                    ('final-frg-tur', 'West Germany', 'Turkey', 65, 64, 'Final Phase', 'Final Phase'),
                    ('final-grc-ned', 'Greece', 'Netherlands', 105, 94, 'Final Phase', 'Final Phase'),
                    ('final-frg-eng', 'West Germany', 'England', 84, 66, 'Final Phase', 'Final Phase'),
                    ('final-grc-swe', 'Greece', 'Sweden', 84, 79, 'Final Phase', 'Final Phase'),
                    ('final-tur-ned', 'Turkey', 'Netherlands', 82, 78, 'Final Phase', 'Final Phase'),
                    ('final-ned-eng', 'Netherlands', 'England', 96, 82, 'Final Phase', 'Final Phase'),
                    ('final-grc-frg', 'Greece', 'West Germany', 81, 79, 'Final Phase', 'Final Phase'),
                    ('final-tur-swe', 'Turkey', 'Sweden', 69, 55, 'Final Phase', 'Final Phase')
                ) AS v(slug, home_name, away_name, home_score, away_score, phase, round_name)
                JOIN teams home ON home."CanonicalName" = v.home_name
                JOIN teams away ON away."CanonicalName" = v.away_name
                ON CONFLICT ("Source", "SourceGameId") DO NOTHING;

                IF (SELECT count(*) FROM games WHERE "Source" = 'Los campeonatos de Europa 1935-1995'
                    AND "SourceGameId" LIKE 'eurobasket-division-b-1981-%') <> 59 THEN
                    RAISE EXCEPTION 'Expected fifty-nine EuroBasket Division B 1981 book games';
                END IF;
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Historical book data is rolled back from the pre-deployment backup.
    }
}
