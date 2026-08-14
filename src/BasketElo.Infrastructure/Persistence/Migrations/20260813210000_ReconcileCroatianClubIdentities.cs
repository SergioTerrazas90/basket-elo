using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260813210000_ReconcileCroatianClubIdentities")]
public partial class ReconcileCroatianClubIdentities : Migration
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
                        ('124b6971-de30-437a-959d-cd6ba43bce0c'::uuid, '33035c8e-5f41-4623-9bad-e1a6a803028b'::uuid, 'Jugoplastika / Split'),
                        ('b7a42b08-b813-4fd6-ba47-50bf6cca7905'::uuid, '33035c8e-5f41-4623-9bad-e1a6a803028b'::uuid, 'Croatia Osiguranje / Split'),
                        ('ea5994d7-25ce-4d68-9c02-95c19d0b0423'::uuid, '2347f2f0-621b-4455-a9f0-ff7af806edb7'::uuid, 'DONA Dubrava / Dubrava'),
                        ('c34d3412-1063-40c9-9502-52ff4b275805'::uuid, '2347f2f0-621b-4455-a9f0-ff7af806edb7'::uuid, 'Franck Dona / Dubrava'),
                        ('0bb53160-7175-49ad-8302-75f8e5307577'::uuid, '6added9a-4e71-4900-bf76-525f2e037c05'::uuid, 'GKK Sibenik / GKK Sibenka'),
                        ('329d73ad-38ab-4f83-a692-0d2ab68f1fd9'::uuid, 'b280b761-8d01-4d94-8c8e-eb91016b75a6'::uuid, 'Sibenka RS / KK Sibenik'),
                        ('a4c2622f-d530-4629-8cb5-3b57c333357c'::uuid, '2f0341d3-a36e-4a81-aacc-e1650759837a'::uuid, 'KK Kvarner Rijeka / Kvarner')
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
                        RAISE EXCEPTION 'Cannot reconcile Croatian identity %: target team is missing.', merge_record.description;
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
                        RAISE EXCEPTION 'Cannot reconcile Croatian identity %: source and target appear in the same game.', merge_record.description;
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
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // These identities are intentionally not split after their games and
        // aliases have been consolidated.
    }
}
