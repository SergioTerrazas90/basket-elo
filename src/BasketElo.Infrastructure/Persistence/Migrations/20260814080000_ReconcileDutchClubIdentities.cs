using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260814080000_ReconcileDutchClubIdentities")]
public partial class ReconcileDutchClubIdentities : Migration
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
                        ('ea099047-b8b3-4159-8a10-f633091f3870'::uuid, '653d10dd-5ab1-4dba-9deb-e13cde4e423b'::uuid, 'Ricoh Astronauts / Amsterdam Astronauts'),
                        ('6d149e77-b876-49a6-907b-21d7daf116e9'::uuid, '653d10dd-5ab1-4dba-9deb-e13cde4e423b'::uuid, 'My Guide Amsterdam / Amsterdam Astronauts'),
                        ('7a6dea09-84bc-4e25-adf9-e9177462a5a6'::uuid, '86db5727-c764-4c5e-a751-b23bb33f34d0'::uuid, 'Donar BC / Donar Groningen'),
                        ('ad1e2f01-61ad-44d9-8f1c-d82b8bf058d8'::uuid, '86db5727-c764-4c5e-a751-b23bb33f34d0'::uuid, 'NN Donar / Donar Groningen'),
                        ('015aa024-bdd5-47ff-afeb-c07462c26ce5'::uuid, '86db5727-c764-4c5e-a751-b23bb33f34d0'::uuid, 'Hanzevast Capitals / Donar Groningen'),
                        ('72d30790-9e72-479d-9e0a-9466b629095d'::uuid, '86db5727-c764-4c5e-a751-b23bb33f34d0'::uuid, 'MPC Capitals / Donar Groningen'),
                        ('3cd58928-e528-41f7-87a2-e2f5a015e539'::uuid, '3a76c6e9-de98-47ee-9bf1-eb2a5078b1b8'::uuid, 'Levis Flamingos / Flamingos Haarlem'),
                        ('0ebb6c25-a6eb-4e8e-9c16-9df2bb985eed'::uuid, '216c0db7-c948-4540-b623-b0fe35255ee1'::uuid, 'DVSB Punch / RAAK Punch Delft'),
                        ('c425de33-527d-4dc1-9bd1-ffddd4a42412'::uuid, 'fde857eb-439d-4899-8a7f-1fde77e145da'::uuid, 'ABC Landlust / Landlust'),
                        ('fa2b4060-5d2e-4471-85d4-0d67d1fb75a8'::uuid, '4db13b65-a33b-49a6-9bc9-90a27e744fcf'::uuid, 'Libertel Dolphins / Den Bosch')
                    ) AS merges(source_team_id, target_team_id, description)
                LOOP
                    source_team_name := NULL;
                    target_team_name := NULL;

                    SELECT "CanonicalName" INTO source_team_name FROM teams WHERE "Id" = merge_record.source_team_id;
                    SELECT "CanonicalName" INTO target_team_name FROM teams WHERE "Id" = merge_record.target_team_id;

                    IF target_team_name IS NULL THEN
                        RAISE EXCEPTION 'Cannot reconcile Dutch identity %: target team is missing.', merge_record.description;
                    END IF;

                    IF source_team_name IS NULL AND NOT EXISTS (SELECT 1 FROM games WHERE "HomeTeamId" = merge_record.source_team_id OR "AwayTeamId" = merge_record.source_team_id) AND NOT EXISTS (SELECT 1 FROM rating_history WHERE "TeamId" = merge_record.source_team_id OR "OpponentTeamId" = merge_record.source_team_id) AND NOT EXISTS (SELECT 1 FROM team_ratings WHERE "TeamId" = merge_record.source_team_id) AND NOT EXISTS (SELECT 1 FROM team_aliases WHERE "TeamId" = merge_record.source_team_id) THEN
                        CONTINUE;
                    END IF;

                    IF EXISTS (SELECT 1 FROM games WHERE ("HomeTeamId" = merge_record.source_team_id AND "AwayTeamId" = merge_record.target_team_id) OR ("HomeTeamId" = merge_record.target_team_id AND "AwayTeamId" = merge_record.source_team_id)) THEN
                        RAISE EXCEPTION 'Cannot reconcile Dutch identity %: source and target appear in the same game.', merge_record.description;
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
