using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace BasketElo.Infrastructure.Persistence.Migrations;
[DbContext(typeof(BasketEloDbContext))]
[Migration("20260814110000_ReconcileSlovakClubIdentities")]
public partial class ReconcileSlovakClubIdentities : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$ DECLARE m record; n text; BEGIN
            FOR m IN SELECT * FROM (VALUES
            ('1e8d9de6-3833-49b9-b5d2-fa40314884c6'::uuid,'d3f3a8c9-2687-4d44-9182-ab3bc9928bbc'::uuid,'BK Banik Cigel / BC Prievidza'),
            ('95069afb-c2fa-4e0d-b7fb-971ded49b976'::uuid,'f455baca-1766-435b-8740-317694c277fe'::uuid,'BC Slovakofarma / Slovakofarma Pezinok'),
            ('51e16e77-5e7c-4fba-b0ee-643fd97288f4'::uuid,'d2233ecb-b2d9-4ecd-9650-bf448efcbec7'::uuid,'Inter Slovnaft ZTS / Inter Bratislava'),
            ('eaa52301-4e88-4fdd-a634-a7ab9f9851ab'::uuid,'d2233ecb-b2d9-4ecd-9650-bf448efcbec7'::uuid,'BK Inter Bratislava / Inter Bratislava'),
            ('c9cbe42b-7fe6-4e85-8dbd-995bb40d61b2'::uuid,'cfbf255c-71b1-4382-977b-0bd842363787'::uuid,'MBK Komarno / BC Komarno')) AS x(source_id,target_id,label) LOOP
            SELECT "CanonicalName" INTO n FROM teams WHERE "Id"=m.target_id;
            IF n IS NULL THEN RAISE EXCEPTION 'Cannot reconcile Slovak identity %: target team is missing.',m.label; END IF;
            IF EXISTS(SELECT 1 FROM games WHERE ("HomeTeamId"=m.source_id AND "AwayTeamId"=m.target_id) OR ("HomeTeamId"=m.target_id AND "AwayTeamId"=m.source_id)) THEN RAISE EXCEPTION 'Cannot reconcile Slovak identity %: source and target appear in the same game.',m.label; END IF;
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

            UPDATE teams SET "CountryCode"='RS' WHERE "Id"='daf97b66-3296-484f-bd13-b59dfd55989b'::uuid;
            UPDATE teams SET "CountryCode"='BA' WHERE "Id"='4e731f23-bf13-45bc-9551-7eb3049d6bdb'::uuid;
            UPDATE teams SET "CountryCode"='BG' WHERE "Id"='706a4b22-a133-478e-bb7e-eecdf83724d6'::uuid;
            UPDATE teams SET "CountryCode"='AZ' WHERE "Id"='e7e3a7eb-256f-4bb3-b25f-42afae42f919'::uuid;
            UPDATE teams SET "CountryCode"='GE' WHERE "Id"='79d70970-82bd-4fae-a792-64978bfb67eb'::uuid;
            UPDATE teams SET "CountryCode"='SI' WHERE "Id"='7e76fe8d-d4c0-4f96-8da6-05129ee69b13'::uuid;
            UPDATE teams SET "CountryCode"='AL' WHERE "Id"='4ad88a99-4bda-4b54-b117-e6f17383483a'::uuid;
            """);
    }
    protected override void Down(MigrationBuilder migrationBuilder) { }
}
