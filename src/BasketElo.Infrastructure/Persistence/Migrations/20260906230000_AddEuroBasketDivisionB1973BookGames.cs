using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260906230000_AddEuroBasketDivisionB1973BookGames")]
public partial class AddEuroBasketDivisionB1973BookGames : Migration
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
                    (md5('tournament-cycle:eurobasket-division-b:1973')::uuid,
                     'eurobasket-division-b-1973', 'EuroBasket Division B', '1973',
                     'EuroBasket Division B 1973', now())
                ON CONFLICT ("Key") DO NOTHING;

                INSERT INTO seasons
                    ("Id", "CompetitionId", "Label", "StartDateUtc", "EndDateUtc", "CreatedAtUtc")
                VALUES
                    (md5('season:eurobasket-division-b:1973')::uuid,
                     competition_id, '1973',
                     timestamptz '1973-01-01 00:00:00+00',
                     timestamptz '1973-12-31 23:59:59+00', now())
                ON CONFLICT ("CompetitionId", "Label") DO NOTHING;

                SELECT "Id" INTO season_id
                FROM seasons
                WHERE "CompetitionId" = competition_id AND "Label" = '1973';

                SELECT "Id" INTO cycle_id
                FROM tournament_cycles
                WHERE "Key" = 'eurobasket-division-b-1973';

                IF cycle_id IS NULL THEN
                    RAISE EXCEPTION 'eurobasket-division-b-1973 tournament cycle is missing';
                END IF;

                -- No match dates are printed in the supplied book page; the
                -- deterministic season-start fallback is therefore used.
                INSERT INTO games
                    ("Id", "Source", "SourceGameId", "SourceUrl", "SourceSeasonKey",
                     "SourceFetchedAtUtc", "SourceRevision", "ParserVersion",
                     "CompetitionId", "SeasonId", "TournamentCycleId", "GameDateTimeUtc",
                     "HomeTeamId", "AwayTeamId", "HomeScore", "AwayScore", "Status",
                     "CompetitionPhase", "CompetitionRound", "IsNeutralSite", "EloEligible",
                     "EloExclusionReason", "HasManualResultOverride", "IngestedAtUtc", "UpdatedAtUtc")
                SELECT md5('game:book:eurobasket-division-b-1973:' || v.slug)::uuid,
                       'Los campeonatos de Europa 1935-1995',
                       'eurobasket-division-b-1973-' || v.slug,
                       NULL, '1973', now(),
                       'Los campeonatos de Europa 1935-1995 — Carlos Jiménez',
                       'manual-book-photo-v1', competition_id, season_id, cycle_id,
                       timestamptz '1973-01-01 00:00:00+00', home."Id", away."Id",
                       v.home_score, v.away_score, 'finished', v.phase, v.round_name,
                       true, true, NULL, false, now(), now()
                FROM (VALUES
                    ('gda-den', 'East Germany', 'Denmark', 91, 63, 'Qualification Round', 'Group A'),
                    ('isr-fra', 'Israel', 'France', 75, 71, 'Qualification Round', 'Group A'),
                    ('aut-sco', 'Austria', 'Scotland', 101, 69, 'Qualification Round', 'Group A'),
                    ('sco-ned', 'Scotland', 'Netherlands', 95, 66, 'Qualification Round', 'Group A'),
                    ('isr-gda', 'Israel', 'East Germany', 87, 80, 'Qualification Round', 'Group A'),
                    ('aut-den', 'Austria', 'Denmark', 86, 59, 'Qualification Round', 'Group A'),
                    ('ned-den', 'Netherlands', 'Denmark', 73, 62, 'Qualification Round', 'Group A'),
                    ('fra-gda', 'France', 'East Germany', 65, 53, 'Qualification Round', 'Group A'),
                    ('isr-aut', 'Israel', 'Austria', 89, 78, 'Qualification Round', 'Group A'),
                    ('fra-sco', 'France', 'Scotland', 82, 66, 'Qualification Round', 'Group A'),
                    ('isr-den', 'Israel', 'Denmark', 85, 62, 'Qualification Round', 'Group A'),
                    ('gda-ned', 'East Germany', 'Netherlands', 68, 58, 'Qualification Round', 'Group A'),
                    ('isr-sco', 'Israel', 'Scotland', 126, 77, 'Qualification Round', 'Group A'),
                    ('fra-ned', 'France', 'Netherlands', 60, 52, 'Qualification Round', 'Group A'),
                    ('gda-aut', 'East Germany', 'Austria', 76, 49, 'Qualification Round', 'Group A'),
                    ('fra-den', 'France', 'Denmark', 77, 55, 'Qualification Round', 'Group A'),
                    ('gda-sco', 'East Germany', 'Scotland', 91, 54, 'Qualification Round', 'Group A'),
                    ('ned-aut', 'Netherlands', 'Austria', 78, 70, 'Qualification Round', 'Group A'),

                    ('eng-bel', 'England', 'Belgium', 92, 84, 'Qualification Round', 'Group B'),
                    ('hun-por', 'Hungary', 'Portugal', 115, 46, 'Qualification Round', 'Group B'),
                    ('grc-frg', 'Greece', 'West Germany', 81, 77, 'Qualification Round', 'Group B'),
                    ('tur-fin', 'Turkey', 'Finland', 86, 76, 'Qualification Round', 'Group B'),
                    ('bel-por', 'Belgium', 'Portugal', 94, 73, 'Qualification Round', 'Group B'),
                    ('grc-eng', 'Greece', 'England', 88, 81, 'Qualification Round', 'Group B'),
                    ('tur-hun', 'Turkey', 'Hungary', 80, 62, 'Qualification Round', 'Group B'),
                    ('frg-fin', 'West Germany', 'Finland', 95, 72, 'Qualification Round', 'Group B'),
                    ('tur-por', 'Turkey', 'Portugal', 101, 74, 'Qualification Round', 'Group B'),
                    ('frg-hun', 'West Germany', 'Hungary', 75, 67, 'Qualification Round', 'Group B'),
                    ('grc-bel', 'Greece', 'Belgium', 93, 67, 'Qualification Round', 'Group B'),
                    ('fin-eng', 'Finland', 'England', 90, 78, 'Qualification Round', 'Group B'),
                    ('tur-bel', 'Turkey', 'Belgium', 87, 63, 'Qualification Round', 'Group B'),
                    ('hun-eng', 'Hungary', 'England', 99, 88, 'Qualification Round', 'Group B'),
                    ('grc-fin', 'Greece', 'Finland', 112, 83, 'Qualification Round', 'Group B'),
                    ('frg-por', 'West Germany', 'Portugal', 95, 56, 'Qualification Round', 'Group B'),
                    ('bel-fin', 'Belgium', 'Finland', 103, 82, 'Qualification Round', 'Group B'),
                    ('eng-por', 'England', 'Portugal', 93, 78, 'Qualification Round', 'Group B'),
                    ('tur-frg', 'Turkey', 'West Germany', 82, 78, 'Qualification Round', 'Group B'),
                    ('grc-hun', 'Greece', 'Hungary', 86, 79, 'Qualification Round', 'Group B'),
                    ('tur-eng', 'Turkey', 'England', 100, 91, 'Qualification Round', 'Group B'),
                    ('hun-fin', 'Hungary', 'Finland', 79, 78, 'Qualification Round', 'Group B'),
                    ('grc-por', 'Greece', 'Portugal', 96, 65, 'Qualification Round', 'Group B'),
                    ('frg-bel', 'West Germany', 'Belgium', 83, 81, 'Qualification Round', 'Group B'),
                    ('fin-por', 'Finland', 'Portugal', 84, 76, 'Qualification Round', 'Group B'),
                    ('frg-eng', 'West Germany', 'England', 95, 88, 'Qualification Round', 'Group B'),
                    ('grc-tur', 'Greece', 'Turkey', 84, 81, 'Qualification Round', 'Group B'),
                    ('bel-hun', 'Belgium', 'Hungary', 89, 81, 'Qualification Round', 'Group B'),

                    ('isr-frg-final', 'Israel', 'West Germany', 84, 73, 'Final Phase', 'Final Phase'),
                    ('grc-gda-final', 'Greece', 'East Germany', 76, 58, 'Final Phase', 'Final Phase'),
                    ('fra-tur-final', 'France', 'Turkey', 71, 60, 'Final Phase', 'Final Phase'),
                    ('frg-gda-final', 'West Germany', 'East Germany', 76, 72, 'Final Phase', 'Final Phase'),
                    ('isr-tur-final', 'Israel', 'Turkey', 88, 83, 'Final Phase', 'Final Phase'),
                    ('fra-grc-final', 'France', 'Greece', 61, 57, 'Final Phase', 'Final Phase'),
                    ('fra-frg-final', 'France', 'West Germany', 71, 68, 'Final Phase', 'Final Phase'),
                    ('gda-tur-final', 'East Germany', 'Turkey', 63, 61, 'Final Phase', 'Final Phase'),
                    ('isr-grc-final', 'Israel', 'Greece', 75, 73, 'Final Phase', 'Final Phase')
                ) AS v(slug, home_name, away_name, home_score, away_score, phase, round_name)
                JOIN teams home ON home."CanonicalName" = v.home_name
                JOIN teams away ON away."CanonicalName" = v.away_name
                ON CONFLICT ("Source", "SourceGameId") DO NOTHING;

                IF (SELECT count(*) FROM games
                    WHERE "Source" = 'Los campeonatos de Europa 1935-1995'
                      AND "SourceGameId" LIKE 'eurobasket-division-b-1973-%') <> 55 THEN
                    RAISE EXCEPTION 'Expected fifty-five EuroBasket Division B 1973 book games';
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
