using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260907110000_AddEuroBasketDivisionB1977BookGames")]
public partial class AddEuroBasketDivisionB1977BookGames : Migration
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
                    (md5('tournament-cycle:eurobasket-division-b:1977')::uuid,
                     'eurobasket-division-b-1977', 'EuroBasket Division B', '1977',
                     'EuroBasket Division B 1977', now())
                ON CONFLICT ("Key") DO NOTHING;

                INSERT INTO seasons
                    ("Id", "CompetitionId", "Label", "StartDateUtc", "EndDateUtc", "CreatedAtUtc")
                VALUES
                    (md5('season:eurobasket-division-b:1977')::uuid,
                     competition_id, '1977',
                     timestamptz '1977-01-01 00:00:00+00',
                     timestamptz '1977-12-31 23:59:59+00', now())
                ON CONFLICT ("CompetitionId", "Label") DO NOTHING;

                SELECT "Id" INTO season_id
                FROM seasons
                WHERE "CompetitionId" = competition_id AND "Label" = '1977';

                SELECT "Id" INTO cycle_id
                FROM tournament_cycles
                WHERE "Key" = 'eurobasket-division-b-1977';

                IF cycle_id IS NULL THEN
                    RAISE EXCEPTION 'eurobasket-division-b-1977 tournament cycle is missing';
                END IF;

                -- Match dates are not printed on all supplied pages, so the
                -- deterministic season-start fallback is used. Finland's
                -- 82-78 win over Austria is the final score after overtime;
                -- the source note records the printed 72-72 regulation score.
                INSERT INTO games
                    ("Id", "Source", "SourceGameId", "SourceUrl", "SourceSeasonKey",
                     "SourceFetchedAtUtc", "SourceRevision", "ParserVersion",
                     "CompetitionId", "SeasonId", "TournamentCycleId", "GameDateTimeUtc",
                     "HomeTeamId", "AwayTeamId", "HomeScore", "AwayScore", "Status",
                     "CompetitionPhase", "CompetitionRound", "IsNeutralSite", "EloEligible",
                     "EloExclusionReason", "HasManualResultOverride", "IngestedAtUtc", "UpdatedAtUtc")
                SELECT md5('game:book:eurobasket-division-b-1977:' || v.slug)::uuid,
                       'Los campeonatos de Europa 1935-1995',
                       'eurobasket-division-b-1977-' || v.slug,
                       NULL, '1977', now(),
                       CASE WHEN v.slug = 'b-fin-aut'
                            THEN 'Los campeonatos de Europa 1935-1995 — Carlos Jiménez (overtime; 72-72 after 40 minutes)'
                            ELSE 'Los campeonatos de Europa 1935-1995 — Carlos Jiménez'
                       END,
                       'manual-book-photo-v1', competition_id, season_id, cycle_id,
                       timestamptz '1977-01-01 00:00:00+00', home."Id", away."Id",
                       v.home_score, v.away_score, 'finished', v.phase, v.round_name,
                       true, true, NULL, false, now(), now()
                FROM (VALUES
                    ('hemel-sco-den', 'Scotland', 'Denmark', 100, 77, 'Qualification Round', 'Hemel Hempstead Qualification'),
                    ('hemel-lux-irl', 'Luxembourg', 'Ireland', 72, 58, 'Qualification Round', 'Hemel Hempstead Qualification'),
                    ('hemel-aut-por', 'Austria', 'Portugal', 69, 53, 'Qualification Round', 'Hemel Hempstead Qualification'),
                    ('hemel-eng-isl', 'England', 'Iceland', 93, 85, 'Qualification Round', 'Hemel Hempstead Qualification'),
                    ('hemel-den-lux', 'Denmark', 'Luxembourg', 83, 64, 'Qualification Round', 'Hemel Hempstead Qualification'),
                    ('hemel-sco-irl', 'Scotland', 'Ireland', 85, 74, 'Qualification Round', 'Hemel Hempstead Qualification'),
                    ('hemel-aut-isl', 'Austria', 'Iceland', 107, 74, 'Qualification Round', 'Hemel Hempstead Qualification'),
                    ('hemel-eng-por', 'England', 'Portugal', 83, 62, 'Qualification Round', 'Hemel Hempstead Qualification'),
                    ('hemel-sco-lux', 'Scotland', 'Luxembourg', 73, 57, 'Qualification Round', 'Hemel Hempstead Qualification'),
                    ('hemel-den-irl', 'Denmark', 'Ireland', 84, 61, 'Qualification Round', 'Hemel Hempstead Qualification'),
                    ('hemel-isl-por', 'Iceland', 'Portugal', 83, 65, 'Qualification Round', 'Hemel Hempstead Qualification'),
                    ('hemel-aut-eng', 'Austria', 'England', 73, 52, 'Qualification Round', 'Hemel Hempstead Qualification'),
                    ('hemel-por-lux', 'Portugal', 'Luxembourg', 68, 59, 'Qualification Round', 'Hemel Hempstead Qualification'),
                    ('hemel-aut-den', 'Austria', 'Denmark', 109, 68, 'Qualification Round', 'Hemel Hempstead Qualification'),
                    ('hemel-isl-irl', 'Iceland', 'Ireland', 91, 83, 'Qualification Round', 'Hemel Hempstead Qualification'),
                    ('hemel-sco-eng', 'Scotland', 'England', 79, 72, 'Qualification Round', 'Hemel Hempstead Qualification'),
                    ('hemel-irl-por', 'Ireland', 'Portugal', 76, 69, 'Qualification Round', 'Hemel Hempstead Qualification'),
                    ('hemel-isl-lux', 'Iceland', 'Luxembourg', 106, 88, 'Qualification Round', 'Hemel Hempstead Qualification'),
                    ('hemel-eng-den', 'England', 'Denmark', 123, 80, 'Qualification Round', 'Hemel Hempstead Qualification'),
                    ('hemel-aut-sco', 'Austria', 'Scotland', 79, 69, 'Qualification Round', 'Hemel Hempstead Qualification'),
                    ('hemel-aut-lux', 'Austria', 'Luxembourg', 81, 66, 'Qualification Round', 'Hemel Hempstead Qualification'),
                    ('hemel-sco-isl', 'Scotland', 'Iceland', 97, 73, 'Qualification Round', 'Hemel Hempstead Qualification'),
                    ('hemel-eng-irl', 'England', 'Ireland', 85, 81, 'Qualification Round', 'Hemel Hempstead Qualification'),
                    ('hemel-den-por', 'Denmark', 'Portugal', 79, 65, 'Qualification Round', 'Hemel Hempstead Qualification'),

                    ('a-pol-sco', 'Poland', 'Scotland', 112, 69, 'Qualification Round', 'Group A'),
                    ('a-ned-swe', 'Netherlands', 'Sweden', 69, 68, 'Qualification Round', 'Group A'),
                    ('a-hun-frg', 'Hungary', 'West Germany', 65, 63, 'Qualification Round', 'Group A'),
                    ('a-pol-hun', 'Poland', 'Hungary', 105, 77, 'Qualification Round', 'Group A'),
                    ('a-swe-sco', 'Sweden', 'Scotland', 95, 62, 'Qualification Round', 'Group A'),
                    ('a-ned-frg', 'Netherlands', 'West Germany', 94, 68, 'Qualification Round', 'Group A'),
                    ('a-hun-sco', 'Hungary', 'Scotland', 93, 73, 'Qualification Round', 'Group A'),
                    ('a-frg-swe', 'West Germany', 'Sweden', 76, 71, 'Qualification Round', 'Group A'),
                    ('a-ned-pol', 'Netherlands', 'Poland', 93, 87, 'Qualification Round', 'Group A'),
                    ('a-ned-sco', 'Netherlands', 'Scotland', 108, 72, 'Qualification Round', 'Group A'),
                    ('a-hun-swe', 'Hungary', 'Sweden', 78, 63, 'Qualification Round', 'Group A'),
                    ('a-pol-frg', 'Poland', 'West Germany', 85, 71, 'Qualification Round', 'Group A'),
                    ('a-frg-sco', 'West Germany', 'Scotland', 93, 78, 'Qualification Round', 'Group A'),
                    ('a-swe-pol', 'Sweden', 'Poland', 90, 86, 'Qualification Round', 'Group A'),
                    ('a-ned-hun', 'Netherlands', 'Hungary', 69, 67, 'Qualification Round', 'Group A'),

                    ('b-fin-aut', 'Finland', 'Austria', 82, 78, 'Qualification Round', 'Group B'),
                    ('b-rom-tur', 'Romania', 'Turkey', 97, 89, 'Qualification Round', 'Group B'),
                    ('b-fra-grc', 'France', 'Greece', 88, 86, 'Qualification Round', 'Group B'),
                    ('b-fra-fin', 'France', 'Finland', 85, 78, 'Qualification Round', 'Group B'),
                    ('b-aut-tur', 'Austria', 'Turkey', 93, 69, 'Qualification Round', 'Group B'),
                    ('b-grc-rom', 'Greece', 'Romania', 90, 85, 'Qualification Round', 'Group B'),
                    ('b-tur-grc', 'Turkey', 'Greece', 90, 87, 'Qualification Round', 'Group B'),
                    ('b-fin-rom', 'Finland', 'Romania', 69, 68, 'Qualification Round', 'Group B'),
                    ('b-aut-fra', 'Austria', 'France', 79, 77, 'Qualification Round', 'Group B'),
                    ('b-aut-grc', 'Austria', 'Greece', 82, 65, 'Qualification Round', 'Group B'),
                    ('b-fra-rom', 'France', 'Romania', 100, 89, 'Qualification Round', 'Group B'),
                    ('b-fin-tur', 'Finland', 'Turkey', 68, 57, 'Qualification Round', 'Group B'),
                    ('b-rom-aut', 'Romania', 'Austria', 81, 75, 'Qualification Round', 'Group B'),
                    ('b-grc-fin', 'Greece', 'Finland', 85, 79, 'Qualification Round', 'Group B'),
                    ('b-fra-tur', 'France', 'Turkey', 99, 89, 'Qualification Round', 'Group B'),

                    ('final-ned-aut', 'Netherlands', 'Austria', 74, 71, 'Final Phase', 'Final Phase'),
                    ('final-fin-pol', 'Finland', 'Poland', 96, 94, 'Final Phase', 'Final Phase'),
                    ('final-hun-fra', 'Hungary', 'France', 82, 81, 'Final Phase', 'Final Phase'),
                    ('final-fra-pol', 'France', 'Poland', 95, 81, 'Final Phase', 'Final Phase'),
                    ('final-aut-hun', 'Austria', 'Hungary', 61, 57, 'Final Phase', 'Final Phase'),
                    ('final-ned-fin', 'Netherlands', 'Finland', 61, 57, 'Final Phase', 'Final Phase'),
                    ('final-fin-hun', 'Finland', 'Hungary', 70, 66, 'Final Phase', 'Final Phase'),
                    ('final-aut-pol', 'Austria', 'Poland', 81, 70, 'Final Phase', 'Final Phase'),
                    ('final-fra-ned', 'France', 'Netherlands', 72, 67, 'Final Phase', 'Final Phase')
                ) AS v(slug, home_name, away_name, home_score, away_score, phase, round_name)
                JOIN teams home ON home."CanonicalName" = v.home_name
                JOIN teams away ON away."CanonicalName" = v.away_name
                ON CONFLICT ("Source", "SourceGameId") DO NOTHING;

                IF (SELECT count(*) FROM games
                    WHERE "Source" = 'Los campeonatos de Europa 1935-1995'
                      AND "SourceGameId" LIKE 'eurobasket-division-b-1977-%') <> 63 THEN
                    RAISE EXCEPTION 'Expected sixty-three EuroBasket Division B 1977 book games';
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
