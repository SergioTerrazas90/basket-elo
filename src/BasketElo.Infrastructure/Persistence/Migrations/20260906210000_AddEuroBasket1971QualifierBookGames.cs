using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260906210000_AddEuroBasket1971QualifierBookGames")]
public partial class AddEuroBasket1971QualifierBookGames : Migration
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
                    (md5('season:eurobasket-qualifiers:1971')::uuid,
                     competition_id,
                     '1971',
                     timestamptz '1971-01-01 00:00:00+00',
                     timestamptz '1971-12-31 23:59:59+00',
                     now())
                ON CONFLICT ("CompetitionId", "Label") DO NOTHING;

                SELECT "Id" INTO season_id
                FROM seasons
                WHERE "CompetitionId" = competition_id
                  AND "Label" = '1971';

                SELECT "Id" INTO cycle_id
                FROM tournament_cycles
                WHERE "Key" = 'eurobasket-1971';

                IF cycle_id IS NULL THEN
                    RAISE EXCEPTION 'eurobasket-1971 tournament cycle is missing';
                END IF;

                -- The supplied photo lists forty-five results. The Group B
                -- table also gives the omitted Spain-Israel result (70-59),
                -- which is included with that reconstruction noted in its
                -- source revision. No match dates are printed, so 1 January
                -- is used as the deterministic season fallback.
                INSERT INTO games
                    ("Id", "Source", "SourceGameId", "SourceUrl", "SourceSeasonKey",
                     "SourceFetchedAtUtc", "SourceRevision", "ParserVersion",
                     "CompetitionId", "SeasonId", "TournamentCycleId", "GameDateTimeUtc",
                     "HomeTeamId", "AwayTeamId", "HomeScore", "AwayScore", "Status",
                     "CompetitionPhase", "CompetitionRound", "IsNeutralSite", "EloEligible",
                     "EloExclusionReason", "HasManualResultOverride", "IngestedAtUtc", "UpdatedAtUtc")
                SELECT md5('game:book:eurobasket-1971:' || v.slug)::uuid,
                       'Los campeonatos de Europa 1935-1995',
                       'eurobasket-1971-qualifier-' || v.slug,
                       NULL, '1971', now(),
                       CASE WHEN v.slug = 'esp-isr'
                            THEN 'Los campeonatos de Europa 1935-1995 — Carlos Jiménez (score reconstructed from printed standings)'
                            ELSE 'Los campeonatos de Europa 1935-1995 — Carlos Jiménez'
                       END,
                       'manual-book-photo-v1', competition_id, season_id, cycle_id,
                       timestamptz '1971-01-01 00:00:00+00', home."Id", away."Id",
                       v.home_score, v.away_score, 'finished', 'Qualification Round',
                       v.group_name, true, true, NULL, false, now(), now()
                FROM (VALUES
                    ('cze-sco', 'Czechoslovakia', 'Scotland', 111, 56, 'Group A'),
                    ('sui-grc', 'Switzerland', 'Greece', 90, 89, 'Group A'),
                    ('cze-grc', 'Czechoslovakia', 'Greece', 75, 73, 'Group A'),
                    ('fra-sui', 'France', 'Switzerland', 123, 62, 'Group A'),
                    ('fra-grc', 'France', 'Greece', 77, 73, 'Group A'),
                    ('sco-sui', 'Scotland', 'Switzerland', 72, 68, 'Group A'),
                    ('cze-sui', 'Czechoslovakia', 'Switzerland', 99, 59, 'Group A'),
                    ('fra-sco', 'France', 'Scotland', 99, 46, 'Group A'),
                    ('grc-sco', 'Greece', 'Scotland', 111, 72, 'Group A'),
                    ('fra-cze', 'France', 'Czechoslovakia', 74, 67, 'Group A'),
                    ('isr-swe', 'Israel', 'Sweden', 96, 85, 'Group B'),
                    ('esp-ned', 'Spain', 'Netherlands', 103, 75, 'Group B'),
                    ('esp-swe', 'Spain', 'Sweden', 90, 66, 'Group B'),
                    ('isr-ned', 'Israel', 'Netherlands', 70, 58, 'Group B'),
                    ('swe-ned', 'Sweden', 'Netherlands', 68, 62, 'Group B'),
                    ('esp-isr', 'Spain', 'Israel', 70, 59, 'Group B'),
                    ('pol-sui', 'Poland', 'Switzerland', 144, 56, 'Group C'),
                    ('rom-den', 'Romania', 'Denmark', 105, 40, 'Group C'),
                    ('hun-wal', 'Hungary', 'Wales', 133, 40, 'Group C'),
                    ('pol-rom', 'Poland', 'Romania', 83, 81, 'Group C'),
                    ('rom-hun', 'Romania', 'Hungary', 89, 73, 'Group C'),
                    ('pol-den', 'Poland', 'Denmark', 103, 44, 'Group C'),
                    ('hun-den', 'Hungary', 'Denmark', 88, 57, 'Group C'),
                    ('pol-hun', 'Poland', 'Hungary', 97, 53, 'Group C'),
                    ('den-wal', 'Denmark', 'Wales', 94, 83, 'Group C'),
                    ('pol-wal', 'Poland', 'Wales', 80, 69, 'Group C'),
                    ('fin-aut', 'Finland', 'Austria', 92, 73, 'Group D'),
                    ('tur-egy', 'Turkey', 'Egypt', 85, 43, 'Group D'),
                    ('yug-fin', 'Yugoslavia', 'Finland', 90, 60, 'Group D'),
                    ('tur-aut', 'Turkey', 'Austria', 97, 69, 'Group D'),
                    ('egy-fin', 'Egypt', 'Finland', 61, 60, 'Group D'),
                    ('yug-aut', 'Yugoslavia', 'Austria', 96, 54, 'Group D'),
                    ('yug-egy', 'Yugoslavia', 'Egypt', 112, 68, 'Group D'),
                    ('tur-fin', 'Turkey', 'Finland', 73, 66, 'Group D'),
                    ('aut-egy', 'Austria', 'Egypt', 66, 49, 'Group D'),
                    ('yug-tur', 'Yugoslavia', 'Turkey', 109, 62, 'Group D'),
                    ('ita-alb', 'Italy', 'Albania', 74, 59, 'Group E'),
                    ('bul-bel', 'Bulgaria', 'Belgium', 105, 75, 'Group E'),
                    ('ita-bel', 'Italy', 'Belgium', 84, 56, 'Group E'),
                    ('alb-eng', 'Albania', 'England', 92, 63, 'Group E'),
                    ('ita-eng', 'Italy', 'England', 96, 48, 'Group E'),
                    ('bul-alb', 'Bulgaria', 'Albania', 102, 74, 'Group E'),
                    ('bul-eng', 'Bulgaria', 'England', 96, 57, 'Group E'),
                    ('bel-alb', 'Belgium', 'Albania', 76, 65, 'Group E'),
                    ('bul-ita', 'Bulgaria', 'Italy', 83, 65, 'Group E'),
                    ('bel-eng', 'Belgium', 'England', 76, 58, 'Group E')
                ) AS v(slug, home_name, away_name, home_score, away_score, group_name)
                JOIN teams home ON home."CanonicalName" = v.home_name
                JOIN teams away ON away."CanonicalName" = v.away_name
                ON CONFLICT ("Source", "SourceGameId") DO NOTHING;

                IF (SELECT count(*) FROM games
                    WHERE "Source" = 'Los campeonatos de Europa 1935-1995'
                      AND "SourceGameId" LIKE 'eurobasket-1971-qualifier-%') <> 46 THEN
                    RAISE EXCEPTION 'Expected forty-six EuroBasket 1971 qualifier book games';
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
