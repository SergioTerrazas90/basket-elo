using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260907130000_AddEuroBasketDivisionB1979BookGames")]
public partial class AddEuroBasketDivisionB1979BookGames : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            DECLARE
                competition_id uuid;
                season_id uuid;
                cycle_id uuid;
            BEGIN
                SELECT "Id" INTO competition_id
                FROM competitions
                WHERE "Name" = 'FIBA EuroBasket Division B'
                  AND "CountryCode" IS NULL;

                IF competition_id IS NULL THEN
                    RAISE EXCEPTION 'FIBA EuroBasket Division B competition is missing';
                END IF;

                INSERT INTO tournament_cycles
                    ("Id", "Key", "Family", "EditionLabel", "DisplayName", "CreatedAtUtc")
                VALUES
                    (md5('tournament-cycle:eurobasket-division-b:1979')::uuid,
                     'eurobasket-division-b-1979', 'EuroBasket Division B', '1979',
                     'EuroBasket Division B 1979', now())
                ON CONFLICT ("Key") DO NOTHING;

                INSERT INTO seasons
                    ("Id", "CompetitionId", "Label", "StartDateUtc", "EndDateUtc", "CreatedAtUtc")
                VALUES
                    (md5('season:eurobasket-division-b:1979')::uuid,
                     competition_id, '1979',
                     timestamptz '1979-01-01 00:00:00+00',
                     timestamptz '1979-12-31 23:59:59+00', now())
                ON CONFLICT ("CompetitionId", "Label") DO NOTHING;

                SELECT "Id" INTO season_id
                FROM seasons
                WHERE "CompetitionId" = competition_id AND "Label" = '1979';

                SELECT "Id" INTO cycle_id
                FROM tournament_cycles
                WHERE "Key" = 'eurobasket-division-b-1979';

                IF cycle_id IS NULL THEN
                    RAISE EXCEPTION 'eurobasket-division-b-1979 tournament cycle is missing';
                END IF;

                -- Dates are not printed in the supplied pages. Overtime notes are
                -- retained in SourceRevision while scores are recorded as final.
                INSERT INTO games
                    ("Id", "Source", "SourceGameId", "SourceUrl", "SourceSeasonKey",
                     "SourceFetchedAtUtc", "SourceRevision", "ParserVersion",
                     "CompetitionId", "SeasonId", "TournamentCycleId", "GameDateTimeUtc",
                     "HomeTeamId", "AwayTeamId", "HomeScore", "AwayScore", "Status",
                     "CompetitionPhase", "CompetitionRound", "IsNeutralSite", "EloEligible",
                     "EloExclusionReason", "HasManualResultOverride", "IngestedAtUtc", "UpdatedAtUtc")
                SELECT md5('game:book:eurobasket-division-b-1979:' || v.slug)::uuid,
                       'Los campeonatos de Europa 1935-1995',
                       'eurobasket-division-b-1979-' || v.slug,
                       NULL, '1979', now(),
                       CASE WHEN v.slug IN ('a-hun-aut', 'a-hun-tur', 'b-fra-rom')
                            THEN 'Los campeonatos de Europa 1935-1995 — Carlos Jiménez (overtime noted in source)'
                            ELSE 'Los campeonatos de Europa 1935-1995 — Carlos Jiménez'
                       END,
                       'manual-book-photo-v1', competition_id, season_id, cycle_id,
                       timestamptz '1979-01-01 00:00:00+00', home."Id", away."Id",
                       v.home_score, v.away_score, 'finished', v.phase, v.round_name,
                       true, true, NULL, false, now(), now()
                FROM (VALUES
                    ('qual-a-sui-nor', 'Switzerland', 'Norway', 91, 64, 'Qualification Round', 'Qualification Group A'),
                    ('qual-a-bel-lux', 'Belgium', 'Luxembourg', 98, 92, 'Qualification Round', 'Qualification Group A'),
                    ('qual-a-sco-sui', 'Scotland', 'Switzerland', 94, 70, 'Qualification Round', 'Qualification Group A'),
                    ('qual-a-lux-nor', 'Luxembourg', 'Norway', 73, 68, 'Qualification Round', 'Qualification Group A'),
                    ('qual-a-sco-nor', 'Scotland', 'Norway', 80, 67, 'Qualification Round', 'Qualification Group A'),
                    ('qual-a-sui-lux', 'Switzerland', 'Luxembourg', 80, 75, 'Qualification Round', 'Qualification Group A'),

                    ('qual-b-eng-por', 'England', 'Portugal', 74, 60, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-tur-den', 'Turkey', 'Denmark', 84, 58, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-eng-alg', 'England', 'Algeria', 108, 73, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-tur-por', 'Turkey', 'Portugal', 90, 61, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-por-den', 'Portugal', 'Denmark', 71, 69, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-tur-alg', 'Turkey', 'Algeria', 111, 41, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-por-alg', 'Portugal', 'Algeria', 87, 55, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-eng-den', 'England', 'Denmark', 98, 54, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-den-alg', 'Denmark', 'Algeria', 91, 86, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-tur-eng', 'Turkey', 'England', 75, 63, 'Qualification Round', 'Qualification Group B'),

                    ('a-swe-fin', 'Sweden', 'Finland', 60, 58, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-hun-grc', 'Hungary', 'Greece', 62, 61, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-tur-aut', 'Turkey', 'Austria', 68, 61, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-fin-hun', 'Finland', 'Hungary', 72, 70, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-swe-tur', 'Sweden', 'Turkey', 73, 60, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-grc-aut', 'Greece', 'Austria', 82, 67, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-hun-aut', 'Hungary', 'Austria', 84, 82, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-fin-tur', 'Finland', 'Turkey', 71, 66, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-grc-swe', 'Greece', 'Sweden', 72, 70, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-grc-tur', 'Greece', 'Turkey', 87, 77, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-swe-hun', 'Sweden', 'Hungary', 73, 65, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-fin-aut', 'Finland', 'Austria', 84, 69, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-swe-aut', 'Sweden', 'Austria', 67, 54, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-hun-tur', 'Hungary', 'Turkey', 87, 81, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-grc-fin', 'Greece', 'Finland', 92, 62, 'Qualification Round', 'Challenge Round Group A'),

                    ('b-rom-sco', 'Romania', 'Scotland', 102, 67, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-esp-frg', 'Spain', 'West Germany', 103, 88, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-fra-pol', 'France', 'Poland', 75, 69, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-esp-sco', 'Spain', 'Scotland', 113, 68, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-fra-frg', 'France', 'West Germany', 79, 62, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-pol-rom', 'Poland', 'Romania', 71, 68, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-frg-sco', 'West Germany', 'Scotland', 104, 55, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-fra-rom', 'France', 'Romania', 79, 78, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-esp-pol', 'Spain', 'Poland', 93, 81, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-pol-frg', 'Poland', 'West Germany', 84, 80, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-esp-rom', 'Spain', 'Romania', 85, 83, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-fra-sco', 'France', 'Scotland', 99, 68, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-pol-sco', 'Poland', 'Scotland', 84, 58, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-frg-rom', 'West Germany', 'Romania', 93, 89, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-fra-esp', 'France', 'Spain', 93, 70, 'Qualification Round', 'Challenge Round Group B'),

                    ('final-grc-pol', 'Greece', 'Poland', 90, 83, 'Final Phase', 'Final Phase'),
                    ('final-fra-fin', 'France', 'Finland', 106, 86, 'Final Phase', 'Final Phase'),
                    ('final-esp-swe', 'Spain', 'Sweden', 79, 74, 'Final Phase', 'Final Phase'),
                    ('final-pol-fin', 'Poland', 'Finland', 83, 75, 'Final Phase', 'Final Phase'),
                    ('final-fra-swe', 'France', 'Sweden', 59, 58, 'Final Phase', 'Final Phase'),
                    ('final-esp-grc', 'Spain', 'Greece', 89, 88, 'Final Phase', 'Final Phase'),
                    ('final-esp-fin', 'Spain', 'Finland', 99, 85, 'Final Phase', 'Final Phase'),
                    ('final-pol-swe', 'Poland', 'Sweden', 81, 73, 'Final Phase', 'Final Phase'),
                    ('final-fra-grc', 'France', 'Greece', 81, 80, 'Final Phase', 'Final Phase')
                ) AS v(slug, home_name, away_name, home_score, away_score, phase, round_name)
                JOIN teams home ON home."CanonicalName" = v.home_name
                JOIN teams away ON away."CanonicalName" = v.away_name
                ON CONFLICT ("Source", "SourceGameId") DO NOTHING;

                IF (SELECT count(*) FROM games
                    WHERE "Source" = 'Los campeonatos de Europa 1935-1995'
                      AND "SourceGameId" LIKE 'eurobasket-division-b-1979-%') <> 55 THEN
                    RAISE EXCEPTION 'Expected fifty-five EuroBasket Division B 1979 book games';
                END IF;
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Historical book data is rolled back from the pre-deployment backup.
    }
}
