using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260814020000_ReconcileGermanClubIdentities")]
public partial class ReconcileGermanClubIdentities : Migration
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
                        ('6fece885-ea7e-4005-8e78-b718f0202c93'::uuid, 'd805a426-6614-4c83-b94c-e43c1a6587f5'::uuid, 'BG Bramsche / TuS Bramsche'),
                        ('e808f51d-e0bd-45cc-b39c-22fdb4551d0b'::uuid, 'd805a426-6614-4c83-b94c-e43c1a6587f5'::uuid, 'BG Bramsche/Osnabrück / TuS Bramsche'),
                        ('eb3072c1-cc82-4357-8c9b-d055bc85afeb'::uuid, '28ab7930-39a6-44e7-8fa1-749dccfb7ba1'::uuid, 'ASC Göttingen 1846 / ASC 46 Göttingen'),
                        ('7b1fde29-99ec-4e57-95e0-32f7093b8e5e'::uuid, '4138b166-c813-40cf-a4b1-b094c473e0ae'::uuid, 'Galatasaray Köln / BSC Saturn Köln'),
                        ('11d6b9bf-285c-4eac-aab7-06a9fd2ec77d'::uuid, 'aa70e1c5-a3b3-447e-a151-54f2a3954bb3'::uuid, 'Brose Baskets / Bamberg'),
                        ('82cfc79c-34fd-4477-b3e1-222523004080'::uuid, 'aa70e1c5-a3b3-447e-a151-54f2a3954bb3'::uuid, 'GHP Bamberg / Bamberg'),
                        ('f1b1a7a1-39d3-43a5-8e2d-7e1b4f04c7e9'::uuid, '82d614f9-3ab0-40a4-9238-febcdb470d64'::uuid, 'SG Braunschweig / Basketball Löwen Braunschweig'),
                        ('3b27b248-2755-4ae9-b3f5-689947f0ab6a'::uuid, '21ebc691-d28d-4d2a-a8ea-a623d0820609'::uuid, 'TSV Bayer 04 / Bayer Giants Leverkusen'),
                        ('56ac1a30-c8c6-4223-b7d3-5959d91dcff0'::uuid, '7b93da27-caf9-4dbe-a211-10b66a5f1275'::uuid, 'Telekom Baskets Bonn / Bonn'),
                        ('119e12c2-18c8-4cb2-bdea-e82e884407f8'::uuid, '5dd545c4-5fcd-4654-8786-a92eea5d3c34'::uuid, 'SSV Hagen / Brandt Hagen'),
                        ('7e46db45-a6e9-41b6-9f17-e6494737f01b'::uuid, 'b68ba44d-7ff9-4e37-8fef-96ad61c254e0'::uuid, 'EnBW Ludwigsburg / Ludwigsburg'),
                        ('1f38a0ca-9c50-4cba-833a-057e7a228d86'::uuid, 'b68ba44d-7ff9-4e37-8fef-96ad61c254e0'::uuid, 'BG Ludwigsburg e.V. / Ludwigsburg'),
                        ('98a6f831-0b4d-4f1b-be62-7b32a06941f8'::uuid, '2a2a13a5-28a5-46f7-b2e9-90176fc3645a'::uuid, 'USC Heidelberg / Heidelberg'),
                        ('1f089e9e-e310-4389-b812-cd3efb5c844d'::uuid, '386c5bc8-148a-48d8-8880-2fd158f05f37'::uuid, 'MTV Wölfenbüttel / MTV Wolfenbüttel'),
                        ('85d83c63-7610-4e6b-ae75-9f60ed9f2894'::uuid, 'ab6de6c1-912d-414f-aa89-ba4f5fadcc48'::uuid, 'Mitteldeutscher BC / Syntainics MBC'),
                        ('9fb54bc3-5419-4548-b64f-15180f37a065'::uuid, '898f7027-5f2c-43b3-9472-ab2b43509cde'::uuid, 'Ratiopharm / Ulm'),
                        ('8f0c31c9-c617-4197-9a6f-b8372f6546e7'::uuid, '6f10d09c-f06f-45a1-a0d5-9507303d4244'::uuid, 'RheinEnergie Köln / Köln 99ers'),
                        ('f73507f8-b1e6-45d0-a7ee-246e60637ec0'::uuid, 'fd28b7e4-de03-4df0-af53-29181313add3'::uuid, 'Rhondorf / TATAMI Rhöndorf'),
                        ('600192a3-cd8e-437b-b2d6-85099ef530c7'::uuid, 'fd28b7e4-de03-4df0-af53-29181313add3'::uuid, 'Tatami Rhöndorf / TATAMI Rhöndorf'),
                        ('6522b690-1afe-4af8-85c7-9e1ecb096d53'::uuid, '84f65499-7356-44ed-98d8-f81e3b1105c9'::uuid, 'TSK Würzburg / Wurzburg'),
                        ('fbfcda56-5e72-4794-a6cb-ab698bd8c6fd'::uuid, '84f65499-7356-44ed-98d8-f81e3b1105c9'::uuid, 'DJK S.Oliver / Wurzburg'),
                        ('ee023258-9638-4656-9272-1622d367a774'::uuid, '5b0f685c-8a72-4d40-9377-7157ae508e17'::uuid, 'Avitos / Giessen'),
                        ('a5e06a68-4cf2-43e2-8989-8d5d1bd96bc9'::uuid, '82d614f9-3ab0-40a4-9238-febcdb470d64'::uuid, 'Energy Braunschweig / Basketball Löwen Braunschweig'),
                        ('beaab369-9a81-4f7f-a9a3-58c777c47d99'::uuid, '00caf001-f9f4-4405-8bca-2ecb8a750129'::uuid, 'TBB Trier / VET-CONCEPT Gladiators Trier'),
                        ('10e01f6c-9985-4873-9d11-9f6857e97f69'::uuid, 'e3455fe8-9fff-498f-ba47-6292bfb03461'::uuid, 'Nikol Fert-G / Nikol Fert')
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
                        RAISE EXCEPTION 'Cannot reconcile German identity %: target team is missing.', merge_record.description;
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
                        RAISE EXCEPTION 'Cannot reconcile German identity %: source and target appear in the same game.', merge_record.description;
                    END IF;

                    UPDATE teams
                    SET "PredecessorTeamId" = NULL
                    WHERE "Id" = merge_record.target_team_id
                      AND "PredecessorTeamId" = merge_record.source_team_id;

                    UPDATE teams
                    SET "SuccessorTeamId" = NULL
                    WHERE "Id" = merge_record.target_team_id
                      AND "SuccessorTeamId" = merge_record.source_team_id;

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
                    WHERE "PredecessorTeamId" = merge_record.source_team_id
                      AND "Id" <> merge_record.target_team_id;

                    UPDATE teams
                    SET "SuccessorTeamId" = merge_record.target_team_id
                    WHERE "SuccessorTeamId" = merge_record.source_team_id
                      AND "Id" <> merge_record.target_team_id;

                    DELETE FROM teams
                    WHERE "Id" = merge_record.source_team_id;
                END LOOP;

                UPDATE teams
                SET "CountryCode" = 'MK'
                WHERE "Id" = 'e3455fe8-9fff-498f-ba47-6292bfb03461'::uuid
                  AND upper(coalesce("CountryCode", '')) IN ('DE', 'DEU', 'GER');
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // These historical identities are intentionally not split after their
        // games and aliases have been consolidated.
    }
}
