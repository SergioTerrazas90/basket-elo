using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260906170000_AddEuroBasket1967QualifierBookGames")]
public partial class AddEuroBasket1967QualifierBookGames : Migration
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
                    (md5('season:eurobasket-qualifiers:1967')::uuid,
                     competition_id,
                     '1967',
                     timestamptz '1967-01-01 00:00:00+00',
                     timestamptz '1967-12-31 23:59:59+00',
                     now())
                ON CONFLICT ("CompetitionId", "Label") DO NOTHING;

                SELECT "Id" INTO season_id
                FROM seasons
                WHERE "CompetitionId" = competition_id
                  AND "Label" = '1967';

                SELECT "Id" INTO cycle_id
                FROM tournament_cycles
                WHERE "Key" = 'eurobasket-1967';

                IF cycle_id IS NULL THEN
                    RAISE EXCEPTION 'eurobasket-1967 tournament cycle is missing';
                END IF;

                -- The supplied photo lists these fifteen results. It does not
                -- print match dates, so 1 January is used as the deterministic
                -- season fallback until dated source material is available.
                INSERT INTO games
                    ("Id", "Source", "SourceGameId", "SourceUrl", "SourceSeasonKey",
                     "SourceFetchedAtUtc", "SourceRevision", "ParserVersion",
                     "CompetitionId", "SeasonId", "TournamentCycleId", "GameDateTimeUtc",
                     "HomeTeamId", "AwayTeamId", "HomeScore", "AwayScore", "Status",
                     "CompetitionPhase", "CompetitionRound", "IsNeutralSite", "EloEligible",
                     "EloExclusionReason", "HasManualResultOverride", "IngestedAtUtc", "UpdatedAtUtc")
                SELECT md5('game:book:eurobasket-1967:' || v.slug)::uuid,
                       'Los campeonatos de Europa 1935-1995',
                       'eurobasket-1967-qualifier-' || v.slug,
                       NULL, '1967', now(),
                       'Los campeonatos de Europa 1935-1995 — Carlos Jiménez',
                       'manual-book-photo-v1', competition_id, season_id, cycle_id,
                       timestamptz '1967-01-01 00:00:00+00', home."Id", away."Id",
                       v.home_score, v.away_score, 'finished', 'Qualification Round',
                       v.group_name, true, true, NULL, false, now(), now()
                FROM (VALUES
                    ('bel-den', 'Belgium', 'Denmark', 94, 50, 'Group A'),
                    ('isr-lux', 'Israel', 'Luxembourg', 81, 39, 'Group A'),
                    ('bel-lux', 'Belgium', 'Luxembourg', 106, 52, 'Group A'),
                    ('isr-den', 'Israel', 'Denmark', 101, 49, 'Group A'),
                    ('den-lux', 'Denmark', 'Luxembourg', 63, 53, 'Group A'),
                    ('bel-isr', 'Belgium', 'Israel', 73, 70, 'Group A'),
                    ('esp-sui', 'Spain', 'Switzerland', 88, 39, 'Group B'),
                    ('fra-sui', 'France', 'Switzerland', 105, 51, 'Group B'),
                    ('fra-esp', 'France', 'Spain', 81, 74, 'Group B'),
                    ('gdr-swe', 'German DR', 'Sweden', 87, 42, 'Group C'),
                    ('cze-aut', 'Czechoslovakia', 'Austria', 94, 65, 'Group C'),
                    ('gdr-aut', 'German DR', 'Austria', 75, 59, 'Group C'),
                    ('cze-swe', 'Czechoslovakia', 'Sweden', 87, 62, 'Group C'),
                    ('aut-swe', 'Austria', 'Sweden', 58, 56, 'Group C'),
                    ('gdr-cze', 'German DR', 'Czechoslovakia', 60, 52, 'Group C')
                ) AS v(slug, home_name, away_name, home_score, away_score, group_name)
                JOIN teams home ON home."CanonicalName" = v.home_name
                JOIN teams away ON away."CanonicalName" = v.away_name
                ON CONFLICT ("Source", "SourceGameId") DO NOTHING;

                IF (SELECT count(*) FROM games
                    WHERE "Source" = 'Los campeonatos de Europa 1935-1995'
                      AND "SourceGameId" LIKE 'eurobasket-1967-qualifier-%') <> 15 THEN
                    RAISE EXCEPTION 'Expected fifteen EuroBasket 1967 qualifier book games';
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
