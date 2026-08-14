using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260814140000_ReconcilePortugueseClubIdentities")]
public partial class ReconcilePortugueseClubIdentities : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$ DECLARE m record; n text; BEGIN
            FOR m IN SELECT * FROM (VALUES
            ('59a3004f-0848-41ef-965e-6cd537d8e360'::uuid,'748aee7c-7d5a-4f7f-aa6a-134ef05bb835'::uuid,'Académica Coimbra / Associacao Academica de Coimbra'),
            ('d1025f19-321e-4d52-a342-48483aa2b8e2'::uuid,'687604c9-fe05-4b92-b612-fdfc3e8dbcab'::uuid,'Ovarense Aerosoles / AD Ovarense'),
            ('0b676c6f-ccae-4cbb-aec3-35baf94507a2'::uuid,'766f3b67-a38d-41e2-a66f-06a3d41209aa'::uuid,'CA Sintra PM / Clube Atlético'),
            ('d4a8e3b9-a3f6-4e38-b1e5-541855b1acc9'::uuid,'766f3b67-a38d-41e2-a66f-06a3d41209aa'::uuid,'Queluz Sintra PM / Clube Atlético'),
            ('13baa75b-ea93-42a6-9967-70205c25a191'::uuid,'ce8bfc5e-2814-464e-914f-574e61c3f11a'::uuid,'CR Estrelas / Estrelas da Avenida'),
            ('1e35e423-0089-4fb9-a948-98176f840f48'::uuid,'68ae827e-31f1-44cb-b358-079e12911913'::uuid,'Sangalnos DC / Sangalhos Desporto')) AS x(source_id,target_id,label) LOOP
            SELECT "CanonicalName" INTO n FROM teams WHERE "Id"=m.target_id;
            IF n IS NULL THEN RAISE EXCEPTION 'Cannot reconcile Portugal identity %: target team is missing.',m.label; END IF;
            IF EXISTS(SELECT 1 FROM games WHERE ("HomeTeamId"=m.source_id AND "AwayTeamId"=m.target_id) OR ("HomeTeamId"=m.target_id AND "AwayTeamId"=m.source_id)) THEN RAISE EXCEPTION 'Cannot reconcile Portugal identity %: source and target appear in the same game.',m.label; END IF;
            DELETE FROM team_aliases s WHERE s."TeamId"=m.source_id AND EXISTS(SELECT 1 FROM team_aliases t WHERE t."TeamId"=m.target_id AND t."Source"=s."Source" AND t."SourceTeamId"=s."SourceTeamId" AND t."AliasName"=s."AliasName");
            UPDATE team_aliases SET "TeamId"=m.target_id WHERE "TeamId"=m.source_id;
            DELETE FROM model_lab_run_ratings s WHERE s."TeamId"=m.source_id AND EXISTS(SELECT 1 FROM model_lab_run_ratings t WHERE t."TeamId"=m.target_id AND t."RunId"=s."RunId");
            UPDATE model_lab_run_ratings SET "TeamId"=m.target_id WHERE "TeamId"=m.source_id;
            UPDATE model_lab_run_predictions SET "HomeTeamName"=CASE WHEN "HomeTeamId"=m.source_id THEN n ELSE "HomeTeamName" END,"AwayTeamName"=CASE WHEN "AwayTeamId"=m.source_id THEN n ELSE "AwayTeamName" END,"HomeTeamId"=CASE WHEN "HomeTeamId"=m.source_id THEN m.target_id ELSE "HomeTeamId" END,"AwayTeamId"=CASE WHEN "AwayTeamId"=m.source_id THEN m.target_id ELSE "AwayTeamId" END WHERE "HomeTeamId"=m.source_id OR "AwayTeamId"=m.source_id;
            DELETE FROM rating_history s WHERE s."TeamId"=m.source_id AND EXISTS(SELECT 1 FROM rating_history t WHERE t."TeamId"=m.target_id AND t."GameId"=s."GameId" AND t."EloPoolKey"=s."EloPoolKey" AND t."RulesetVersion"=s."RulesetVersion");
            UPDATE rating_history SET "TeamId"=CASE WHEN "TeamId"=m.source_id THEN m.target_id ELSE "TeamId" END,"OpponentTeamId"=CASE WHEN "OpponentTeamId"=m.source_id THEN m.target_id ELSE "OpponentTeamId" END WHERE "TeamId"=m.source_id OR "OpponentTeamId"=m.source_id;
            DELETE FROM team_ratings s WHERE s."TeamId"=m.source_id AND EXISTS(SELECT 1 FROM team_ratings t WHERE t."TeamId"=m.target_id AND t."EloPoolKey"=s."EloPoolKey" AND t."RulesetVersion"=s."RulesetVersion");
            UPDATE team_ratings SET "TeamId"=m.target_id WHERE "TeamId"=m.source_id;
            UPDATE games SET "HomeTeamId"=CASE WHEN "HomeTeamId"=m.source_id THEN m.target_id ELSE "HomeTeamId" END,"AwayTeamId"=CASE WHEN "AwayTeamId"=m.source_id THEN m.target_id ELSE "AwayTeamId" END,"UpdatedAtUtc"=NOW() WHERE "HomeTeamId"=m.source_id OR "AwayTeamId"=m.source_id;
            UPDATE identity_health_check_findings SET "AffectedTeamId"=CASE WHEN "AffectedTeamId"=m.source_id THEN m.target_id ELSE "AffectedTeamId" END,"RelatedTeamId"=CASE WHEN "RelatedTeamId"=m.source_id THEN m.target_id ELSE "RelatedTeamId" END WHERE "AffectedTeamId"=m.source_id OR "RelatedTeamId"=m.source_id;
            DELETE FROM teams WHERE "Id"=m.source_id; END LOOP; END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) { }
}
