using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace BasketElo.Infrastructure.Persistence.Migrations;
[DbContext(typeof(BasketEloDbContext))]
[Migration("20260814100000_ReconcileSlovenianClubIdentities")]
public partial class ReconcileSlovenianClubIdentities : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$ DECLARE m record; n text; BEGIN
            FOR m IN SELECT * FROM (VALUES
            ('34362e34-6830-4f55-824c-60a8d977fef1'::uuid,'ace3ee3b-4310-4b73-9391-b0756bb89a48'::uuid,'BC Olimpija / Olimpija'),
            ('0028dd19-b5ed-412a-811d-8b77410cba28'::uuid,'ace3ee3b-4310-4b73-9391-b0756bb89a48'::uuid,'Petrol Olimpija / Olimpija'),
            ('180f0dc0-a0e6-4f2e-9e26-3614b97eb883'::uuid,'ace3ee3b-4310-4b73-9391-b0756bb89a48'::uuid,'Union Olimpija / Olimpija'),
            ('021ef712-4c3e-4b61-9ae6-428d4c629cae'::uuid,'5d56a550-ac61-4bfe-8906-27fe0cecd239'::uuid,'KRKA Novo Mesto / KK Krka'),
            ('a74f3371-fb1a-4f65-a60b-999eb9aa56d5'::uuid,'d6848be0-d1a7-4bdb-9bb5-c391b384a2ba'::uuid,'Helios / Helios Domzale'),
            ('b4d28798-c2d9-4d89-a4dd-a8379815ea9d'::uuid,'020e553e-ede6-4303-80f2-8d786fdd6ae5'::uuid,'KK Triglav / KK Triglav Kranj'),
            ('3c85aa72-d34b-4f58-8307-5fd0191a6c46'::uuid,'020e553e-ede6-4303-80f2-8d786fdd6ae5'::uuid,'Triglav Osiguranje / KK Triglav Kranj'),
            ('4ab8323c-987a-4b9e-a5e7-ce9ee6b0f225'::uuid,'020e553e-ede6-4303-80f2-8d786fdd6ae5'::uuid,'Triglav osiguranje / KK Triglav Kranj'),
            ('ed56574e-c060-4182-8145-7df1957be323'::uuid,'e84faba9-1978-45a4-863b-86e82da8fbff'::uuid,'Rogaska Donat / Rogaska'),
            ('1467a9d0-22b7-4d09-a371-d57fc54f0e69'::uuid,'fe138c63-4716-4954-8915-da244b9c1c05'::uuid,'Zlatorog / Zlatorog Lasko'),
            ('58b694d7-e3b2-4bfb-a506-a2c2ec37118e'::uuid,'7b1eedc5-6ae9-4a19-bb83-ae6622d1db7c'::uuid,'BC Plama / Plama P.'),
            ('61c5204b-eacc-4c76-b093-549490ec4770'::uuid,'914bad4b-c1e7-426d-a08a-ef432f4ccfb8'::uuid,'Geoplin Slovan / KK Slovan'),
            ('e5a1c803-b100-48ff-911f-788f74be4499'::uuid,'fe138c63-4716-4954-8915-da244b9c1c05'::uuid,'Pivovarna Lasko / Zlatorog Lasko')) AS x(source_id,target_id,label) LOOP
            SELECT "CanonicalName" INTO n FROM teams WHERE "Id"=m.target_id;
            IF n IS NULL THEN RAISE EXCEPTION 'Cannot reconcile Slovenian identity %: target team is missing.',m.label; END IF;
            IF EXISTS(SELECT 1 FROM games WHERE ("HomeTeamId"=m.source_id AND "AwayTeamId"=m.target_id) OR ("HomeTeamId"=m.target_id AND "AwayTeamId"=m.source_id)) THEN RAISE EXCEPTION 'Cannot reconcile Slovenian identity %: source and target appear in the same game.',m.label; END IF;
            DELETE FROM team_aliases s WHERE s."TeamId"=m.source_id AND EXISTS(SELECT 1 FROM team_aliases t WHERE t."TeamId"=m.target_id AND t."Source"=s."Source" AND t."SourceTeamId"=s."SourceTeamId" AND t."AliasName"=s."AliasName");
            UPDATE team_aliases SET "TeamId"=m.target_id WHERE "TeamId"=m.source_id;
            DELETE FROM model_lab_run_ratings s WHERE s."TeamId"=m.source_id AND EXISTS(SELECT 1 FROM model_lab_run_ratings t WHERE t."TeamId"=m.target_id AND t."RunId"=s."RunId"); UPDATE model_lab_run_ratings SET "TeamId"=m.target_id WHERE "TeamId"=m.source_id;
            UPDATE model_lab_run_predictions SET "HomeTeamName"=CASE WHEN "HomeTeamId"=m.source_id THEN n ELSE "HomeTeamName" END,"AwayTeamName"=CASE WHEN "AwayTeamId"=m.source_id THEN n ELSE "AwayTeamName" END,"HomeTeamId"=CASE WHEN "HomeTeamId"=m.source_id THEN m.target_id ELSE "HomeTeamId" END,"AwayTeamId"=CASE WHEN "AwayTeamId"=m.source_id THEN m.target_id ELSE "AwayTeamId" END WHERE "HomeTeamId"=m.source_id OR "AwayTeamId"=m.source_id;
            DELETE FROM rating_history s WHERE s."TeamId"=m.source_id AND EXISTS(SELECT 1 FROM rating_history t WHERE t."TeamId"=m.target_id AND t."GameId"=s."GameId" AND t."EloPoolKey"=s."EloPoolKey" AND t."RulesetVersion"=s."RulesetVersion"); UPDATE rating_history SET "TeamId"=CASE WHEN "TeamId"=m.source_id THEN m.target_id ELSE "TeamId" END,"OpponentTeamId"=CASE WHEN "OpponentTeamId"=m.source_id THEN m.target_id ELSE "OpponentTeamId" END WHERE "TeamId"=m.source_id OR "OpponentTeamId"=m.source_id;
            DELETE FROM team_ratings s WHERE s."TeamId"=m.source_id AND EXISTS(SELECT 1 FROM team_ratings t WHERE t."TeamId"=m.target_id AND t."EloPoolKey"=s."EloPoolKey" AND t."RulesetVersion"=s."RulesetVersion"); UPDATE team_ratings SET "TeamId"=m.target_id WHERE "TeamId"=m.source_id;
            UPDATE games SET "HomeTeamId"=CASE WHEN "HomeTeamId"=m.source_id THEN m.target_id ELSE "HomeTeamId" END,"AwayTeamId"=CASE WHEN "AwayTeamId"=m.source_id THEN m.target_id ELSE "AwayTeamId" END,"UpdatedAtUtc"=NOW() WHERE "HomeTeamId"=m.source_id OR "AwayTeamId"=m.source_id;
            UPDATE identity_health_check_findings SET "AffectedTeamId"=CASE WHEN "AffectedTeamId"=m.source_id THEN m.target_id ELSE "AffectedTeamId" END,"RelatedTeamId"=CASE WHEN "RelatedTeamId"=m.source_id THEN m.target_id ELSE "RelatedTeamId" END WHERE "AffectedTeamId"=m.source_id OR "RelatedTeamId"=m.source_id;
            DELETE FROM teams WHERE "Id"=m.source_id; END LOOP; END $$;
            """);
    }
    protected override void Down(MigrationBuilder migrationBuilder) { }
}
