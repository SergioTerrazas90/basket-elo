using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260906200000_AddEuroBasket1969QualifierBookGames")]
public partial class AddEuroBasket1969QualifierBookGames : Migration
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
                WHERE "Name" = 'EuroBasket Qualifiers'
                  AND "CountryCode" IS NULL;

                IF competition_id IS NULL THEN
                    RAISE EXCEPTION 'EuroBasket Qualifiers competition is missing';
                END IF;

                INSERT INTO seasons
                    ("Id", "CompetitionId", "Label", "StartDateUtc", "EndDateUtc", "CreatedAtUtc")
                VALUES
                    (md5('season:eurobasket-qualifiers:1969')::uuid,
                     competition_id,
                     '1969',
                     timestamptz '1969-01-01 00:00:00+00',
                     timestamptz '1969-12-31 23:59:59+00',
                     now())
                ON CONFLICT ("CompetitionId", "Label") DO NOTHING;

                SELECT "Id" INTO season_id
                FROM seasons
                WHERE "CompetitionId" = competition_id
                  AND "Label" = '1969';

                SELECT "Id" INTO cycle_id
                FROM tournament_cycles
                WHERE "Key" = 'eurobasket-1969';

                IF cycle_id IS NULL THEN
                    RAISE EXCEPTION 'eurobasket-1969 tournament cycle is missing';
                END IF;

                -- The supplied photos list these forty-six results. They do
                -- not print match dates, so 1 January is used as the
                -- deterministic season fallback until dated source material
                -- is available. The two Yugoslavia-Netherlands entries in
                -- Group B are retained separately exactly as printed.
                INSERT INTO games
                    ("Id", "Source", "SourceGameId", "SourceUrl", "SourceSeasonKey",
                     "SourceFetchedAtUtc", "SourceRevision", "ParserVersion",
                     "CompetitionId", "SeasonId", "TournamentCycleId", "GameDateTimeUtc",
                     "HomeTeamId", "AwayTeamId", "HomeScore", "AwayScore", "Status",
                     "CompetitionPhase", "CompetitionRound", "IsNeutralSite", "EloEligible",
                     "EloExclusionReason", "HasManualResultOverride", "IngestedAtUtc", "UpdatedAtUtc")
                SELECT md5('game:book:eurobasket-1969:' || v.slug)::uuid,
                       'Los campeonatos de Europa 1935-1995',
                       'eurobasket-1969-qualifier-' || v.slug,
                       NULL, '1969', now(),
                       'Los campeonatos de Europa 1935-1995 — Carlos Jiménez',
                       'manual-book-photo-v1-v2', competition_id, season_id, cycle_id,
                       timestamptz '1969-01-01 00:00:00+00', home."Id", away."Id",
                       v.home_score, v.away_score, 'finished', 'Qualification Round',
                       v.group_name, true, true, NULL, false, now(), now()
                FROM (VALUES
                    ('cze-den', 'Czechoslovakia', 'Denmark', 82, 55, 'Group A'),
                    ('swe-isl', 'Sweden', 'Iceland', 79, 51, 'Group A'),
                    ('cze-isl', 'Czechoslovakia', 'Iceland', 123, 63, 'Group A'),
                    ('swe-den', 'Sweden', 'Denmark', 82, 70, 'Group A'),
                    ('isl-den', 'Iceland', 'Denmark', 51, 49, 'Group A'),
                    ('cze-swe', 'Czechoslovakia', 'Sweden', 78, 64, 'Group A'),
                    ('yug-ned-1', 'Yugoslavia', 'Netherlands', 114, 69, 'Group B'),
                    ('rom-ned', 'Romania', 'Netherlands', 80, 69, 'Group B'),
                    ('rom-sco', 'Romania', 'Scotland', 98, 44, 'Group B'),
                    ('ned-eng', 'Netherlands', 'England', 87, 72, 'Group B'),
                    ('yug-sco', 'Yugoslavia', 'Scotland', 108, 47, 'Group B'),
                    ('rom-eng', 'Romania', 'England', 91, 61, 'Group B'),
                    ('eng-sco', 'England', 'Scotland', 71, 53, 'Group B'),
                    ('yug-ned-2', 'Yugoslavia', 'Netherlands', 81, 65, 'Group B'),
                    ('ned-sco', 'Netherlands', 'Scotland', 103, 76, 'Group B'),
                    ('yug-rom', 'Yugoslavia', 'Romania', 69, 55, 'Group B'),
                    ('esp-sui', 'Spain', 'Switzerland', 93, 52, 'Group C'),
                    ('bel-egy', 'Belgium', 'Egypt', 70, 62, 'Group C'),
                    ('bel-sui', 'Belgium', 'Switzerland', 79, 48, 'Group C'),
                    ('bul-egy', 'Bulgaria', 'Egypt', 77, 61, 'Group C'),
                    ('esp-bel', 'Spain', 'Belgium', 78, 64, 'Group C'),
                    ('bul-sui', 'Bulgaria', 'Switzerland', 93, 60, 'Group C'),
                    ('bul-bel', 'Bulgaria', 'Belgium', 88, 67, 'Group C'),
                    ('esp-egy', 'Spain', 'Egypt', 91, 42, 'Group C'),
                    ('egy-sui', 'Egypt', 'Switzerland', 77, 61, 'Group C'),
                    ('esp-bul', 'Spain', 'Bulgaria', 72, 68, 'Group C'),
                    ('grc-frg', 'Greece', 'West Germany', 69, 65, 'Group D'),
                    ('isr-fin', 'Israel', 'Finland', 69, 52, 'Group D'),
                    ('aut-frg', 'Austria', 'West Germany', 82, 75, 'Group D'),
                    ('grc-fin', 'Greece', 'Finland', 68, 62, 'Group D'),
                    ('frg-fin', 'West Germany', 'Finland', 74, 60, 'Group D'),
                    ('isr-aut', 'Israel', 'Austria', 85, 73, 'Group D'),
                    ('aut-grc', 'Austria', 'Greece', 66, 60, 'Group D'),
                    ('isr-frg', 'Israel', 'West Germany', 76, 50, 'Group D'),
                    ('aut-fin', 'Austria', 'Finland', 63, 53, 'Group D'),
                    ('grc-isr', 'Greece', 'Israel', 70, 66, 'Group D'),
                    ('fra-alb', 'France', 'Albania', 71, 59, 'Group E'),
                    ('hun-tur', 'Hungary', 'Turkey', 69, 58, 'Group E'),
                    ('pol-alb', 'Poland', 'Albania', 91, 57, 'Group E'),
                    ('tur-fra', 'Turkey', 'France', 72, 67, 'Group E'),
                    ('pol-tur', 'Poland', 'Turkey', 79, 65, 'Group E'),
                    ('hun-fra', 'Hungary', 'France', 69, 62, 'Group E'),
                    ('hun-alb', 'Hungary', 'Albania', 61, 54, 'Group E'),
                    ('pol-fra', 'Poland', 'France', 66, 61, 'Group E'),
                    ('tur-alb', 'Turkey', 'Albania', 81, 56, 'Group E'),
                    ('pol-hun', 'Poland', 'Hungary', 84, 74, 'Group E')
                ) AS v(slug, home_name, away_name, home_score, away_score, group_name)
                JOIN teams home ON home."CanonicalName" = v.home_name
                JOIN teams away ON away."CanonicalName" = v.away_name
                ON CONFLICT ("Source", "SourceGameId") DO NOTHING;

                IF (SELECT count(*) FROM games
                    WHERE "Source" = 'Los campeonatos de Europa 1935-1995'
                      AND "SourceGameId" LIKE 'eurobasket-1969-qualifier-%') <> 46 THEN
                    RAISE EXCEPTION 'Expected forty-six EuroBasket 1969 qualifier book games';
                END IF;
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // This is an audited data addition. The pre-deployment database backup
        // is the rollback path, consistent with the historical data migrations.
    }
}
