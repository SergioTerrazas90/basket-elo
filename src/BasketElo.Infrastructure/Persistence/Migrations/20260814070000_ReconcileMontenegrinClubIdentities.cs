using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260814070000_ReconcileMontenegrinClubIdentities")]
public partial class ReconcileMontenegrinClubIdentities : Migration
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
                        ('77d4e618-8473-4a5a-b4fa-8b23de986d8f'::uuid, '10a6b7bd-a823-471a-abbc-d46c0130949a'::uuid, 'KK Buducnost / Buducnost VOLI'),
                        ('e0c1e1bf-12ca-4748-8504-0575b2a7a641'::uuid, '59b14d8f-eefc-480a-ba18-328eee9a9d5d'::uuid, 'BC Lovcen / Lovcen 1947'),
                        ('2f0b300f-507e-47f5-9afc-ebd36a397128'::uuid, '59b14d8f-eefc-480a-ba18-328eee9a9d5d'::uuid, 'Lovcen Cetinje / Lovcen 1947'),
                        ('d4ebe7ec-22c2-4fa0-9ea0-c6503b236241'::uuid, '59b14d8f-eefc-480a-ba18-328eee9a9d5d'::uuid, 'Lovcen / Lovcen 1947'),
                        ('2ec3f22a-ce93-402f-8d09-3d2f9673186c'::uuid, 'a2918e27-f0ce-4e91-875a-042ccf9a205f'::uuid, 'Ibar Rozaje / KK Ibar')
                    ) AS merges(source_team_id, target_team_id, description)
                LOOP
                    source_team_name := NULL;
                    target_team_name := NULL;

                    SELECT "CanonicalName" INTO source_team_name FROM teams WHERE "Id" = merge_record.source_team_id;
                    SELECT "CanonicalName" INTO target_team_name FROM teams WHERE "Id" = merge_record.target_team_id;

                    IF target_team_name IS NULL THEN
                        RAISE EXCEPTION 'Cannot reconcile Montenegrin identity %: target team is missing.', merge_record.description;
                    END IF;

                    IF source_team_name IS NULL AND NOT EXISTS (SELECT 1 FROM games WHERE "HomeTeamId" = merge_record.source_team_id OR "AwayTeamId" = merge_record.source_team_id) AND NOT EXISTS (SELECT 1 FROM rating_history WHERE "TeamId" = merge_record.source_team_id OR "OpponentTeamId" = merge_record.source_team_id) AND NOT EXISTS (SELECT 1 FROM team_ratings WHERE "TeamId" = merge_record.source_team_id) AND NOT EXISTS (SELECT 1 FROM team_aliases WHERE "TeamId" = merge_record.source_team_id) THEN
                        CONTINUE;
                    END IF;

                    IF EXISTS (SELECT 1 FROM games WHERE ("HomeTeamId" = merge_record.source_team_id AND "AwayTeamId" = merge_record.target_team_id) OR ("HomeTeamId" = merge_record.target_team_id AND "AwayTeamId" = merge_record.source_team_id)) THEN
                        RAISE EXCEPTION 'Cannot reconcile Montenegrin identity %: source and target appear in the same game.', merge_record.description;
                    END IF;

                    UPDATE teams SET "PredecessorTeamId" = NULL WHERE "Id" = merge_record.target_team_id AND "PredecessorTeamId" = merge_record.source_team_id;
                    UPDATE teams SET "SuccessorTeamId" = NULL WHERE "Id" = merge_record.target_team_id AND "SuccessorTeamId" = merge_record.source_team_id;

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
                    UPDATE rating_history SET "TeamId" = CASE WHEN "TeamId" = merge_record.source_team_id THEN merge_record.target_team_id ELSE "TeamId" END, "OpponentTeamId" = CASE WHEN "OpponentTeamId" = merge_record.source_team_id THEN merge_record.target_team_id ELSE "OpponentTeamId" END WHERE "TeamId" = merge_record.source_team_id OR "OpponentTeamId" = merge_record.source_team_id;

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
        // These historical provider identities are intentionally not split after consolidation.
    }
}
