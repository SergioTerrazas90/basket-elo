using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260907190000_AddEuroBasketDivisionB1985BookGames")]
public partial class AddEuroBasketDivisionB1985BookGames : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            DECLARE competition_id uuid; season_id uuid; cycle_id uuid;
            BEGIN
                SELECT "Id" INTO competition_id FROM competitions
                WHERE "Name" = 'FIBA EuroBasket Division B' AND "CountryCode" IS NULL;
                IF competition_id IS NULL THEN RAISE EXCEPTION 'FIBA EuroBasket Division B competition is missing'; END IF;
                INSERT INTO tournament_cycles ("Id", "Key", "Family", "EditionLabel", "DisplayName", "CreatedAtUtc")
                VALUES (md5('tournament-cycle:eurobasket-division-b:1985')::uuid, 'eurobasket-division-b-1985',
                        'EuroBasket Division B', '1985', 'EuroBasket Division B 1985', now()) ON CONFLICT ("Key") DO NOTHING;
                INSERT INTO seasons ("Id", "CompetitionId", "Label", "StartDateUtc", "EndDateUtc", "CreatedAtUtc")
                VALUES (md5('season:eurobasket-division-b:1985')::uuid, competition_id, '1985',
                        timestamptz '1985-01-01 00:00:00+00', timestamptz '1985-12-31 23:59:59+00', now())
                ON CONFLICT ("CompetitionId", "Label") DO NOTHING;
                SELECT "Id" INTO season_id FROM seasons WHERE "CompetitionId" = competition_id AND "Label" = '1985';
                SELECT "Id" INTO cycle_id FROM tournament_cycles WHERE "Key" = 'eurobasket-division-b-1985';
                IF cycle_id IS NULL THEN RAISE EXCEPTION 'eurobasket-division-b-1985 tournament cycle is missing'; END IF;

                INSERT INTO games
                    ("Id", "Source", "SourceGameId", "SourceUrl", "SourceSeasonKey", "SourceFetchedAtUtc",
                     "SourceRevision", "ParserVersion", "CompetitionId", "SeasonId", "TournamentCycleId",
                     "GameDateTimeUtc", "HomeTeamId", "AwayTeamId", "HomeScore", "AwayScore", "Status",
                     "CompetitionPhase", "CompetitionRound", "IsNeutralSite", "EloEligible", "EloExclusionReason",
                     "HasManualResultOverride", "IngestedAtUtc", "UpdatedAtUtc")
                SELECT md5('game:book:eurobasket-division-b-1985:' || v.slug)::uuid,
                       'Los campeonatos de Europa 1935-1995', 'eurobasket-division-b-1985-' || v.slug,
                       NULL, '1985', now(),
                       CASE WHEN v.slug IN ('qual-a-por-isl', 'qual-a-isl-sco', 'a-swe-rom', 'b-rom-bul', 'final-bel-hun')
                            THEN 'Los campeonatos de Europa 1935-1995 — Carlos Jiménez (overtime noted in source)'
                            ELSE 'Los campeonatos de Europa 1935-1995 — Carlos Jiménez' END,
                       'manual-book-photo-v1', competition_id, season_id, cycle_id,
                       timestamptz '1985-01-01 00:00:00+00', home."Id", away."Id", v.home_score, v.away_score,
                       'finished', v.phase, v.round_name, true, true, NULL, false, now(), now()
                FROM (VALUES
                    ('qual-a-nor-sco', 'Norway', 'Scotland', 103, 74, 'Qualification Round', 'Qualification Group A'),
                    ('qual-a-den-por', 'Denmark', 'Portugal', 87, 80, 'Qualification Round', 'Qualification Group A'),
                    ('qual-a-por-sco', 'Portugal', 'Scotland', 65, 56, 'Qualification Round', 'Qualification Group A'),
                    ('qual-a-nor-isl', 'Norway', 'Iceland', 84, 63, 'Qualification Round', 'Qualification Group A'),
                    ('qual-a-den-sco', 'Denmark', 'Scotland', 87, 74, 'Qualification Round', 'Qualification Group A'),
                    ('qual-a-por-isl', 'Portugal', 'Iceland', 94, 91, 'Qualification Round', 'Qualification Group A'),
                    ('qual-a-nor-por', 'Norway', 'Portugal', 78, 76, 'Qualification Round', 'Qualification Group A'),
                    ('qual-a-den-isl', 'Denmark', 'Iceland', 80, 76, 'Qualification Round', 'Qualification Group A'),
                    ('qual-a-isl-sco', 'Iceland', 'Scotland', 96, 95, 'Qualification Round', 'Qualification Group A'),
                    ('qual-a-nor-den', 'Norway', 'Denmark', 97, 78, 'Qualification Round', 'Qualification Group A'),

                    ('qual-b-bul-cyp', 'Bulgaria', 'Cyprus', 109, 55, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-aut-alg', 'Austria', 'Algeria', 91, 63, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-lux-wal', 'Luxembourg', 'Wales', 115, 66, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-alg-cyp', 'Algeria', 'Cyprus', 81, 78, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-bul-wal', 'Bulgaria', 'Wales', 98, 57, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-lux-aut', 'Luxembourg', 'Austria', 70, 69, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-aut-wal', 'Austria', 'Wales', 85, 37, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-bul-alg', 'Bulgaria', 'Algeria', 86, 50, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-cyp-lux', 'Cyprus', 'Luxembourg', 73, 68, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-alg-wal', 'Algeria', 'Wales', 73, 72, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-bul-aut', 'Bulgaria', 'Austria', 86, 65, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-lux-alg', 'Luxembourg', 'Algeria', 67, 64, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-alg-wal-2', 'Algeria', 'Wales', 83, 57, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-aut-cyp', 'Austria', 'Cyprus', 96, 82, 'Qualification Round', 'Qualification Group B'),
                    ('qual-b-bul-lux', 'Bulgaria', 'Luxembourg', 102, 57, 'Qualification Round', 'Qualification Group B'),

                    ('a-rom-tur', 'Romania', 'Turkey', 75, 70, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-swe-nor', 'Sweden', 'Norway', 74, 72, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-bel-cze', 'Belgium', 'Czechoslovakia', 65, 52, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-rom-nor', 'Romania', 'Norway', 87, 71, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-swe-bel', 'Sweden', 'Belgium', 72, 62, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-cze-tur', 'Czechoslovakia', 'Turkey', 79, 62, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-cze-nor', 'Czechoslovakia', 'Norway', 92, 82, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-swe-rom', 'Sweden', 'Romania', 71, 68, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-bel-tur', 'Belgium', 'Turkey', 69, 67, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-nor-bel', 'Norway', 'Belgium', 76, 66, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-tur-swe', 'Turkey', 'Sweden', 80, 77, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-cze-rom', 'Czechoslovakia', 'Romania', 81, 75, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-tur-nor', 'Turkey', 'Norway', 70, 68, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-cze-swe', 'Czechoslovakia', 'Sweden', 85, 75, 'Qualification Round', 'Challenge Round Group A'),
                    ('a-rom-bel', 'Romania', 'Belgium', 73, 60, 'Qualification Round', 'Challenge Round Group A'),

                    ('b-grc-hun', 'Greece', 'Hungary', 91, 87, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-bul-fin', 'Bulgaria', 'Finland', 67, 63, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-pol-eng', 'Poland', 'England', 107, 90, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-fin-eng', 'Finland', 'England', 81, 80, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-hun-pol', 'Hungary', 'Poland', 77, 76, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-bul-grc', 'Bulgaria', 'Greece', 79, 72, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-fin-grc', 'Finland', 'Greece', 78, 73, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-pol-bul', 'Poland', 'Bulgaria', 79, 66, 'Qualification Round', 'Challenge Round Group B'),
                    ('b-hun-eng', 'Hungary', 'England', 70, 68, 'Qualification Round', 'Challenge Round Group B'),

                    ('final-bel-pol', 'Belgium', 'Poland', 82, 76, 'Final Phase', 'Final Phase'),
                    ('final-cze-bul', 'Czechoslovakia', 'Bulgaria', 77, 75, 'Final Phase', 'Final Phase'),
                    ('final-hun-swe', 'Hungary', 'Sweden', 80, 79, 'Final Phase', 'Final Phase'),
                    ('final-rom-fin', 'Romania', 'Finland', 76, 74, 'Final Phase', 'Final Phase'),
                    ('final-bul-bel', 'Bulgaria', 'Belgium', 77, 69, 'Final Phase', 'Final Phase'),
                    ('final-pol-rom', 'Poland', 'Romania', 83, 81, 'Final Phase', 'Final Phase'),
                    ('final-swe-fin', 'Sweden', 'Finland', 102, 81, 'Final Phase', 'Final Phase'),
                    ('final-cze-hun', 'Czechoslovakia', 'Hungary', 77, 75, 'Final Phase', 'Final Phase'),
                    ('final-rom-bul', 'Romania', 'Bulgaria', 83, 77, 'Final Phase', 'Final Phase'),
                    ('final-pol-swe', 'Poland', 'Sweden', 103, 102, 'Final Phase', 'Final Phase'),
                    ('final-cze-fin', 'Czechoslovakia', 'Finland', 109, 88, 'Final Phase', 'Final Phase'),
                    ('final-bel-hun', 'Belgium', 'Hungary', 92, 80, 'Final Phase', 'Final Phase'),
                    ('final-cze-pol', 'Czechoslovakia', 'Poland', 106, 91, 'Final Phase', 'Final Phase'),
                    ('final-bul-swe', 'Bulgaria', 'Sweden', 93, 68, 'Final Phase', 'Final Phase'),
                    ('final-rom-hun', 'Romania', 'Hungary', 73, 66, 'Final Phase', 'Final Phase'),
                    ('final-bel-fin', 'Belgium', 'Finland', 83, 70, 'Final Phase', 'Final Phase')
                ) AS v(slug, home_name, away_name, home_score, away_score, phase, round_name)
                JOIN teams home ON home."CanonicalName" = v.home_name
                JOIN teams away ON away."CanonicalName" = v.away_name
                ON CONFLICT ("Source", "SourceGameId") DO NOTHING;

                IF (SELECT count(*) FROM games WHERE "Source" = 'Los campeonatos de Europa 1935-1995'
                    AND "SourceGameId" LIKE 'eurobasket-division-b-1985-%') <> 65 THEN
                    RAISE EXCEPTION 'Expected sixty-five EuroBasket Division B 1985 book games';
                END IF;
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Historical book data is rolled back from the pre-deployment backup.
    }
}
