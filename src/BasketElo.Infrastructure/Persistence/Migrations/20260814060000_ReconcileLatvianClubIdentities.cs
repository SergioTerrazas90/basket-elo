using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260814060000_ReconcileLatvianClubIdentities")]
public partial class ReconcileLatvianClubIdentities : Migration
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
                        ('be2a1044-acff-472e-b573-c4dfc0c1696f'::uuid, '416db3b0-a3ec-4f0d-8ab1-fe5e0c226d02'::uuid, 'BC Barons / Barons-LMT'),
                        ('08eb395e-22ba-4747-b6a8-e3ef2a55d8ce'::uuid, '416db3b0-a3ec-4f0d-8ab1-fe5e0c226d02'::uuid, 'Barons LMT / Barons-LMT'),
                        ('c15b7959-4cc5-41bd-ac63-9279d95be582'::uuid, '416db3b0-a3ec-4f0d-8ab1-fe5e0c226d02'::uuid, 'Barons Kvartals / Barons-LMT'),
                        ('4f484d6f-bbc4-42e0-99f4-655ec59420b2'::uuid, '2d5add89-4e2f-44be-88be-7c7d08e3cdca'::uuid, 'BK Livu Alus / BK Liepaja'),
                        ('fec1ac81-b61c-450e-acc9-bb9dedc1ce81'::uuid, '2d5add89-4e2f-44be-88be-7c7d08e3cdca'::uuid, 'Liepajas Lauvas / BK Liepaja'),
                        ('ffe6a6d9-200b-4e74-b35c-2d2ca7a2260b'::uuid, 'c095cf2f-9d0f-40a9-bc99-748d14598d41'::uuid, 'Gulbenes Buki / Bumerangs-Gulbene-ASK'),
                        ('eae5c22f-dda6-4df4-b2a6-973edb113922'::uuid, 'ec07dba0-1d3d-44d4-b2b6-3fc1430314e7'::uuid, 'BC Valmiera Rujiena / Valmiera-ORDO'),
                        ('b6222bb3-5a0d-4088-a74f-099f7ec304c2'::uuid, 'ec07dba0-1d3d-44d4-b2b6-3fc1430314e7'::uuid, 'Valmiera Piens / Valmiera-ORDO'),
                        ('08ac37ab-505a-465b-887d-2293761d1a09'::uuid, 'ec07dba0-1d3d-44d4-b2b6-3fc1430314e7'::uuid, 'Valmiera Lacplesa Alus / Valmiera-ORDO'),
                        ('70bb7a29-7654-40c8-be54-8ac144615911'::uuid, 'ec07dba0-1d3d-44d4-b2b6-3fc1430314e7'::uuid, 'SK Valmiera / Valmiera-ORDO')
                    ) AS merges(source_team_id, target_team_id, description)
                LOOP
                    source_team_name := NULL;
                    target_team_name := NULL;

                    SELECT "CanonicalName" INTO source_team_name FROM teams WHERE "Id" = merge_record.source_team_id;
                    SELECT "CanonicalName" INTO target_team_name FROM teams WHERE "Id" = merge_record.target_team_id;

                    IF target_team_name IS NULL THEN
                        RAISE EXCEPTION 'Cannot reconcile Latvian identity %: target team is missing.', merge_record.description;
                    END IF;

                    IF source_team_name IS NULL AND NOT EXISTS (SELECT 1 FROM games WHERE "HomeTeamId" = merge_record.source_team_id OR "AwayTeamId" = merge_record.source_team_id) AND NOT EXISTS (SELECT 1 FROM rating_history WHERE "TeamId" = merge_record.source_team_id OR "OpponentTeamId" = merge_record.source_team_id) AND NOT EXISTS (SELECT 1 FROM team_ratings WHERE "TeamId" = merge_record.source_team_id) AND NOT EXISTS (SELECT 1 FROM team_aliases WHERE "TeamId" = merge_record.source_team_id) THEN
                        CONTINUE;
                    END IF;

                    IF EXISTS (SELECT 1 FROM games WHERE ("HomeTeamId" = merge_record.source_team_id AND "AwayTeamId" = merge_record.target_team_id) OR ("HomeTeamId" = merge_record.target_team_id AND "AwayTeamId" = merge_record.source_team_id)) THEN
                        RAISE EXCEPTION 'Cannot reconcile Latvian identity %: source and target appear in the same game.', merge_record.description;
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
        // These sponsor-era and historical provider identities are intentionally not split after consolidation.
    }
}
