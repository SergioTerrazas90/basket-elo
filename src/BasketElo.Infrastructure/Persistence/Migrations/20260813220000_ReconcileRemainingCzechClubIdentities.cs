using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260813220000_ReconcileRemainingCzechClubIdentities")]
public partial class ReconcileRemainingCzechClubIdentities : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            DECLARE
                merge_record record;
                source_team_name text;
                target_team_name text;
            BEGIN
                FOR merge_record IN
                    SELECT *
                    FROM (VALUES
                        ('eda19d7a-fe8e-4f80-9e48-6a69fae1472d'::uuid, '205bbe6a-43cf-4eda-81a2-986ea52351ba'::uuid, 'Spartak ZJS Brno / Brno'),
                        ('6d5a5438-1a00-4eb9-be7e-6ba30f0d64c8'::uuid, '3b0ee22e-c7c0-47c0-bb02-8d567684b9b0'::uuid, 'BK SCE Decin / Decin'),
                        ('5bc614dd-2722-4ae2-8e0b-d8d4d25e3f48'::uuid, 'dcd9ae5d-e951-410f-8177-6c6275eb87ab'::uuid, 'BHC SKP Pardubice / Pardubice'),
                        ('4f0d8a99-0f55-4313-926c-5c5c2359e6ac'::uuid, 'dafca83e-4b5d-423a-a082-f5dd9083cef5'::uuid, 'Nova Hut / NH Ostrava'),
                        ('6887650d-060c-40aa-bd8d-79b0ff195344'::uuid, '42db7c38-2bc1-4e64-9122-0128b95b2831'::uuid, 'Mlekarna Kunin / Novy Jicin'),
                        ('49e702aa-15a6-4a27-91b5-cfd999efc7ba'::uuid, '0b7135be-282a-42de-a50b-f3b870ce17d9'::uuid, 'USK Erpet Praha / USK Prague')
                    ) AS merges(source_team_id, target_team_id, description)
                LOOP
                    source_team_name := NULL;
                    target_team_name := NULL;

                    SELECT "CanonicalName" INTO source_team_name
                    FROM teams
                    WHERE "Id" = merge_record.source_team_id;

                    SELECT "CanonicalName" INTO target_team_name
                    FROM teams
                    WHERE "Id" = merge_record.target_team_id;

                    IF target_team_name IS NULL THEN
                        RAISE EXCEPTION 'Cannot reconcile Czech identity %: target team is missing.', merge_record.description;
                    END IF;

                    IF source_team_name IS NULL AND NOT EXISTS (
                        SELECT 1 FROM games
                        WHERE "HomeTeamId" = merge_record.source_team_id
                           OR "AwayTeamId" = merge_record.source_team_id
                    ) AND NOT EXISTS (
                        SELECT 1 FROM rating_history
                        WHERE "TeamId" = merge_record.source_team_id
                           OR "OpponentTeamId" = merge_record.source_team_id
                    ) AND NOT EXISTS (
                        SELECT 1 FROM team_ratings
                        WHERE "TeamId" = merge_record.source_team_id
                    ) AND NOT EXISTS (
                        SELECT 1 FROM team_aliases
                        WHERE "TeamId" = merge_record.source_team_id
                    ) THEN
                        CONTINUE;
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM games
                        WHERE ("HomeTeamId" = merge_record.source_team_id AND "AwayTeamId" = merge_record.target_team_id)
                           OR ("HomeTeamId" = merge_record.target_team_id AND "AwayTeamId" = merge_record.source_team_id)
                    ) THEN
                        RAISE EXCEPTION 'Cannot reconcile Czech identity %: source and target appear in the same game.', merge_record.description;
                    END IF;

                    DELETE FROM team_aliases duplicate_alias
                    WHERE duplicate_alias."TeamId" = merge_record.source_team_id
                      AND EXISTS (
                          SELECT 1
                          FROM team_aliases target_alias
                          WHERE target_alias."TeamId" = merge_record.target_team_id
                            AND target_alias."Source" = duplicate_alias."Source"
                            AND target_alias."SourceTeamId" = duplicate_alias."SourceTeamId"
                            AND target_alias."AliasName" = duplicate_alias."AliasName"
                      );

                    UPDATE team_aliases
                    SET "TeamId" = merge_record.target_team_id
                    WHERE "TeamId" = merge_record.source_team_id;

                    DELETE FROM model_lab_run_ratings duplicate_rating
                    WHERE duplicate_rating."TeamId" = merge_record.source_team_id
                      AND EXISTS (
                          SELECT 1
                          FROM model_lab_run_ratings target_rating
                          WHERE target_rating."TeamId" = merge_record.target_team_id
                            AND target_rating."RunId" = duplicate_rating."RunId"
                      );

                    UPDATE model_lab_run_ratings
                    SET "TeamId" = merge_record.target_team_id
                    WHERE "TeamId" = merge_record.source_team_id;

                    UPDATE model_lab_run_predictions
                    SET "HomeTeamName" = CASE
                            WHEN "HomeTeamId" = merge_record.source_team_id THEN target_team_name
                            ELSE "HomeTeamName"
                        END,
                        "AwayTeamName" = CASE
                            WHEN "AwayTeamId" = merge_record.source_team_id THEN target_team_name
                            ELSE "AwayTeamName"
                        END,
                        "HomeTeamId" = CASE
                            WHEN "HomeTeamId" = merge_record.source_team_id THEN merge_record.target_team_id
                            ELSE "HomeTeamId"
                        END,
                        "AwayTeamId" = CASE
                            WHEN "AwayTeamId" = merge_record.source_team_id THEN merge_record.target_team_id
                            ELSE "AwayTeamId"
                        END
                    WHERE "HomeTeamId" = merge_record.source_team_id
                       OR "AwayTeamId" = merge_record.source_team_id;

                    DELETE FROM rating_history duplicate_history
                    WHERE duplicate_history."TeamId" = merge_record.source_team_id
                      AND EXISTS (
                          SELECT 1
                          FROM rating_history target_history
                          WHERE target_history."TeamId" = merge_record.target_team_id
                            AND target_history."GameId" = duplicate_history."GameId"
                            AND target_history."EloPoolKey" = duplicate_history."EloPoolKey"
                            AND target_history."RulesetVersion" = duplicate_history."RulesetVersion"
                      );

                    UPDATE rating_history
                    SET "TeamId" = CASE
                            WHEN "TeamId" = merge_record.source_team_id THEN merge_record.target_team_id
                            ELSE "TeamId"
                        END,
                        "OpponentTeamId" = CASE
                            WHEN "OpponentTeamId" = merge_record.source_team_id THEN merge_record.target_team_id
                            ELSE "OpponentTeamId"
                        END
                    WHERE "TeamId" = merge_record.source_team_id
                       OR "OpponentTeamId" = merge_record.source_team_id;

                    DELETE FROM team_ratings duplicate_rating
                    WHERE duplicate_rating."TeamId" = merge_record.source_team_id
                      AND EXISTS (
                          SELECT 1
                          FROM team_ratings target_rating
                          WHERE target_rating."TeamId" = merge_record.target_team_id
                            AND target_rating."EloPoolKey" = duplicate_rating."EloPoolKey"
                            AND target_rating."RulesetVersion" = duplicate_rating."RulesetVersion"
                      );

                    UPDATE team_ratings
                    SET "TeamId" = merge_record.target_team_id
                    WHERE "TeamId" = merge_record.source_team_id;

                    UPDATE games
                    SET "HomeTeamId" = CASE
                            WHEN "HomeTeamId" = merge_record.source_team_id THEN merge_record.target_team_id
                            ELSE "HomeTeamId"
                        END,
                        "AwayTeamId" = CASE
                            WHEN "AwayTeamId" = merge_record.source_team_id THEN merge_record.target_team_id
                            ELSE "AwayTeamId"
                        END,
                        "UpdatedAtUtc" = NOW()
                    WHERE "HomeTeamId" = merge_record.source_team_id
                       OR "AwayTeamId" = merge_record.source_team_id;

                    UPDATE identity_health_check_findings
                    SET "AffectedTeamId" = CASE
                            WHEN "AffectedTeamId" = merge_record.source_team_id THEN merge_record.target_team_id
                            ELSE "AffectedTeamId"
                        END,
                        "RelatedTeamId" = CASE
                            WHEN "RelatedTeamId" = merge_record.source_team_id THEN merge_record.target_team_id
                            ELSE "RelatedTeamId"
                        END
                    WHERE "AffectedTeamId" = merge_record.source_team_id
                       OR "RelatedTeamId" = merge_record.source_team_id;

                    DELETE FROM identity_review_decisions duplicate_decision
                    WHERE (duplicate_decision."AffectedTeamId" = merge_record.source_team_id
                           OR duplicate_decision."RelatedTeamId" = merge_record.source_team_id)
                      AND EXISTS (
                          SELECT 1
                          FROM identity_review_decisions target_decision
                          WHERE target_decision."DecisionKey" = replace(
                              duplicate_decision."DecisionKey",
                              replace(merge_record.source_team_id::text, '-', ''),
                              replace(merge_record.target_team_id::text, '-', '')
                          )
                      );

                    UPDATE identity_review_decisions
                    SET "AffectedTeamId" = CASE
                            WHEN "AffectedTeamId" = merge_record.source_team_id THEN merge_record.target_team_id
                            ELSE "AffectedTeamId"
                        END,
                        "RelatedTeamId" = CASE
                            WHEN "RelatedTeamId" = merge_record.source_team_id THEN merge_record.target_team_id
                            ELSE "RelatedTeamId"
                        END,
                        "DecisionKey" = replace(
                            "DecisionKey",
                            replace(merge_record.source_team_id::text, '-', ''),
                            replace(merge_record.target_team_id::text, '-', '')
                        )
                    WHERE "AffectedTeamId" = merge_record.source_team_id
                       OR "RelatedTeamId" = merge_record.source_team_id;

                    UPDATE teams
                    SET "PredecessorTeamId" = merge_record.target_team_id
                    WHERE "PredecessorTeamId" = merge_record.source_team_id;

                    UPDATE teams
                    SET "SuccessorTeamId" = merge_record.target_team_id
                    WHERE "SuccessorTeamId" = merge_record.source_team_id;

                    DELETE FROM teams
                    WHERE "Id" = merge_record.source_team_id;
                END LOOP;

                UPDATE teams
                SET "CountryCode" = 'SK'
                WHERE "Id" IN (
                    '5e920d07-8eec-45b3-9e7d-a11ffa0f4102'::uuid,
                    'd2233ecb-b2d9-4ecd-9650-bf448efcbec7'::uuid
                )
                  AND "CountryCode" = 'CZ';

                UPDATE teams
                SET "CountryCode" = 'CZ'
                WHERE "Id" IN (
                    '9b7d01c2-1d16-4669-b11c-92a491006f03'::uuid,
                    '3362377b-1bb5-4a78-acf2-4f3b44123b3f'::uuid,
                    '1a3b1941-54cd-4732-979c-d354ffb42065'::uuid
                )
                  AND "CountryCode" = 'SK';

                UPDATE identity_health_check_findings
                SET "CountryCode" = 'SK'
                WHERE "CountryCode" = 'CZ'
                  AND "AffectedTeamId" IN (
                      '5e920d07-8eec-45b3-9e7d-a11ffa0f4102'::uuid,
                      'd2233ecb-b2d9-4ecd-9650-bf448efcbec7'::uuid
                  );

                UPDATE identity_health_check_findings
                SET "CountryCode" = 'CZ'
                WHERE "CountryCode" = 'SK'
                  AND "AffectedTeamId" IN (
                      '9b7d01c2-1d16-4669-b11c-92a491006f03'::uuid,
                      '3362377b-1bb5-4a78-acf2-4f3b44123b3f'::uuid,
                      '1a3b1941-54cd-4732-979c-d354ffb42065'::uuid
                  );
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // These identities are intentionally not split after their games and
        // aliases have been consolidated.
    }
}
