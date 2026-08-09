using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260808145000_ReconcileAmeriCupGsaIdentityVariants")]
public partial class ReconcileAmeriCupGsaIdentityVariants : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            DECLARE
                reconciled_count integer;
            BEGIN
                -- FIBA remains canonical. This second pass handles source identity
                -- variants that the first, exact-ID pass must not guess: GSA has
                -- historical score drift, occasional date drift, and names such as
                -- "Antigua and Barbuda" where FIBA renders "Antigua". A candidate
                -- must still be in the same season/stage, use the same unordered
                -- team pair, and be the unique nearest FIBA game within 31 days.
                CREATE TEMP TABLE fiba_americup_identity_variant_candidates ON COMMIT DROP AS
                WITH candidates AS (
                    SELECT
                        fiba_game."Id" AS fiba_id,
                        gsa_game."Id" AS gsa_id,
                        abs(fiba_game."GameDateTimeUtc"::date - gsa_game."GameDateTimeUtc"::date) AS date_distance,
                        CASE WHEN
                            (fiba_game."HomeScore" = gsa_game."HomeScore" AND fiba_game."AwayScore" = gsa_game."AwayScore")
                            OR (fiba_game."HomeScore" = gsa_game."AwayScore" AND fiba_game."AwayScore" = gsa_game."HomeScore")
                            THEN 1 ELSE 0 END AS score_match
                    FROM games fiba_game
                    JOIN games gsa_game
                      ON fiba_game."Source" = 'fiba'
                     AND gsa_game."Source" = 'global-sports-archive'
                     AND NOT gsa_game."HasManualResultOverride"
                     AND abs(fiba_game."GameDateTimeUtc"::date - gsa_game."GameDateTimeUtc"::date) <= 31
                    JOIN teams fiba_home
                      ON fiba_home."Id" = fiba_game."HomeTeamId"
                    JOIN teams fiba_away
                      ON fiba_away."Id" = fiba_game."AwayTeamId"
                    JOIN teams gsa_home
                      ON gsa_home."Id" = gsa_game."HomeTeamId"
                    JOIN teams gsa_away
                      ON gsa_away."Id" = gsa_game."AwayTeamId"
                    JOIN competitions fiba_competition
                      ON fiba_competition."Id" = fiba_game."CompetitionId"
                    JOIN competitions gsa_competition
                      ON gsa_competition."Id" = gsa_game."CompetitionId"
                    JOIN seasons fiba_season
                      ON fiba_season."Id" = fiba_game."SeasonId"
                    JOIN seasons gsa_season
                      ON gsa_season."Id" = gsa_game."SeasonId"
                     AND gsa_season."Label" = fiba_season."Label"
                    WHERE (
                        (
                            fiba_competition."Name" = 'FIBA AmeriCup'
                            AND gsa_competition."Name" = 'FIBA AmeriCup'
                        )
                        OR (
                            fiba_competition."Name" = 'FIBA AmeriCup Pre-Qualifiers'
                            AND gsa_competition."Name" = 'FIBA AmeriCup Pre-Qualifiers'
                        )
                    )
                    AND (
                        (
                            CASE WHEN lower(regexp_replace(fiba_home."CanonicalName", '[^[:alnum:]]', '', 'g')) = 'antigua'
                                 THEN 'antiguaandbarbuda'
                                 ELSE lower(regexp_replace(fiba_home."CanonicalName", '[^[:alnum:]]', '', 'g')) END
                            = lower(regexp_replace(gsa_home."CanonicalName", '[^[:alnum:]]', '', 'g'))
                            AND
                            CASE WHEN lower(regexp_replace(fiba_away."CanonicalName", '[^[:alnum:]]', '', 'g')) = 'antigua'
                                 THEN 'antiguaandbarbuda'
                                 ELSE lower(regexp_replace(fiba_away."CanonicalName", '[^[:alnum:]]', '', 'g')) END
                            = lower(regexp_replace(gsa_away."CanonicalName", '[^[:alnum:]]', '', 'g'))
                        )
                        OR (
                            CASE WHEN lower(regexp_replace(fiba_home."CanonicalName", '[^[:alnum:]]', '', 'g')) = 'antigua'
                                 THEN 'antiguaandbarbuda'
                                 ELSE lower(regexp_replace(fiba_home."CanonicalName", '[^[:alnum:]]', '', 'g')) END
                            = lower(regexp_replace(gsa_away."CanonicalName", '[^[:alnum:]]', '', 'g'))
                            AND
                            CASE WHEN lower(regexp_replace(fiba_away."CanonicalName", '[^[:alnum:]]', '', 'g')) = 'antigua'
                                 THEN 'antiguaandbarbuda'
                                 ELSE lower(regexp_replace(fiba_away."CanonicalName", '[^[:alnum:]]', '', 'g')) END
                            = lower(regexp_replace(gsa_home."CanonicalName", '[^[:alnum:]]', '', 'g'))
                        )
                    )
                ), ranked AS (
                    SELECT *,
                           rank() OVER (PARTITION BY gsa_id ORDER BY date_distance, score_match DESC) AS gsa_rank,
                           rank() OVER (PARTITION BY fiba_id ORDER BY date_distance, score_match DESC) AS fiba_rank
                    FROM candidates
                )
                SELECT fiba_id, gsa_id
                FROM ranked
                WHERE gsa_rank = 1
                  AND fiba_rank = 1;

                IF EXISTS (
                    SELECT 1
                    FROM fiba_americup_identity_variant_candidates
                    GROUP BY fiba_id
                    HAVING count(*) > 1
                ) OR EXISTS (
                    SELECT 1
                    FROM fiba_americup_identity_variant_candidates
                    GROUP BY gsa_id
                    HAVING count(*) > 1
                ) THEN
                    RAISE EXCEPTION 'AmeriCup identity-variant reconciliation is not one-to-one; no rows were removed';
                END IF;

                DELETE FROM games gsa_game
                USING fiba_americup_identity_variant_candidates candidate
                WHERE gsa_game."Id" = candidate.gsa_id
                  AND gsa_game."Source" = 'global-sports-archive'
                  AND NOT gsa_game."HasManualResultOverride";

                GET DIAGNOSTICS reconciled_count = ROW_COUNT;
                RAISE NOTICE 'Removed % AmeriCup GSA identity-variant duplicates; FIBA rows remain canonical', reconciled_count;
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Reconciled GSA rows are not restored automatically; the verified backup is the rollback path.
    }
}
