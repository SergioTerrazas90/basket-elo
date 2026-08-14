using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260814050000_ReconcileLithuanianClubIdentities")]
public partial class ReconcileLithuanianClubIdentities : Migration
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
                        ('d23b8086-f729-46c5-9945-f736bc4fe820'::uuid, 'cdf6b64f-b06d-440a-9b6b-9b971c4c5485'::uuid, 'BC Zalgiris / Zalgiris Kaunas'),
                        ('5308002f-f1b1-4f44-b7c3-713aead97766'::uuid, 'cdf6b64f-b06d-440a-9b6b-9b971c4c5485'::uuid, 'KK Zalgiris / Zalgiris Kaunas'),
                        ('fc7354f2-edc4-4312-be79-7c37ee51f207'::uuid, '9077760c-3e4d-4c78-ae52-70330c431f85'::uuid, 'Neptunas Klaipeda / Neptunas'),
                        ('3f5bc6ef-b174-4591-9028-4e04bb018b09'::uuid, '2b9d03f5-8f8c-4cda-9e2a-f25e753b7b2c'::uuid, 'Techasas / KK Lietkabelis'),
                        ('d0d4dd0a-1dda-4657-a7d6-6e17d762dda5'::uuid, '2b9d03f5-8f8c-4cda-9e2a-f25e753b7b2c'::uuid, 'Panevezys / KK Lietkabelis'),
                        ('a04eaac0-04dc-4339-9756-d28397dc66ed'::uuid, '2b9d03f5-8f8c-4cda-9e2a-f25e753b7b2c'::uuid, 'BC Sema / KK Lietkabelis'),
                        ('794ea32d-f872-4838-9142-bc260cc9091d'::uuid, '4626fbee-0d90-428f-9ab8-fa840071fcc7'::uuid, 'Skycop / Prienai'),
                        ('8fcd2708-d484-40be-85dc-4ed9893f9710'::uuid, '4626fbee-0d90-428f-9ab8-fa840071fcc7'::uuid, 'CBet / Prienai'),
                        ('24fe8d16-8469-49cf-9cc7-2696103604d6'::uuid, 'c86bf5c6-26d3-4e95-a5b1-0adf0dbe371f'::uuid, 'Alita / BC Alita'),
                        ('42d40ea4-fab4-45f3-a2ac-5c9513cd8d15'::uuid, '14c7f4d3-e7e0-41e9-ad9f-ea8e54b7f478'::uuid, 'Alytus / Alytus Alita'),
                        ('d768573a-e202-4460-b755-a7a2c4776d6a'::uuid, 'dda1fe26-89fc-4886-8efa-0e636b705b7c'::uuid, 'Aisciai/Atletas / Aisciai Kaunas'),
                        ('aa6f5f96-f324-45f3-8bb2-7c1250151b88'::uuid, 'dda1fe26-89fc-4886-8efa-0e636b705b7c'::uuid, 'Aisciai / Aisciai Kaunas'),
                        ('e90fa23a-b3a8-47a1-aa18-161ae063262b'::uuid, 'dda1fe26-89fc-4886-8efa-0e636b705b7c'::uuid, 'KK Baltai / Aisciai Kaunas'),
                        ('5ba2a4ef-bf29-49c4-a8cb-25dbf3439466'::uuid, '39d5fecf-6a4e-404b-88c9-35fdb8ddf107'::uuid, 'Arvi/Suduva / Suduva'),
                        ('1b7e72fd-cc8e-44e6-8280-bbd38862d098'::uuid, '39d5fecf-6a4e-404b-88c9-35fdb8ddf107'::uuid, 'Suduva-Mantinga / Suduva'),
                        ('0a03230a-bec9-4113-ae3a-239cbd094c6e'::uuid, 'c0c40dbe-7dd4-4efb-bf9c-a77a1367e3c8'::uuid, 'Kedainiai Triobet / Nevezis'),
                        ('4a29ef47-0bc4-4eb0-a1c3-37242fbf0fee'::uuid, 'e5d025fe-3eb8-4bf1-8b98-d09aa1fcf117'::uuid, 'Perlas / Perlas Vilnius')
                    ) AS merges(source_team_id, target_team_id, description)
                LOOP
                    SELECT "CanonicalName" INTO target_team_name FROM teams WHERE "Id" = merge_record.target_team_id;
                    IF target_team_name IS NULL THEN
                        RAISE EXCEPTION 'Cannot reconcile Lithuanian identity %: target team is missing.', merge_record.description;
                    END IF;

                    IF EXISTS (SELECT 1 FROM games WHERE ("HomeTeamId" = merge_record.source_team_id AND "AwayTeamId" = merge_record.target_team_id) OR ("HomeTeamId" = merge_record.target_team_id AND "AwayTeamId" = merge_record.source_team_id)) THEN
                        RAISE EXCEPTION 'Cannot reconcile Lithuanian identity %: source and target appear in the same game.', merge_record.description;
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
