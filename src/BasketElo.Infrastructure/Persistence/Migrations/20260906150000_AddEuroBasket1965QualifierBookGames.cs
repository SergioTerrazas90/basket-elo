using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260906150000_AddEuroBasket1965QualifierBookGames")]
public partial class AddEuroBasket1965QualifierBookGames : Migration
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
                    (md5('season:eurobasket-qualifiers:1965')::uuid,
                     competition_id,
                     '1965',
                     timestamptz '1965-01-01 00:00:00+00',
                     timestamptz '1965-12-31 23:59:59+00',
                     now())
                ON CONFLICT ("CompetitionId", "Label") DO NOTHING;

                SELECT "Id" INTO season_id
                FROM seasons
                WHERE "CompetitionId" = competition_id
                  AND "Label" = '1965';

                SELECT "Id" INTO cycle_id
                FROM tournament_cycles
                WHERE "Key" = 'eurobasket-1965';

                IF cycle_id IS NULL THEN
                    RAISE EXCEPTION 'eurobasket-1965 tournament cycle is missing';
                END IF;

                -- The supplied photo lists these eleven results. It does not
                -- print match dates, so 1 January is used as the deterministic
                -- season fallback until dated source material is available.
                INSERT INTO games
                    ("Id", "Source", "SourceGameId", "SourceUrl", "SourceSeasonKey",
                     "SourceFetchedAtUtc", "SourceRevision", "ParserVersion",
                     "CompetitionId", "SeasonId", "TournamentCycleId", "GameDateTimeUtc",
                     "HomeTeamId", "AwayTeamId", "HomeScore", "AwayScore", "Status",
                     "CompetitionPhase", "CompetitionRound", "IsNeutralSite", "EloEligible",
                     "EloExclusionReason", "HasManualResultOverride", "IngestedAtUtc", "UpdatedAtUtc")
                SELECT md5('game:book:eurobasket-1965:' || v.slug)::uuid,
                       'Los campeonatos de Europa 1935-1995',
                       'eurobasket-1965-qualifier-' || v.slug,
                       NULL, '1965', now(),
                       'Los campeonatos de Europa 1935-1995 — Carlos Jiménez',
                       'manual-book-photo-v1', competition_id, season_id, cycle_id,
                       timestamptz '1965-01-01 00:00:00+00', home."Id", away."Id",
                       v.home_score, v.away_score, 'finished', 'Qualification Round',
                       v.group_name, true, true, NULL, false, now(), now()
                FROM (VALUES
                    ('isr-lux', 'Israel', 'Luxembourg', 81, 42, 'Group A'),
                    ('fra-eng', 'France', 'England', 90, 54, 'Group A'),
                    ('lux-eng', 'Luxembourg', 'England', 69, 67, 'Group A'),
                    ('fra-isr', 'France', 'Israel', 77, 75, 'Group A'),
                    ('isr-eng', 'Israel', 'England', 99, 58, 'Group A'),
                    ('fra-lux', 'France', 'Luxembourg', 90, 32, 'Group A'),
                    ('ita-sui', 'Italy', 'Switzerland', 99, 39, 'Group B'),
                    ('esp-ned', 'Spain', 'Netherlands', 85, 77, 'Group B'),
                    ('ita-ned', 'Italy', 'Netherlands', 95, 54, 'Group B'),
                    ('esp-sui', 'Spain', 'Switzerland', 88, 51, 'Group B'),
                    ('ned-sui', 'Netherlands', 'Switzerland', 84, 59, 'Group B')
                ) AS v(slug, home_name, away_name, home_score, away_score, group_name)
                JOIN teams home ON home."CanonicalName" = v.home_name
                JOIN teams away ON away."CanonicalName" = v.away_name
                ON CONFLICT ("Source", "SourceGameId") DO NOTHING;

                IF (SELECT count(*) FROM games
                    WHERE "Source" = 'Los campeonatos de Europa 1935-1995'
                      AND "SourceGameId" LIKE 'eurobasket-1965-qualifier-%') <> 11 THEN
                    RAISE EXCEPTION 'Expected eleven EuroBasket 1965 qualifier book games';
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
