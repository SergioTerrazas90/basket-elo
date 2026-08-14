using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260814000000_ReconcileFinnishClubIdentities")]
public partial class ReconcileFinnishClubIdentities : Migration
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
                        ('3df55f0b-d466-4826-8b67-f8e5dd62c7f0'::uuid, '784c9870-363e-49c7-bc79-5f859a01544e'::uuid, 'Kauhajoen Karhu / Karhu Basket'),
                        ('e0ef9ade-c7c2-4da5-8683-8f248441cb43'::uuid, '11c47b8f-0719-4ee2-a4bb-b8f8a1f9ac05'::uuid, 'Tampereen Pyrinto / Pyrinto Tampere'),
                        ('6979aed7-7ab4-4760-afd8-a9e73be1e954'::uuid, 'be2792cd-d051-49ec-953b-a04dafd7f0d1'::uuid, 'KTP Kotka Basket / KTP'),
                        ('6422bed8-8bf2-4aa8-9b6c-8a84e53d833f'::uuid, '7083d354-068f-4ac6-a040-6e933391c9fc'::uuid, 'Saab Uusikaupunki / Uudenkaupungin Urheilijat')
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
                        RAISE EXCEPTION 'Cannot reconcile Finnish identity %: target team is missing.', merge_record.description;
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
                        RAISE EXCEPTION 'Cannot reconcile Finnish identity %: source and target appear in the same game.', merge_record.description;
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
                SET "CanonicalName" = 'Tampereen Pyrintö'
                WHERE "Id" = '11c47b8f-0719-4ee2-a4bb-b8f8a1f9ac05'::uuid
                  AND "CountryCode" = 'FI';

                UPDATE teams
                SET "CanonicalName" = 'KTP Basket'
                WHERE "Id" = 'be2792cd-d051-49ec-953b-a04dafd7f0d1'::uuid
                  AND "CountryCode" = 'FI';
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // These historical identities are intentionally not split after their
        // games and aliases have been consolidated.
    }
}
