using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260905170000_AddEuroBasket1963QualifierBookGames")]
public partial class AddEuroBasket1963QualifierBookGames : Migration
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
                    (md5('season:eurobasket-qualifiers:1963')::uuid,
                     competition_id,
                     '1963',
                     timestamptz '1963-01-01 00:00:00+00',
                     timestamptz '1963-12-31 23:59:59+00',
                     now())
                ON CONFLICT ("CompetitionId", "Label") DO NOTHING;

                SELECT "Id" INTO season_id
                FROM seasons
                WHERE "CompetitionId" = competition_id
                  AND "Label" = '1963';

                SELECT "Id" INTO cycle_id
                FROM tournament_cycles
                WHERE "Key" = 'eurobasket-1963';

                IF cycle_id IS NULL THEN
                    RAISE EXCEPTION 'eurobasket-1963 tournament cycle is missing';
                END IF;

                -- The book supplied by the user is retained as a separate,
                -- auditable source. The Luxembourg-Netherlands date is not
                -- printed in the supplied photo, so the edition-start date is
                -- used as the deterministic fallback already used by other
                -- historical providers when a source omits a date.
                INSERT INTO games
                    ("Id", "Source", "SourceGameId", "SourceUrl", "SourceSeasonKey",
                     "SourceFetchedAtUtc", "SourceRevision", "ParserVersion",
                     "CompetitionId", "SeasonId", "TournamentCycleId", "GameDateTimeUtc",
                     "HomeTeamId", "AwayTeamId", "HomeScore", "AwayScore", "Status",
                     "CompetitionPhase", "CompetitionRound", "IsNeutralSite", "EloEligible",
                     "EloExclusionReason", "HasManualResultOverride", "IngestedAtUtc", "UpdatedAtUtc")
                SELECT md5('game:book:eurobasket-1963:esp-lby')::uuid,
                       'book', 'eurobasket-1963-qualifier-esp-lby',
                       'manual://book/eurobasket-1963-qualifiers/photo-1', '1963',
                       now(), 'user-book-photo-1', 'manual-book-photo-v1',
                       competition_id, season_id, cycle_id,
                       timestamptz '1963-05-09 00:00:00+00',
                       home."Id", away."Id", 118, 32, 'finished',
                       'Qualification Round', 'Group A', true, true,
                       NULL, false, now(), now()
                FROM teams home, teams away
                WHERE home."CanonicalName" = 'Spain'
                  AND away."CanonicalName" = 'Libya'
                ON CONFLICT ("Source", "SourceGameId") DO NOTHING;

                INSERT INTO games
                    ("Id", "Source", "SourceGameId", "SourceUrl", "SourceSeasonKey",
                     "SourceFetchedAtUtc", "SourceRevision", "ParserVersion",
                     "CompetitionId", "SeasonId", "TournamentCycleId", "GameDateTimeUtc",
                     "HomeTeamId", "AwayTeamId", "HomeScore", "AwayScore", "Status",
                     "CompetitionPhase", "CompetitionRound", "IsNeutralSite", "EloEligible",
                     "EloExclusionReason", "HasManualResultOverride", "IngestedAtUtc", "UpdatedAtUtc")
                SELECT md5('game:book:eurobasket-1963:por-lby')::uuid,
                       'book', 'eurobasket-1963-qualifier-por-lby',
                       'manual://book/eurobasket-1963-qualifiers/photo-1', '1963',
                       now(), 'user-book-photo-1', 'manual-book-photo-v1',
                       competition_id, season_id, cycle_id,
                       timestamptz '1963-05-10 00:00:00+00',
                       home."Id", away."Id", 52, 46, 'finished',
                       'Qualification Round', 'Group A', true, true,
                       NULL, false, now(), now()
                FROM teams home, teams away
                WHERE home."CanonicalName" = 'Portugal'
                  AND away."CanonicalName" = 'Libya'
                ON CONFLICT ("Source", "SourceGameId") DO NOTHING;

                INSERT INTO games
                    ("Id", "Source", "SourceGameId", "SourceUrl", "SourceSeasonKey",
                     "SourceFetchedAtUtc", "SourceRevision", "ParserVersion",
                     "CompetitionId", "SeasonId", "TournamentCycleId", "GameDateTimeUtc",
                     "HomeTeamId", "AwayTeamId", "HomeScore", "AwayScore", "Status",
                     "CompetitionPhase", "CompetitionRound", "IsNeutralSite", "EloEligible",
                     "EloExclusionReason", "HasManualResultOverride", "IngestedAtUtc", "UpdatedAtUtc")
                SELECT md5('game:book:eurobasket-1963:esp-por')::uuid,
                       'book', 'eurobasket-1963-qualifier-esp-por',
                       'manual://book/eurobasket-1963-qualifiers/photo-1', '1963',
                       now(), 'user-book-photo-1', 'manual-book-photo-v1',
                       competition_id, season_id, cycle_id,
                       timestamptz '1963-05-11 00:00:00+00',
                       home."Id", away."Id", 89, 54, 'finished',
                       'Qualification Round', 'Group A', true, true,
                       NULL, false, now(), now()
                FROM teams home, teams away
                WHERE home."CanonicalName" = 'Spain'
                  AND away."CanonicalName" = 'Portugal'
                ON CONFLICT ("Source", "SourceGameId") DO NOTHING;

                INSERT INTO games
                    ("Id", "Source", "SourceGameId", "SourceUrl", "SourceSeasonKey",
                     "SourceFetchedAtUtc", "SourceRevision", "ParserVersion",
                     "CompetitionId", "SeasonId", "TournamentCycleId", "GameDateTimeUtc",
                     "HomeTeamId", "AwayTeamId", "HomeScore", "AwayScore", "Status",
                     "CompetitionPhase", "CompetitionRound", "IsNeutralSite", "EloEligible",
                     "EloExclusionReason", "HasManualResultOverride", "IngestedAtUtc", "UpdatedAtUtc")
                SELECT md5('game:book:eurobasket-1963:lux-ned')::uuid,
                       'book', 'eurobasket-1963-qualifier-lux-ned',
                       'manual://book/eurobasket-1963-qualifiers/photo-1', '1963',
                       now(), 'user-book-photo-1', 'manual-book-photo-v1',
                       competition_id, season_id, cycle_id,
                       timestamptz '1963-01-01 00:00:00+00',
                       home."Id", away."Id", 40, 52, 'finished',
                       'Qualification Round', 'Group B', true, true,
                       NULL, false, now(), now()
                FROM teams home, teams away
                WHERE home."CanonicalName" = 'Luxembourg'
                  AND away."CanonicalName" = 'Netherlands'
                ON CONFLICT ("Source", "SourceGameId") DO NOTHING;

                INSERT INTO games
                    ("Id", "Source", "SourceGameId", "SourceUrl", "SourceSeasonKey",
                     "SourceFetchedAtUtc", "SourceRevision", "ParserVersion",
                     "CompetitionId", "SeasonId", "TournamentCycleId", "GameDateTimeUtc",
                     "HomeTeamId", "AwayTeamId", "HomeScore", "AwayScore", "Status",
                     "CompetitionPhase", "CompetitionRound", "IsNeutralSite", "EloEligible",
                     "EloExclusionReason", "HasManualResultOverride", "IngestedAtUtc", "UpdatedAtUtc")
                SELECT md5('game:book:eurobasket-1963:ned-eng')::uuid,
                       'book', 'eurobasket-1963-qualifier-ned-eng',
                       'manual://book/eurobasket-1963-qualifiers/photo-1', '1963',
                       now(), 'user-book-photo-1', 'manual-book-photo-v1',
                       competition_id, season_id, cycle_id,
                       timestamptz '1963-03-23 00:00:00+00',
                       home."Id", away."Id", 95, 90, 'finished',
                       'Qualification Round', 'Group B', true, true,
                       NULL, false, now(), now()
                FROM teams home, teams away
                WHERE home."CanonicalName" = 'Netherlands'
                  AND away."CanonicalName" = 'England'
                ON CONFLICT ("Source", "SourceGameId") DO NOTHING;

                INSERT INTO games
                    ("Id", "Source", "SourceGameId", "SourceUrl", "SourceSeasonKey",
                     "SourceFetchedAtUtc", "SourceRevision", "ParserVersion",
                     "CompetitionId", "SeasonId", "TournamentCycleId", "GameDateTimeUtc",
                     "HomeTeamId", "AwayTeamId", "HomeScore", "AwayScore", "Status",
                     "CompetitionPhase", "CompetitionRound", "IsNeutralSite", "EloEligible",
                     "EloExclusionReason", "HasManualResultOverride", "IngestedAtUtc", "UpdatedAtUtc")
                SELECT md5('game:book:eurobasket-1963:ita-sui')::uuid,
                       'book', 'eurobasket-1963-qualifier-ita-sui',
                       'manual://book/eurobasket-1963-qualifiers/photo-1', '1963',
                       now(), 'user-book-photo-1', 'manual-book-photo-v1',
                       competition_id, season_id, cycle_id,
                       timestamptz '1963-05-03 00:00:00+00',
                       home."Id", away."Id", 90, 57, 'finished',
                       'Qualification Round', 'Group C', true, true,
                       NULL, false, now(), now()
                FROM teams home, teams away
                WHERE home."CanonicalName" = 'Italy'
                  AND away."CanonicalName" = 'Switzerland'
                ON CONFLICT ("Source", "SourceGameId") DO NOTHING;

                IF (SELECT count(*) FROM games
                    WHERE "Source" = 'book'
                      AND "SourceGameId" LIKE 'eurobasket-1963-qualifier-%') <> 6 THEN
                    RAISE EXCEPTION 'Expected six EuroBasket 1963 qualifier book games';
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
