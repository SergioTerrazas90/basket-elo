using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260814090000_ReconcilePolishClubIdentities")]
public partial class ReconcilePolishClubIdentities : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            DECLARE merge_record record; target_team_name text;
            BEGIN
                FOR merge_record IN SELECT * FROM (VALUES
                    ('4d079c3d-83c9-46a2-8a78-5b59c6d07a94'::uuid,'082c907a-a4d0-4821-adea-d2d288cb2de8'::uuid,'Anwil / Anwil Wloclawek'),
                    ('208e6e0d-c89b-4270-84fc-13c9c2617c63'::uuid,'082c907a-a4d0-4821-adea-d2d288cb2de8'::uuid,'Nobiles Wloclawek / Anwil Wloclawek'),
                    ('d0134b15-3402-43e5-bfed-83bdd12ac110'::uuid,'3b195f06-e676-4c49-89e1-91a3198e7d07'::uuid,'Prokom Trefl / Prokom Trefl Sopot'),
                    ('668f4d40-31ae-4faf-aa76-006c031f1097'::uuid,'59cae47c-d6bd-4d28-a38b-739c5404084b'::uuid,'Idea Slask / Slask Wroclaw'),
                    ('855fdbba-1963-42b3-9b7e-5f4dd5bc7399'::uuid,'59cae47c-d6bd-4d28-a38b-739c5404084b'::uuid,'Slask-ESKA / Slask Wroclaw'),
                    ('dad69e6a-49fe-4e1f-aea1-c0f0b5680dcd'::uuid,'59cae47c-d6bd-4d28-a38b-739c5404084b'::uuid,'ZEPTER IDEA / Slask Wroclaw'),
                    ('ea96def3-01f2-40c5-b62d-e28a5eff0663'::uuid,'59cae47c-d6bd-4d28-a38b-739c5404084b'::uuid,'Śląsk Wrocław / Slask Wroclaw'),
                    ('95a9622e-dd04-45ee-be44-55b3c30de052'::uuid,'a81f3ab7-395f-4c20-9ce1-49a39e765aa3'::uuid,'Turów / Turow Zgorzelec'),
                    ('1263ed35-a81a-4cca-a245-a063389a138a'::uuid,'7dd0a2e2-038b-4b13-8775-158650842f66'::uuid,'Polonia Warbud / Polonia Warszawa'),
                    ('c909e875-7894-4696-a363-e77c41c956a1'::uuid,'7dd0a2e2-038b-4b13-8775-158650842f66'::uuid,'Polonia Warsaw / Polonia Warszawa'),
                    ('1ecc8181-32ed-4a3f-9542-f3bb46a4c444'::uuid,'7dd0a2e2-038b-4b13-8775-158650842f66'::uuid,'KKS Polonia / Polonia Warszawa'),
                    ('6db2ae48-b0f9-4306-a332-3baf16345046'::uuid,'7dd0a2e2-038b-4b13-8775-158650842f66'::uuid,'Polonia-Parte / Polonia Warszawa'),
                    ('c164bb5a-48f8-4af7-9e99-4eda124d00cb'::uuid,'3366cb41-4f15-4bf2-b4de-3b462e1b1e57'::uuid,'Wisła Kraków / GTS Wisla Krakow')
                ) AS merges(source_team_id,target_team_id,description) LOOP
                    SELECT "CanonicalName" INTO target_team_name FROM teams WHERE "Id"=merge_record.target_team_id;
                    IF target_team_name IS NULL THEN RAISE EXCEPTION 'Cannot reconcile Polish identity %: target team is missing.',merge_record.description; END IF;
                    IF EXISTS (SELECT 1 FROM games WHERE ("HomeTeamId"=merge_record.source_team_id AND "AwayTeamId"=merge_record.target_team_id) OR ("HomeTeamId"=merge_record.target_team_id AND "AwayTeamId"=merge_record.source_team_id)) THEN RAISE EXCEPTION 'Cannot reconcile Polish identity %: source and target appear in the same game.',merge_record.description; END IF;
                    UPDATE teams SET "PredecessorTeamId"=NULL WHERE "Id"=merge_record.target_team_id AND "PredecessorTeamId"=merge_record.source_team_id;
                    UPDATE teams SET "SuccessorTeamId"=NULL WHERE "Id"=merge_record.target_team_id AND "SuccessorTeamId"=merge_record.source_team_id;
                    DELETE FROM team_aliases s WHERE s."TeamId"=merge_record.source_team_id AND EXISTS (SELECT 1 FROM team_aliases t WHERE t."TeamId"=merge_record.target_team_id AND t."Source"=s."Source" AND t."SourceTeamId"=s."SourceTeamId" AND t."AliasName"=s."AliasName");
                    UPDATE team_aliases SET "TeamId"=merge_record.target_team_id WHERE "TeamId"=merge_record.source_team_id;
                    DELETE FROM model_lab_run_ratings s WHERE s."TeamId"=merge_record.source_team_id AND EXISTS (SELECT 1 FROM model_lab_run_ratings t WHERE t."TeamId"=merge_record.target_team_id AND t."RunId"=s."RunId");
                    UPDATE model_lab_run_ratings SET "TeamId"=merge_record.target_team_id WHERE "TeamId"=merge_record.source_team_id;
                    UPDATE model_lab_run_predictions SET "HomeTeamName"=CASE WHEN "HomeTeamId"=merge_record.source_team_id THEN target_team_name ELSE "HomeTeamName" END,"AwayTeamName"=CASE WHEN "AwayTeamId"=merge_record.source_team_id THEN target_team_name ELSE "AwayTeamName" END,"HomeTeamId"=CASE WHEN "HomeTeamId"=merge_record.source_team_id THEN merge_record.target_team_id ELSE "HomeTeamId" END,"AwayTeamId"=CASE WHEN "AwayTeamId"=merge_record.source_team_id THEN merge_record.target_team_id ELSE "AwayTeamId" END WHERE "HomeTeamId"=merge_record.source_team_id OR "AwayTeamId"=merge_record.source_team_id;
                    DELETE FROM rating_history s WHERE s."TeamId"=merge_record.source_team_id AND EXISTS (SELECT 1 FROM rating_history t WHERE t."TeamId"=merge_record.target_team_id AND t."GameId"=s."GameId" AND t."EloPoolKey"=s."EloPoolKey" AND t."RulesetVersion"=s."RulesetVersion");
                    UPDATE rating_history SET "TeamId"=CASE WHEN "TeamId"=merge_record.source_team_id THEN merge_record.target_team_id ELSE "TeamId" END,"OpponentTeamId"=CASE WHEN "OpponentTeamId"=merge_record.source_team_id THEN merge_record.target_team_id ELSE "OpponentTeamId" END WHERE "TeamId"=merge_record.source_team_id OR "OpponentTeamId"=merge_record.source_team_id;
                    DELETE FROM team_ratings s WHERE s."TeamId"=merge_record.source_team_id AND EXISTS (SELECT 1 FROM team_ratings t WHERE t."TeamId"=merge_record.target_team_id AND t."EloPoolKey"=s."EloPoolKey" AND t."RulesetVersion"=s."RulesetVersion");
                    UPDATE team_ratings SET "TeamId"=merge_record.target_team_id WHERE "TeamId"=merge_record.source_team_id;
                    UPDATE games SET "HomeTeamId"=CASE WHEN "HomeTeamId"=merge_record.source_team_id THEN merge_record.target_team_id ELSE "HomeTeamId" END,"AwayTeamId"=CASE WHEN "AwayTeamId"=merge_record.source_team_id THEN merge_record.target_team_id ELSE "AwayTeamId" END,"UpdatedAtUtc"=NOW() WHERE "HomeTeamId"=merge_record.source_team_id OR "AwayTeamId"=merge_record.source_team_id;
                    UPDATE identity_health_check_findings SET "AffectedTeamId"=CASE WHEN "AffectedTeamId"=merge_record.source_team_id THEN merge_record.target_team_id ELSE "AffectedTeamId" END,"RelatedTeamId"=CASE WHEN "RelatedTeamId"=merge_record.source_team_id THEN merge_record.target_team_id ELSE "RelatedTeamId" END WHERE "AffectedTeamId"=merge_record.source_team_id OR "RelatedTeamId"=merge_record.source_team_id;
                    UPDATE teams SET "PredecessorTeamId"=merge_record.target_team_id WHERE "PredecessorTeamId"=merge_record.source_team_id AND "Id"<>merge_record.target_team_id;
                    UPDATE teams SET "SuccessorTeamId"=merge_record.target_team_id WHERE "SuccessorTeamId"=merge_record.source_team_id AND "Id"<>merge_record.target_team_id;
                    DELETE FROM teams WHERE "Id"=merge_record.source_team_id;
                END LOOP;
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) { }
}
