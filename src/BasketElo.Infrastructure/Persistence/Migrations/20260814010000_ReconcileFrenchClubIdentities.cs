using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260814010000_ReconcileFrenchClubIdentities")]
public partial class ReconcileFrenchClubIdentities : Migration
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
                        ('61f0b921-01d0-4469-8e07-461dd9e4ce57'::uuid, 'a1025c08-686f-43a8-a9f1-467c48a23401'::uuid, 'SLUC Nancy / Nancy'),
                        ('88d08b80-2152-49ab-9912-47897e3ae9fe'::uuid, 'a1025c08-686f-43a8-a9f1-467c48a23401'::uuid, 'SLUC Basket / Nancy'),
                        ('888c5caf-12a9-4bbc-99dc-c592a27ba5d4'::uuid, '2e051d2c-cbfe-4025-9003-f1ebc22b6db5'::uuid, 'AS Monaco / Monaco'),
                        ('3a6e79a6-ba0b-462d-85a5-d1bd9dfd37f9'::uuid, 'd82ef571-8370-49b2-97df-b72669467820'::uuid, 'STB Le Havre / Le Havre'),
                        ('9083b5d5-56f6-44ac-ad84-6b07af614395'::uuid, '6b738522-e993-47bb-9305-b2eb7d991852'::uuid, 'JA Vichy / Vichy'),
                        ('a7fd1121-1e35-4d43-99cd-3a7dc10b0726'::uuid, '6b738522-e993-47bb-9305-b2eb7d991852'::uuid, 'JA Vichy Auvergne / Vichy'),
                        ('4770ea34-c0d0-47d0-ac4c-4a0900322e6c'::uuid, '4923538d-e52d-4612-a37f-32783e0e1719'::uuid, 'Saint-Quentin / Saint Quentin'),
                        ('1aa5ed07-bf8b-4f6c-a8f3-57d72ce716a5'::uuid, 'c3336457-55bb-4af5-9eb2-b2d521bd4b12'::uuid, 'CEP Lorient / Lorient'),
                        ('98ffd65f-c17d-4f0f-bc3f-6400b776fba7'::uuid, 'e85b9ca4-b15b-4aa8-ada4-8a237a4bdcf4'::uuid, 'AS Denain Voltaire / Denain-Voltaire'),
                        ('c370680f-2f4b-415f-9f3d-5f9eab14283f'::uuid, 'e85b9ca4-b15b-4aa8-ada4-8a237a4bdcf4'::uuid, 'ASC Denain Voltaire / Denain-Voltaire'),
                        ('ac70b728-6b1d-4ab5-82d7-002a1640fc77'::uuid, '43d7010f-7b19-4394-b469-aef3e85d74f4'::uuid, 'ESM Challans / Challans'),
                        ('dd9d74d2-2693-4b40-b54d-6e129b2e5136'::uuid, '43d7010f-7b19-4394-b469-aef3e85d74f4'::uuid, 'Challans Basket Club Vendee / Challans'),
                        ('b30a75eb-dc8b-4067-aa06-dbb88d6d23c8'::uuid, '2ff34932-b2d6-40b2-b6b3-6be6b0c5b772'::uuid, 'Élan Béarnais / Pau-Orthez'),
                        ('a94d6786-0358-441f-a01c-16df253fb2e2'::uuid, 'aba8f2e8-bd87-4af4-b619-5b4c29abc956'::uuid, 'BCM Gravelines / Gravelines-Dunkerque'),
                        ('619f4d0b-78da-490b-8eae-cb3edd525219'::uuid, 'b8eb0360-068f-4741-9d97-0fcd26cf8143'::uuid, 'FC Mulhouse Basket / Mulhouse'),
                        ('b7735942-6bed-4dfb-82ae-28d3e59bff72'::uuid, '0068552c-ccd8-448a-8a58-f73ad576a333'::uuid, 'ASVEL / Lyon-Villeurbanne'),
                        ('b56c39b2-3c15-4378-b4e8-e96ad549ce4a'::uuid, 'c4668189-aeed-4873-a492-b35223eb8f40'::uuid, 'Lyon Cro / Lyon CRO'),
                        ('3a9787a7-ba83-4db1-b5af-1a2ae7f8c4f7'::uuid, '88690f15-9883-425a-bf17-7da15180d4ed'::uuid, 'Straßburg IG / Strasbourg IG'),
                        ('3eb57738-989d-42d8-8f0e-b485ba230805'::uuid, 'bf1d44ec-75c7-4999-a36d-7e7d71cf09c7'::uuid, 'CAEN BC / Caen'),
                        ('43e29ec1-8cf3-4f12-9ae5-bc0fb65e9013'::uuid, '60cc987c-2edf-4e06-864b-92620cd3798a'::uuid, 'Montpellier Basket / Montpellier'),
                        ('03d1f2fb-4cba-4b63-bf64-262780d35295'::uuid, 'ed57e4d2-cf44-41a9-9d53-c0db7c495069'::uuid, 'Andrézieux / ALS Basket Andrezieux-Boutheon'),
                        ('e52f2a90-d82b-4b72-b2ca-5af06ee18a60'::uuid, '770e46fd-3d4e-49aa-80eb-b59eef9158b0'::uuid, 'Roanne Basket / Roanne')
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
                        RAISE EXCEPTION 'Cannot reconcile French identity %: target team is missing.', merge_record.description;
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
                        RAISE EXCEPTION 'Cannot reconcile French identity %: source and target appear in the same game.', merge_record.description;
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
        // These historical identities are intentionally not split after their
        // games and aliases have been consolidated.
    }
}
