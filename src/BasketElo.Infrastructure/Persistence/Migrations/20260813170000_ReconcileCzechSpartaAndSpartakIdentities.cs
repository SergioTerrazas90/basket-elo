using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260813170000_ReconcileCzechSpartaAndSpartakIdentities")]
public partial class ReconcileCzechSpartaAndSpartakIdentities : Migration
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
                        ('d28f95e3-78ac-4ac4-ac93-101a8ca03232'::uuid, '93e1093f-6e69-422b-8978-03d3fb3bb222'::uuid, 'Spartak / Spartak Pleven'),
                        ('b6a16e4a-b108-4146-84e9-a176eb725015'::uuid, '38b116fe-0c52-434d-bbdc-7f53f8d74e80'::uuid, 'Basket Sparta / Sparta Bertrange'),
                        ('6944c055-6e0b-4f69-8357-d24cbe8b117a'::uuid, '38b116fe-0c52-434d-bbdc-7f53f8d74e80'::uuid, 'BBC Sparta / Sparta Bertrange'),
                        ('3c60bbdb-db2f-4d21-98eb-9c8df71f6fe9'::uuid, 'd4f6511b-7dd7-47de-9a54-a392c829a876'::uuid, 'Spartak Sokolovo / Sparta Prague'),
                        ('2f5e1062-7e9d-4c91-a069-42d03b7b04cd'::uuid, 'd4f6511b-7dd7-47de-9a54-a392c829a876'::uuid, 'CSA Sparta / Sparta Prague'),
                        ('4542bcef-c80a-453d-b595-dafef04433cb'::uuid, 'd4f6511b-7dd7-47de-9a54-a392c829a876'::uuid, 'Sparta CKD Praha / Sparta Prague')
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
                SET "CountryCode" = 'BG'
                WHERE "Id" = '24c1f03d-c99d-449f-b5f6-b1e095e90161'::uuid
                  AND "CountryCode" = 'CZ';

                UPDATE teams
                SET "CountryCode" = 'RU'
                WHERE "Id" = '25ec4671-1f04-4005-8d67-7fa7f9e35700'::uuid
                  AND "CountryCode" = 'CZ';
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // These identities are intentionally not split after their games and
        // aliases have been consolidated.
    }
}
