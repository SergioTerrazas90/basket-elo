using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260814041000_ReconcileRemainingItalianClubIdentities")]
public partial class ReconcileRemainingItalianClubIdentities : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            DECLARE
                merge_record record;
                target_team_name text;
            BEGIN
                FOR merge_record IN
                    SELECT *
                    FROM (VALUES
                        ('61ec0db7-d353-4794-b07f-fc15475d91bc'::uuid, '065611de-ea3a-4cbd-aee2-cd0c4865cdff'::uuid, 'Germani Brescia Leonessa / Brescia'),
                        ('85660096-7b3b-4f4f-b8f4-864654d71dce'::uuid, '1487f987-935c-4613-a5bd-b49d053a06c7'::uuid, 'Eurocellulari Roseto / BT Roseto'),
                        ('07b8191f-2daf-4261-8456-0c78afde88e1'::uuid, '1487f987-935c-4613-a5bd-b49d053a06c7'::uuid, 'Roseto Basket / BT Roseto')
                    ) AS merges(source_team_id, target_team_id, description)
                LOOP
                    SELECT "CanonicalName" INTO target_team_name FROM teams WHERE "Id" = merge_record.target_team_id;
                    IF target_team_name IS NULL THEN
                        RAISE EXCEPTION 'Cannot reconcile Italian identity %: target team is missing.', merge_record.description;
                    END IF;

                    IF EXISTS (SELECT 1 FROM games WHERE ("HomeTeamId" = merge_record.source_team_id AND "AwayTeamId" = merge_record.target_team_id) OR ("HomeTeamId" = merge_record.target_team_id AND "AwayTeamId" = merge_record.source_team_id)) THEN
                        RAISE EXCEPTION 'Cannot reconcile Italian identity %: source and target appear in the same game.', merge_record.description;
                    END IF;

                    DELETE FROM team_aliases duplicate_alias
                    WHERE duplicate_alias."TeamId" = merge_record.source_team_id
                      AND EXISTS (SELECT 1 FROM team_aliases target_alias WHERE target_alias."TeamId" = merge_record.target_team_id AND target_alias."Source" = duplicate_alias."Source" AND target_alias."SourceTeamId" = duplicate_alias."SourceTeamId" AND target_alias."AliasName" = duplicate_alias."AliasName");
                    UPDATE team_aliases SET "TeamId" = merge_record.target_team_id WHERE "TeamId" = merge_record.source_team_id;

                    DELETE FROM model_lab_run_ratings duplicate_rating
                    WHERE duplicate_rating."TeamId" = merge_record.source_team_id
                      AND EXISTS (SELECT 1 FROM model_lab_run_ratings target_rating WHERE target_rating."TeamId" = merge_record.target_team_id AND target_rating."RunId" = duplicate_rating."RunId");
                    UPDATE model_lab_run_ratings SET "TeamId" = merge_record.target_team_id WHERE "TeamId" = merge_record.source_team_id;

                    UPDATE model_lab_run_predictions
                    SET "HomeTeamName" = CASE WHEN "HomeTeamId" = merge_record.source_team_id THEN target_team_name ELSE "HomeTeamName" END,
                        "AwayTeamName" = CASE WHEN "AwayTeamId" = merge_record.source_team_id THEN target_team_name ELSE "AwayTeamName" END,
                        "HomeTeamId" = CASE WHEN "HomeTeamId" = merge_record.source_team_id THEN merge_record.target_team_id ELSE "HomeTeamId" END,
                        "AwayTeamId" = CASE WHEN "AwayTeamId" = merge_record.source_team_id THEN merge_record.target_team_id ELSE "AwayTeamId" END
                    WHERE "HomeTeamId" = merge_record.source_team_id OR "AwayTeamId" = merge_record.source_team_id;

                    DELETE FROM rating_history duplicate_history
                    WHERE duplicate_history."TeamId" = merge_record.source_team_id
                      AND EXISTS (SELECT 1 FROM rating_history target_history WHERE target_history."TeamId" = merge_record.target_team_id AND target_history."GameId" = duplicate_history."GameId" AND target_history."EloPoolKey" = duplicate_history."EloPoolKey" AND target_history."RulesetVersion" = duplicate_history."RulesetVersion");
                    UPDATE rating_history
                    SET "TeamId" = CASE WHEN "TeamId" = merge_record.source_team_id THEN merge_record.target_team_id ELSE "TeamId" END,
                        "OpponentTeamId" = CASE WHEN "OpponentTeamId" = merge_record.source_team_id THEN merge_record.target_team_id ELSE "OpponentTeamId" END
                    WHERE "TeamId" = merge_record.source_team_id OR "OpponentTeamId" = merge_record.source_team_id;

                    DELETE FROM team_ratings duplicate_rating
                    WHERE duplicate_rating."TeamId" = merge_record.source_team_id
                      AND EXISTS (SELECT 1 FROM team_ratings target_rating WHERE target_rating."TeamId" = merge_record.target_team_id AND target_rating."EloPoolKey" = duplicate_rating."EloPoolKey" AND target_rating."RulesetVersion" = duplicate_rating."RulesetVersion");
                    UPDATE team_ratings SET "TeamId" = merge_record.target_team_id WHERE "TeamId" = merge_record.source_team_id;

                    UPDATE games
                    SET "HomeTeamId" = CASE WHEN "HomeTeamId" = merge_record.source_team_id THEN merge_record.target_team_id ELSE "HomeTeamId" END,
                        "AwayTeamId" = CASE WHEN "AwayTeamId" = merge_record.source_team_id THEN merge_record.target_team_id ELSE "AwayTeamId" END,
                        "UpdatedAtUtc" = NOW()
                    WHERE "HomeTeamId" = merge_record.source_team_id OR "AwayTeamId" = merge_record.source_team_id;

                    UPDATE identity_health_check_findings
                    SET "AffectedTeamId" = CASE WHEN "AffectedTeamId" = merge_record.source_team_id THEN merge_record.target_team_id ELSE "AffectedTeamId" END,
                        "RelatedTeamId" = CASE WHEN "RelatedTeamId" = merge_record.source_team_id THEN merge_record.target_team_id ELSE "RelatedTeamId" END
                    WHERE "AffectedTeamId" = merge_record.source_team_id OR "RelatedTeamId" = merge_record.source_team_id;

                    UPDATE teams SET "PredecessorTeamId" = merge_record.target_team_id WHERE "PredecessorTeamId" = merge_record.source_team_id AND "Id" <> merge_record.target_team_id;
                    UPDATE teams SET "SuccessorTeamId" = merge_record.target_team_id WHERE "SuccessorTeamId" = merge_record.source_team_id AND "Id" <> merge_record.target_team_id;
                    DELETE FROM teams WHERE "Id" = merge_record.source_team_id;
                END LOOP;
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // These provider identities are intentionally not split after their games and aliases have been consolidated.
    }
}
