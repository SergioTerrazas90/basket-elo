using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace BasketElo.Infrastructure.Persistence.Migrations;
[DbContext(typeof(BasketEloDbContext))]
[Migration("20260814120000_ReconcileTurkishClubIdentities")]
public partial class ReconcileTurkishClubIdentities : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$ DECLARE m record; n text; BEGIN
            FOR m IN SELECT * FROM (VALUES
            ('936d0344-c2ae-4892-be99-d14cc8d9084d'::uuid,'ccaeb2c0-0483-4cd5-9c1a-7254bfe39772'::uuid,'Teksut Bandirma / Banvit BC'),
            ('00d76b2c-967c-4038-ad47-a1646b4a5a95'::uuid,'2fa33efa-08e4-4541-9fa3-a6816eae917a'::uuid,'Turk Telekom PTT / Turk Telekom'),
            ('012d4df2-1d64-45a7-87f4-c7b29d071fd2'::uuid,'ca2ea5bd-31f5-42ef-b639-98f078e2c881'::uuid,'Aliaga Petkim / Petkim Spor'),
            ('d788ebd3-2eb7-430b-8d8b-36bd47ac9e5e'::uuid,'29c67957-0743-4aa6-9851-b84ef75ae7d5'::uuid,'Beslen Makarna SK / Beslenspor'),
            ('215f7253-1b63-4821-99c7-ad867a59810b'::uuid,'cc57cc5a-f687-4767-ad71-5a31b28c45c6'::uuid,'Kombassan Konya / Torku Konyaspor'),
            ('8cbc6b21-69a9-4c2d-af7d-da1cb61e58c1'::uuid,'7582d39b-3664-4286-b412-c0ba799442d9'::uuid,'Sisecam Pasabahce / Pasabahce')) AS x(source_id,target_id,label) LOOP
            SELECT "CanonicalName" INTO n FROM teams WHERE "Id"=m.target_id;
            IF n IS NULL THEN RAISE EXCEPTION 'Cannot reconcile Turkish identity %: target team is missing.',m.label; END IF;
            IF EXISTS(SELECT 1 FROM games WHERE ("HomeTeamId"=m.source_id AND "AwayTeamId"=m.target_id) OR ("HomeTeamId"=m.target_id AND "AwayTeamId"=m.source_id)) THEN RAISE EXCEPTION 'Cannot reconcile Turkish identity %: source and target appear in the same game.',m.label; END IF;
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
