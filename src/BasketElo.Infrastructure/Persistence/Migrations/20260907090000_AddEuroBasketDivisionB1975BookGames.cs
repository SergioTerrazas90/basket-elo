using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260907090000_AddEuroBasketDivisionB1975BookGames")]
public partial class AddEuroBasketDivisionB1975BookGames : Migration
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
                    (md5('tournament-cycle:eurobasket-division-b:1975')::uuid,
                     'eurobasket-division-b-1975', 'EuroBasket Division B', '1975',
                     'EuroBasket Division B 1975', now())
                ON CONFLICT ("Key") DO NOTHING;

                INSERT INTO seasons
                    ("Id", "CompetitionId", "Label", "StartDateUtc", "EndDateUtc", "CreatedAtUtc")
                VALUES
                    (md5('season:eurobasket-division-b:1975')::uuid,
                     competition_id, '1975',
                     timestamptz '1975-01-01 00:00:00+00',
                     timestamptz '1975-12-31 23:59:59+00', now())
                ON CONFLICT ("CompetitionId", "Label") DO NOTHING;

                SELECT "Id" INTO season_id
                FROM seasons
                WHERE "CompetitionId" = competition_id AND "Label" = '1975';

                SELECT "Id" INTO cycle_id
                FROM tournament_cycles
                WHERE "Key" = 'eurobasket-division-b-1975';

                IF cycle_id IS NULL THEN
                    RAISE EXCEPTION 'eurobasket-division-b-1975 tournament cycle is missing';
                END IF;

                -- Dates are not printed consistently with every result on the
                -- supplied pages, so the deterministic season-start fallback
                -- is used for all rows. Scores are final scores; the Poland-
                -- France overtime result is retained in SourceRevision.
                INSERT INTO games
                    ("Id", "Source", "SourceGameId", "SourceUrl", "SourceSeasonKey",
                     "SourceFetchedAtUtc", "SourceRevision", "ParserVersion",
                     "CompetitionId", "SeasonId", "TournamentCycleId", "GameDateTimeUtc",
                     "HomeTeamId", "AwayTeamId", "HomeScore", "AwayScore", "Status",
                     "CompetitionPhase", "CompetitionRound", "IsNeutralSite", "EloEligible",
                     "EloExclusionReason", "HasManualResultOverride", "IngestedAtUtc", "UpdatedAtUtc")
                SELECT md5('game:book:eurobasket-division-b-1975:' || v.slug)::uuid,
                       'Los campeonatos de Europa 1935-1995',
                       'eurobasket-division-b-1975-' || v.slug,
                       NULL, '1975', now(),
                       CASE WHEN v.slug = 'final-pol-fra'
                            THEN 'Los campeonatos de Europa 1935-1995 — Carlos Jiménez (overtime; 95-95 after 40 minutes)'
                            ELSE 'Los campeonatos de Europa 1935-1995 — Carlos Jiménez'
                       END,
                       'manual-book-photo-v1', competition_id, season_id, cycle_id,
                       timestamptz '1975-01-01 00:00:00+00', home."Id", away."Id",
                       v.home_score, v.away_score, 'finished', v.phase, v.round_name,
                       true, true, NULL, false, now(), now()
                FROM (VALUES
                    ('a-sui-mar', 'Switzerland', 'Morocco', 96, 69, 'Qualification Round', 'Group A'),
                    ('a-ned-wal', 'Netherlands', 'Wales', 134, 59, 'Qualification Round', 'Group A'),
                    ('a-rom-frg', 'Romania', 'West Germany', 84, 69, 'Qualification Round', 'Group A'),
                    ('a-mar-wal', 'Morocco', 'Wales', 65, 52, 'Qualification Round', 'Group A'),
                    ('a-rom-sui', 'Romania', 'Switzerland', 85, 58, 'Qualification Round', 'Group A'),
                    ('a-ned-frg', 'Netherlands', 'West Germany', 80, 70, 'Qualification Round', 'Group A'),
                    ('a-rom-mar', 'Romania', 'Morocco', 106, 40, 'Qualification Round', 'Group A'),
                    ('a-ned-sui', 'Netherlands', 'Switzerland', 93, 48, 'Qualification Round', 'Group A'),
                    ('a-frg-wal', 'West Germany', 'Wales', 113, 47, 'Qualification Round', 'Group A'),
                    ('a-ned-mar', 'Netherlands', 'Morocco', 134, 54, 'Qualification Round', 'Group A'),
                    ('a-rom-wal', 'Romania', 'Wales', 164, 50, 'Qualification Round', 'Group A'),
                    ('a-frg-sui', 'West Germany', 'Switzerland', 94, 54, 'Qualification Round', 'Group A'),
                    ('a-rom-ned', 'Romania', 'Netherlands', 84, 61, 'Qualification Round', 'Group A'),
                    ('a-sui-wal', 'Switzerland', 'Wales', 96, 76, 'Qualification Round', 'Group A'),
                    ('a-frg-mar', 'West Germany', 'Morocco', 100, 50, 'Qualification Round', 'Group A'),

                    ('c-swe-lux', 'Sweden', 'Luxembourg', 83, 41, 'Qualification Round', 'Group C'),
                    ('c-pol-isl', 'Poland', 'Iceland', 123, 71, 'Qualification Round', 'Group C'),
                    ('c-grc-alb', 'Greece', 'Albania', 84, 57, 'Qualification Round', 'Group C'),
                    ('c-swe-isl', 'Sweden', 'Iceland', 109, 59, 'Qualification Round', 'Group C'),
                    ('c-grc-lux', 'Greece', 'Luxembourg', 83, 66, 'Qualification Round', 'Group C'),
                    ('c-pol-alb', 'Poland', 'Albania', 98, 88, 'Qualification Round', 'Group C'),
                    ('c-grc-isl', 'Greece', 'Iceland', 108, 78, 'Qualification Round', 'Group C'),
                    ('c-alb-lux', 'Albania', 'Luxembourg', 95, 71, 'Qualification Round', 'Group C'),
                    ('c-pol-swe', 'Poland', 'Sweden', 77, 75, 'Qualification Round', 'Group C'),
                    ('c-alb-isl', 'Albania', 'Iceland', 112, 77, 'Qualification Round', 'Group C'),
                    ('c-pol-lux', 'Poland', 'Luxembourg', 80, 56, 'Qualification Round', 'Group C'),
                    ('c-grc-swe', 'Greece', 'Sweden', 77, 73, 'Qualification Round', 'Group C'),
                    ('c-isl-lux', 'Iceland', 'Luxembourg', 73, 67, 'Qualification Round', 'Group C'),
                    ('c-alb-swe', 'Albania', 'Sweden', 82, 71, 'Qualification Round', 'Group C'),
                    ('c-grc-pol', 'Greece', 'Poland', 88, 82, 'Qualification Round', 'Group C'),

                    ('f-fra-sco', 'France', 'Scotland', 88, 63, 'Qualification Round', 'Group F'),
                    ('f-aut-hun', 'Austria', 'Hungary', 76, 68, 'Qualification Round', 'Group F'),
                    ('f-aut-alg', 'Austria', 'Algeria', 119, 63, 'Qualification Round', 'Group F'),
                    ('f-hun-fra', 'Hungary', 'France', 66, 52, 'Qualification Round', 'Group F'),
                    ('f-hun-sco', 'Hungary', 'Scotland', 109, 72, 'Qualification Round', 'Group F'),
                    ('f-fra-alg', 'France', 'Algeria', 117, 61, 'Qualification Round', 'Group F'),
                    ('f-fra-aut', 'France', 'Austria', 92, 76, 'Qualification Round', 'Group F'),
                    ('f-alg-sco', 'Algeria', 'Scotland', 74, 73, 'Qualification Round', 'Group F'),
                    ('f-hun-alg', 'Hungary', 'Algeria', 130, 65, 'Qualification Round', 'Group F'),
                    ('f-aut-sco', 'Austria', 'Scotland', 76, 73, 'Qualification Round', 'Group F'),

                    ('final-pol-fra', 'Poland', 'France', 106, 100, 'Final Phase', 'Final Phase'),
                    ('final-grc-rom', 'Greece', 'Romania', 81, 68, 'Final Phase', 'Final Phase'),
                    ('final-hun-ned', 'Hungary', 'Netherlands', 82, 63, 'Final Phase', 'Final Phase'),
                    ('final-rom-pol', 'Romania', 'Poland', 88, 71, 'Final Phase', 'Final Phase'),
                    ('final-grc-hun', 'Greece', 'Hungary', 67, 65, 'Final Phase', 'Final Phase'),
                    ('final-ned-fra', 'Netherlands', 'France', 90, 87, 'Final Phase', 'Final Phase'),
                    ('final-rom-hun', 'Romania', 'Hungary', 90, 82, 'Final Phase', 'Final Phase'),
                    ('final-grc-fra', 'Greece', 'France', 80, 77, 'Final Phase', 'Final Phase'),
                    ('final-pol-ned', 'Poland', 'Netherlands', 95, 93, 'Final Phase', 'Final Phase'),
                    ('final-rom-fra', 'Romania', 'France', 91, 83, 'Final Phase', 'Final Phase'),
                    ('final-pol-hun', 'Poland', 'Hungary', 114, 89, 'Final Phase', 'Final Phase'),
                    ('final-ned-grc', 'Netherlands', 'Greece', 83, 68, 'Final Phase', 'Final Phase')
                ) AS v(slug, home_name, away_name, home_score, away_score, phase, round_name)
                JOIN teams home ON home."CanonicalName" = v.home_name
                JOIN teams away ON away."CanonicalName" = v.away_name
                ON CONFLICT ("Source", "SourceGameId") DO NOTHING;

                IF (SELECT count(*) FROM games
                    WHERE "Source" = 'Los campeonatos de Europa 1935-1995'
                      AND "SourceGameId" LIKE 'eurobasket-division-b-1975-%') <> 52 THEN
                    RAISE EXCEPTION 'Expected fifty-two EuroBasket Division B 1975 book games';
                END IF;
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // This audited historical data addition is rolled back from the
        // pre-deployment database backup, like the preceding book migrations.
    }
}
