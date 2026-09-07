using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260907170000_AddEuroBasketDivisionB1983BookGames")]
public partial class AddEuroBasketDivisionB1983BookGames : Migration
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
                VALUES (md5('tournament-cycle:eurobasket-division-b:1983')::uuid, 'eurobasket-division-b-1983',
                        'EuroBasket Division B', '1983', 'EuroBasket Division B 1983', now()) ON CONFLICT ("Key") DO NOTHING;
                INSERT INTO seasons ("Id", "CompetitionId", "Label", "StartDateUtc", "EndDateUtc", "CreatedAtUtc")
                VALUES (md5('season:eurobasket-division-b:1983')::uuid, competition_id, '1983',
                        timestamptz '1983-01-01 00:00:00+00', timestamptz '1983-12-31 23:59:59+00', now())
                ON CONFLICT ("CompetitionId", "Label") DO NOTHING;
                SELECT "Id" INTO season_id FROM seasons WHERE "CompetitionId" = competition_id AND "Label" = '1983';
                SELECT "Id" INTO cycle_id FROM tournament_cycles WHERE "Key" = 'eurobasket-division-b-1983';
                IF cycle_id IS NULL THEN RAISE EXCEPTION 'eurobasket-division-b-1983 tournament cycle is missing'; END IF;

                INSERT INTO games
                    ("Id", "Source", "SourceGameId", "SourceUrl", "SourceSeasonKey", "SourceFetchedAtUtc",
                     "SourceRevision", "ParserVersion", "CompetitionId", "SeasonId", "TournamentCycleId",
                     "GameDateTimeUtc", "HomeTeamId", "AwayTeamId", "HomeScore", "AwayScore", "Status",
                     "CompetitionPhase", "CompetitionRound", "IsNeutralSite", "EloEligible", "EloExclusionReason",
                     "HasManualResultOverride", "IngestedAtUtc", "UpdatedAtUtc")
                SELECT md5('game:book:eurobasket-division-b-1983:' || v.slug)::uuid,
                       'Los campeonatos de Europa 1935-1995', 'eurobasket-division-b-1983-' || v.slug,
                       NULL, '1983', now(),
                       CASE WHEN v.slug IN ('qual-a-por-isl', 'qual-b-isl-irl', 'a-swe-frg', 'b-rom-bul', 'final-rom-fin')
                            THEN 'Los campeonatos de Europa 1935-1995 — Carlos Jiménez (overtime noted in source)'
                            ELSE 'Los campeonatos de Europa 1935-1995 — Carlos Jiménez' END,
                       'manual-book-photo-v1', competition_id, season_id, cycle_id,
                       timestamptz '1983-01-01 00:00:00+00', home."Id", away."Id", v.home_score, v.away_score,
                       'finished', v.phase, v.round_name, true, true, NULL, false, now(), now()
                FROM (VALUES
                    ('qual-a-alg-syr', 'Algeria', 'Syria', 69, 64, 'Qualification Round', 'Qualification Group A'),
                    ('qual-a-bul-cyp', 'Bulgaria', 'Cyprus', 90, 45, 'Qualification Round', 'Qualification Group A'),
                    ('qual-a-alg-cyp', 'Algeria', 'Cyprus', 78, 65, 'Qualification Round', 'Qualification Group A'),
                    ('qual-a-sui-syr', 'Switzerland', 'Syria', 86, 78, 'Qualification Round', 'Qualification Group A'),
                    ('qual-a-sui-cyp', 'Switzerland', 'Cyprus', 91, 69, 'Qualification Round', 'Qualification Group A'),
                    ('qual-a-bul-alg', 'Bulgaria', 'Algeria', 88, 42, 'Qualification Round', 'Qualification Group A'),
                    ('qual-a-sui-alg', 'Switzerland', 'Algeria', 92, 79, 'Qualification Round', 'Qualification Group A'),
                    ('qual-a-bul-syr', 'Bulgaria', 'Syria', 83, 71, 'Qualification Round', 'Qualification Group A'),
                    ('qual-a-bul-sui', 'Bulgaria', 'Switzerland', 96, 70, 'Qualification Round', 'Qualification Group A'),
                    ('qual-a-syr-cyp', 'Syria', 'Cyprus', 89, 77, 'Qualification Round', 'Qualification Group A'),

                    ('qual-b-aut-isl', 'Austria', 'Iceland', 91, 77, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-hun-egy', 'Hungary', 'Egypt', 97, 86, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-sco-irl', 'Scotland', 'Ireland', 54, 43, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-hun-isl', 'Hungary', 'Iceland', 114, 90, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-aut-irl', 'Austria', 'Ireland', 69, 58, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-sco-egy', 'Scotland', 'Egypt', 86, 74, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-egy-irl', 'Egypt', 'Ireland', 68, 64, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-hun-aut', 'Hungary', 'Austria', 105, 78, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-sco-isl', 'Scotland', 'Iceland', 77, 64, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-isl-irl', 'Iceland', 'Ireland', 74, 68, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-sco-hun', 'Scotland', 'Hungary', 100, 75, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-egy-aut', 'Egypt', 'Austria', 84, 82, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-aut-sco', 'Austria', 'Scotland', 75, 64, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-isl-egy', 'Iceland', 'Egypt', 73, 72, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-hun-irl', 'Hungary', 'Ireland', 61, 47, 'Qualification Round', 'Qualification Group B'),

                    ('a-frg-fin', 'West Germany', 'Finland', 65, 64, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-hun-eng', 'Hungary', 'England', 89, 84, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-swe-por', 'Sweden', 'Portugal', 88, 82, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-swe-hun', 'Sweden', 'Hungary', 81, 80, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-fin-eng', 'Finland', 'England', 73, 72, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-frg-por', 'West Germany', 'Portugal', 92, 80, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-hun-fin', 'Hungary', 'Finland', 90, 81, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-swe-frg', 'Sweden', 'West Germany', 88, 86, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-eng-por', 'England', 'Portugal', 107, 90, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-swe-eng', 'Sweden', 'England', 82, 73, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-frg-hun', 'West Germany', 'Hungary', 93, 70, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-fin-por', 'Finland', 'Portugal', 81, 77, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-frg-eng', 'West Germany', 'England', 96, 90, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-swe-fin', 'Sweden', 'Finland', 85, 77, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-hun-por', 'Hungary', 'Portugal', 92, 70, 'Qualification Round', 'Challenge Round Group A'),

                    ('b-bel-grc', 'Belgium', 'Greece', 90, 81, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-ned-rom', 'Netherlands', 'Romania', 76, 74, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-bel-tur', 'Belgium', 'Turkey', 76, 73, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-bel-bul', 'Belgium', 'Bulgaria', 70, 65, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-grc-rom', 'Greece', 'Romania', 73, 62, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-ned-tur', 'Netherlands', 'Turkey', 88, 58, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-rom-tur', 'Romania', 'Turkey', 75, 70, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-grc-bel', 'Greece', 'Belgium', 97, 72, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-ned-bul', 'Netherlands', 'Bulgaria', 78, 64, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-rom-bel', 'Romania', 'Belgium', 83, 66, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-tur-bul', 'Turkey', 'Bulgaria', 53, 47, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-ned-grc', 'Netherlands', 'Greece', 71, 67, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-ned-bel', 'Netherlands', 'Belgium', 82, 59, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-rom-bul', 'Romania', 'Bulgaria', 100, 96, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-grc-tur', 'Greece', 'Turkey', 75, 57, 'Qualification Round', 'Challenge Round Group B'),

                    ('final-grc-fin', 'Greece', 'Finland', 96, 93, 'Final Phase', 'Final Phase'),
                    ('final-frg-tur', 'West Germany', 'Turkey', 94, 83, 'Final Phase', 'Final Phase'),
                    ('final-rom-swe', 'Romania', 'Sweden', 96, 88, 'Final Phase', 'Final Phase'),
                    ('final-ned-hun', 'Netherlands', 'Hungary', 78, 73, 'Final Phase', 'Final Phase'),
                    ('final-frg-rom', 'West Germany', 'Romania', 88, 81, 'Final Phase', 'Final Phase'),
                    ('final-swe-tur', 'Sweden', 'Turkey', 102, 84, 'Final Phase', 'Final Phase'),
                    ('final-ned-fin', 'Netherlands', 'Finland', 93, 82, 'Final Phase', 'Final Phase'),
                    ('final-grc-hun', 'Greece', 'Hungary', 89, 82, 'Final Phase', 'Final Phase'),
                    ('final-tur-hun', 'Turkey', 'Hungary', 79, 76, 'Final Phase', 'Final Phase'),
                    ('final-rom-fin', 'Romania', 'Finland', 103, 102, 'Final Phase', 'Final Phase'),
                    ('final-grc-swe', 'Greece', 'Sweden', 79, 68, 'Final Phase', 'Final Phase'),
                    ('final-ned-frg', 'Netherlands', 'West Germany', 92, 70, 'Final Phase', 'Final Phase'),
                    ('final-tur-fin', 'Turkey', 'Finland', 106, 93, 'Final Phase', 'Final Phase'),
                    ('final-hun-rom', 'Hungary', 'Romania', 92, 78, 'Final Phase', 'Final Phase'),
                    ('final-grc-frg', 'Greece', 'West Germany', 95, 87, 'Final Phase', 'Final Phase'),
                    ('final-ned-swe', 'Netherlands', 'Sweden', 94, 85, 'Final Phase', 'Final Phase')
                ) AS v(slug, home_name, away_name, home_score, away_score, phase, round_name)
                JOIN teams home ON home."CanonicalName" = v.home_name
                JOIN teams away ON away."CanonicalName" = v.away_name
                ON CONFLICT ("Source", "SourceGameId") DO NOTHING;

                IF (SELECT count(*) FROM games WHERE "Source" = 'Los campeonatos de Europa 1935-1995'
                    AND "SourceGameId" LIKE 'eurobasket-division-b-1983-%') <> 71 THEN
                    RAISE EXCEPTION 'Expected seventy-one EuroBasket Division B 1983 book games';
                END IF;
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Historical book data is rolled back from the pre-deployment backup.
    }
}
