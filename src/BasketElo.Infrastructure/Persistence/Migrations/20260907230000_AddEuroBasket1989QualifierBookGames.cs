using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260907230000_AddEuroBasket1989QualifierBookGames")]
public partial class AddEuroBasket1989QualifierBookGames : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            DECLARE competition_id uuid; season_id uuid; cycle_id uuid;
            BEGIN
                SELECT "Id" INTO competition_id FROM competitions
                WHERE "Name" = 'EuroBasket Qualifiers' AND "CountryCode" IS NULL;
                IF competition_id IS NULL THEN RAISE EXCEPTION 'EuroBasket Qualifiers competition is missing'; END IF;
                INSERT INTO tournament_cycles ("Id", "Key", "Family", "EditionLabel", "DisplayName", "CreatedAtUtc")
                VALUES (md5('tournament-cycle:eurobasket-qualifiers:1989')::uuid, 'eurobasket-qualifiers-1989',
                        'EuroBasket Qualifiers', '1989', 'EuroBasket Qualifiers 1989', now()) ON CONFLICT ("Key") DO NOTHING;
                INSERT INTO seasons ("Id", "CompetitionId", "Label", "StartDateUtc", "EndDateUtc", "CreatedAtUtc")
                VALUES (md5('season:eurobasket-qualifiers:1989')::uuid, competition_id, '1989',
                        timestamptz '1989-01-01 00:00:00+00', timestamptz '1989-12-31 23:59:59+00', now())
                ON CONFLICT ("CompetitionId", "Label") DO NOTHING;
                SELECT "Id" INTO season_id FROM seasons WHERE "CompetitionId" = competition_id AND "Label" = '1989';
                SELECT "Id" INTO cycle_id FROM tournament_cycles WHERE "Key" = 'eurobasket-qualifiers-1989';
                IF cycle_id IS NULL THEN RAISE EXCEPTION 'eurobasket-qualifiers-1989 tournament cycle is missing'; END IF;

                INSERT INTO games
                    ("Id", "Source", "SourceGameId", "SourceUrl", "SourceSeasonKey", "SourceFetchedAtUtc",
                     "SourceRevision", "ParserVersion", "CompetitionId", "SeasonId", "TournamentCycleId",
                     "GameDateTimeUtc", "HomeTeamId", "AwayTeamId", "HomeScore", "AwayScore", "Status",
                     "CompetitionPhase", "CompetitionRound", "IsNeutralSite", "EloEligible", "EloExclusionReason",
                     "HasManualResultOverride", "IngestedAtUtc", "UpdatedAtUtc")
                SELECT md5('game:book:eurobasket-qualifiers-1989:' || v.slug)::uuid,
                       'Los campeonatos de Europa 1935-1995', 'eurobasket-qualifiers-1989-' || v.slug,
                       NULL, '1989', now(),
                       CASE WHEN v.slug IN ('c-sui-isl', 'd-eng-swe')
                            THEN 'Los campeonatos de Europa 1935-1995 — Carlos Jiménez (overtime noted in source)'
                            ELSE 'Los campeonatos de Europa 1935-1995 — Carlos Jiménez' END,
                       'manual-book-photo-v1', competition_id, season_id, cycle_id,
                       timestamptz '1989-01-01 00:00:00+00', home."Id", away."Id", v.home_score, v.away_score,
                       'finished', v.phase, v.round_name, true, true, NULL, false, now(), now()
                FROM (VALUES
                    ('b-fin-isr', 'Finland', 'Israel', 106, 102, 'Qualification Round', 'Group B'),
                    ('b-bul-nor', 'Bulgaria', 'Norway', 102, 84, 'Qualification Round', 'Group B'),
                    ('b-fin-nor', 'Finland', 'Norway', 89, 77, 'Qualification Round', 'Group B'),
                    ('b-isr-bul', 'Israel', 'Bulgaria', 92, 90, 'Qualification Round', 'Group B'),
                    ('b-isr-nor', 'Israel', 'Norway', 95, 74, 'Qualification Round', 'Group B'),
                    ('b-bul-fin', 'Bulgaria', 'Finland', 101, 98, 'Qualification Round', 'Group B'),

                    ('c-fra-isl', 'France', 'Iceland', 104, 63, 'Qualification Round', 'Group C'),
                    ('c-sui-den', 'Switzerland', 'Denmark', 89, 82, 'Qualification Round', 'Group C'),
                    ('c-fra-sui', 'France', 'Switzerland', 106, 60, 'Qualification Round', 'Group C'),
                    ('c-den-isl', 'Denmark', 'Iceland', 76, 73, 'Qualification Round', 'Group C'),
                    ('c-sui-isl', 'Switzerland', 'Iceland', 112, 100, 'Qualification Round', 'Group C'),
                    ('c-fra-den', 'France', 'Denmark', 123, 95, 'Qualification Round', 'Group C'),

                    ('d-eng-swe', 'England', 'Sweden', 115, 108, 'Qualification Round', 'Group D'),
                    ('d-tur-sco', 'Turkey', 'Scotland', 87, 71, 'Qualification Round', 'Group D'),
                    ('d-eng-sco', 'England', 'Scotland', 120, 80, 'Qualification Round', 'Group D'),
                    ('d-swe-tur', 'Sweden', 'Turkey', 87, 82, 'Qualification Round', 'Group D'),
                    ('d-swe-sco', 'Sweden', 'Scotland', 109, 85, 'Qualification Round', 'Group D'),
                    ('d-eng-tur', 'England', 'Turkey', 84, 83, 'Qualification Round', 'Group D'),

                    ('sa-grc-eng', 'Greece', 'England', 107, 66, 'Semifinal Phase', 'Semifinal Group A'),
                    ('sa-cze-ned', 'Czechoslovakia', 'Netherlands', 79, 86, 'Semifinal Phase', 'Semifinal Group A'),
                    ('sa-grc-ned', 'Greece', 'Netherlands', 102, 62, 'Semifinal Phase', 'Semifinal Group A'),
                    ('sa-eng-cze', 'England', 'Czechoslovakia', 103, 101, 'Semifinal Phase', 'Semifinal Group A'),
                    ('sa-cze-grc', 'Czechoslovakia', 'Greece', 76, 89, 'Semifinal Phase', 'Semifinal Group A'),
                    ('sa-ned-eng', 'Netherlands', 'England', 93, 75, 'Semifinal Phase', 'Semifinal Group A'),
                    ('sa-eng-grc', 'England', 'Greece', 76, 84, 'Semifinal Phase', 'Semifinal Group A'),
                    ('sa-ned-cze', 'Netherlands', 'Czechoslovakia', 89, 67, 'Semifinal Phase', 'Semifinal Group A'),
                    ('sa-ned-grc', 'Netherlands', 'Greece', 66, 93, 'Semifinal Phase', 'Semifinal Group A'),
                    ('sa-cze-eng', 'Czechoslovakia', 'England', 107, 80, 'Semifinal Phase', 'Semifinal Group A'),
                    ('sa-grc-cze', 'Greece', 'Czechoslovakia', 107, 85, 'Semifinal Phase', 'Semifinal Group A'),
                    ('sa-eng-ned', 'England', 'Netherlands', 71, 65, 'Semifinal Phase', 'Semifinal Group A'),

                    ('sb-yug-bul', 'Yugoslavia', 'Bulgaria', 140, 91, 'Semifinal Phase', 'Semifinal Group B'),
                    ('sb-swe-frg', 'Sweden', 'West Germany', 85, 95, 'Semifinal Phase', 'Semifinal Group B'),
                    ('sb-yug-swe', 'Yugoslavia', 'Sweden', 117, 86, 'Semifinal Phase', 'Semifinal Group B'),
                    ('sb-bul-frg', 'Bulgaria', 'West Germany', 93, 88, 'Semifinal Phase', 'Semifinal Group B'),
                    ('sb-frg-yug', 'West Germany', 'Yugoslavia', 101, 114, 'Semifinal Phase', 'Semifinal Group B'),
                    ('sb-swe-bul', 'Sweden', 'Bulgaria', 86, 88, 'Semifinal Phase', 'Semifinal Group B'),
                    ('sb-bul-yug', 'Bulgaria', 'Yugoslavia', 97, 74, 'Semifinal Phase', 'Semifinal Group B'),
                    ('sb-frg-swe', 'West Germany', 'Sweden', 89, 92, 'Semifinal Phase', 'Semifinal Group B'),
                    ('sb-swe-yug', 'Sweden', 'Yugoslavia', 78, 106, 'Semifinal Phase', 'Semifinal Group B'),
                    ('sb-frg-bul', 'West Germany', 'Bulgaria', 96, 105, 'Semifinal Phase', 'Semifinal Group B'),
                    ('sb-bul-swe', 'Bulgaria', 'Sweden', 85, 69, 'Semifinal Phase', 'Semifinal Group B'),
                    ('sb-yug-frg', 'Yugoslavia', 'West Germany', 116, 78, 'Semifinal Phase', 'Semifinal Group B')
                ) AS v(slug, home_name, away_name, home_score, away_score, phase, round_name)
                JOIN teams home ON home."CanonicalName" = v.home_name
                JOIN teams away ON away."CanonicalName" = v.away_name
                ON CONFLICT ("Source", "SourceGameId") DO NOTHING;

                IF (SELECT count(*) FROM games WHERE "Source" = 'Los campeonatos de Europa 1935-1995'
                    AND "SourceGameId" LIKE 'eurobasket-qualifiers-1989-%') <> 42 THEN
                    RAISE EXCEPTION 'Expected forty-two EuroBasket Qualifiers 1989 book games';
                END IF;
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Historical book data is rolled back from the pre-deployment backup.
    }
}
