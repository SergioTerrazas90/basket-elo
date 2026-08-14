using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260813233000_ReconcileEstonianTartuRockIdentity")]
public partial class ReconcileEstonianTartuRockIdentity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            DECLARE
                source_team_id uuid := '82a9f8b0-f512-43d5-97d0-c903f9b686a2'::uuid;
                target_team_id uuid := '247420a6-8d65-46d1-9d53-c3ddcc96f7a7'::uuid;
                target_team_name text;
            BEGIN
                SELECT "CanonicalName" INTO target_team_name
                FROM teams
                WHERE "Id" = target_team_id;

                IF target_team_name IS NULL THEN
                    RAISE EXCEPTION 'Cannot reconcile Estonian identity: Tartu Ulikool target is missing.';
                END IF;

                IF NOT EXISTS (SELECT 1 FROM teams WHERE "Id" = source_team_id)
                   AND NOT EXISTS (SELECT 1 FROM games WHERE "HomeTeamId" = source_team_id OR "AwayTeamId" = source_team_id)
                   AND NOT EXISTS (SELECT 1 FROM rating_history WHERE "TeamId" = source_team_id OR "OpponentTeamId" = source_team_id)
                   AND NOT EXISTS (SELECT 1 FROM team_ratings WHERE "TeamId" = source_team_id)
                   AND NOT EXISTS (SELECT 1 FROM team_aliases WHERE "TeamId" = source_team_id) THEN
                    RETURN;
                END IF;

                IF EXISTS (
                    SELECT 1
                    FROM games
                    WHERE ("HomeTeamId" = source_team_id AND "AwayTeamId" = target_team_id)
                       OR ("HomeTeamId" = target_team_id AND "AwayTeamId" = source_team_id)
                ) THEN
                    RAISE EXCEPTION 'Cannot reconcile Estonian identity: Tartu Rock and Tartu Ulikool appear in the same game.';
                END IF;

                DELETE FROM team_aliases duplicate_alias
                WHERE duplicate_alias."TeamId" = source_team_id
                  AND EXISTS (
                      SELECT 1
                      FROM team_aliases target_alias
                      WHERE target_alias."TeamId" = target_team_id
                        AND target_alias."Source" = duplicate_alias."Source"
                        AND target_alias."SourceTeamId" = duplicate_alias."SourceTeamId"
                        AND target_alias."AliasName" = duplicate_alias."AliasName"
                  );

                UPDATE team_aliases
                SET "TeamId" = target_team_id
                WHERE "TeamId" = source_team_id;

                DELETE FROM model_lab_run_ratings duplicate_rating
                WHERE duplicate_rating."TeamId" = source_team_id
                  AND EXISTS (
                      SELECT 1
                      FROM model_lab_run_ratings target_rating
                      WHERE target_rating."TeamId" = target_team_id
                        AND target_rating."RunId" = duplicate_rating."RunId"
                  );

                UPDATE model_lab_run_ratings
                SET "TeamId" = target_team_id
                WHERE "TeamId" = source_team_id;

                UPDATE model_lab_run_predictions
                SET "HomeTeamName" = CASE
                        WHEN "HomeTeamId" = source_team_id THEN target_team_name
                        ELSE "HomeTeamName"
                    END,
                    "AwayTeamName" = CASE
                        WHEN "AwayTeamId" = source_team_id THEN target_team_name
                        ELSE "AwayTeamName"
                    END,
                    "HomeTeamId" = CASE
                        WHEN "HomeTeamId" = source_team_id THEN target_team_id
                        ELSE "HomeTeamId"
                    END,
                    "AwayTeamId" = CASE
                        WHEN "AwayTeamId" = source_team_id THEN target_team_id
                        ELSE "AwayTeamId"
                    END
                WHERE "HomeTeamId" = source_team_id
                   OR "AwayTeamId" = source_team_id;

                DELETE FROM rating_history duplicate_history
                WHERE duplicate_history."TeamId" = source_team_id
                  AND EXISTS (
                      SELECT 1
                      FROM rating_history target_history
                      WHERE target_history."TeamId" = target_team_id
                        AND target_history."GameId" = duplicate_history."GameId"
                        AND target_history."EloPoolKey" = duplicate_history."EloPoolKey"
                        AND target_history."RulesetVersion" = duplicate_history."RulesetVersion"
                  );

                UPDATE rating_history
                SET "TeamId" = CASE
                        WHEN "TeamId" = source_team_id THEN target_team_id
                        ELSE "TeamId"
                    END,
                    "OpponentTeamId" = CASE
                        WHEN "OpponentTeamId" = source_team_id THEN target_team_id
                        ELSE "OpponentTeamId"
                    END
                WHERE "TeamId" = source_team_id
                   OR "OpponentTeamId" = source_team_id;

                DELETE FROM team_ratings duplicate_rating
                WHERE duplicate_rating."TeamId" = source_team_id
                  AND EXISTS (
                      SELECT 1
                      FROM team_ratings target_rating
                      WHERE target_rating."TeamId" = target_team_id
                        AND target_rating."EloPoolKey" = duplicate_rating."EloPoolKey"
                        AND target_rating."RulesetVersion" = duplicate_rating."RulesetVersion"
                  );

                UPDATE team_ratings
                SET "TeamId" = target_team_id
                WHERE "TeamId" = source_team_id;

                UPDATE games
                SET "HomeTeamId" = CASE
                        WHEN "HomeTeamId" = source_team_id THEN target_team_id
                        ELSE "HomeTeamId"
                    END,
                    "AwayTeamId" = CASE
                        WHEN "AwayTeamId" = source_team_id THEN target_team_id
                        ELSE "AwayTeamId"
                    END,
                    "UpdatedAtUtc" = NOW()
                WHERE "HomeTeamId" = source_team_id
                   OR "AwayTeamId" = source_team_id;

                UPDATE identity_health_check_findings
                SET "AffectedTeamId" = CASE
                        WHEN "AffectedTeamId" = source_team_id THEN target_team_id
                        ELSE "AffectedTeamId"
                    END,
                    "RelatedTeamId" = CASE
                        WHEN "RelatedTeamId" = source_team_id THEN target_team_id
                        ELSE "RelatedTeamId"
                    END
                WHERE "AffectedTeamId" = source_team_id
                   OR "RelatedTeamId" = source_team_id;

                DELETE FROM identity_review_decisions duplicate_decision
                WHERE (duplicate_decision."AffectedTeamId" = source_team_id
                       OR duplicate_decision."RelatedTeamId" = source_team_id)
                  AND EXISTS (
                      SELECT 1
                      FROM identity_review_decisions target_decision
                      WHERE target_decision."DecisionKey" = replace(
                          duplicate_decision."DecisionKey",
                          replace(source_team_id::text, '-', ''),
                          replace(target_team_id::text, '-', '')
                      )
                  );

                UPDATE identity_review_decisions
                SET "AffectedTeamId" = CASE
                        WHEN "AffectedTeamId" = source_team_id THEN target_team_id
                        ELSE "AffectedTeamId"
                    END,
                    "RelatedTeamId" = CASE
                        WHEN "RelatedTeamId" = source_team_id THEN target_team_id
                        ELSE "RelatedTeamId"
                    END,
                    "DecisionKey" = replace(
                        "DecisionKey",
                        replace(source_team_id::text, '-', ''),
                        replace(target_team_id::text, '-', '')
                    )
                WHERE "AffectedTeamId" = source_team_id
                   OR "RelatedTeamId" = source_team_id;

                UPDATE teams
                SET "PredecessorTeamId" = target_team_id
                WHERE "PredecessorTeamId" = source_team_id;

                UPDATE teams
                SET "SuccessorTeamId" = target_team_id
                WHERE "SuccessorTeamId" = source_team_id;

                DELETE FROM teams
                WHERE "Id" = source_team_id;
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The historical identity is intentionally not split after its games
        // and aliases have been consolidated.
    }
}
