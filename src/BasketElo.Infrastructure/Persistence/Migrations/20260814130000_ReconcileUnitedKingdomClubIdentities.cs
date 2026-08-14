using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260814130000_ReconcileUnitedKingdomClubIdentities")]
public partial class ReconcileUnitedKingdomClubIdentities : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$ DECLARE m record; n text; BEGIN
            FOR m IN SELECT * FROM (VALUES
            ('409f1612-fbff-4fea-8095-8437174c080a'::uuid,'ec97ee53-91a0-490b-8811-80edd28da36a'::uuid,'CINZANO SCP / Crystal Palace BC'),
            ('bde00060-00c1-45cc-a401-bd0f62310039'::uuid,'5012b3c9-fb8f-43e9-af93-9343f7c624a5'::uuid,'Cadbury''s Boost / Guildford Kings'),
            ('e5e6a2da-e88a-4f01-bf72-7c7bb01a3f28'::uuid,'5012b3c9-fb8f-43e9-af93-9343f7c624a5'::uuid,'Team Polyceil Kingston / Guildford Kings'),
            ('aea7579b-c840-42e4-93c3-151e582a83b7'::uuid,'5012b3c9-fb8f-43e9-af93-9343f7c624a5'::uuid,'Glasgow Rangers BC / Guildford Kings'),
            ('aa5779e6-4f8e-479b-bb39-61aaaa6bde5c'::uuid,'3e25d63b-425f-470e-a196-b4f4330e8157'::uuid,'Worthing Bears / Brighton Bears'),
            ('7067292d-a5f5-4ed4-a223-f508ecacc149'::uuid,'8417260b-f2f3-46e0-88fa-0de18cebf3a7'::uuid,'Leicester / Leicester Riders'),
            ('d62b283d-2aa2-4f9b-ad19-6d747a7401cf'::uuid,'9555ff8e-4ff9-459b-af08-64eb2336c9e8'::uuid,'London Towers / Haribo London Towers'),
            ('4330184f-fb42-434e-ad95-dd9f208c034a'::uuid,'61986a9a-abc2-41af-9415-83dc4c31d434'::uuid,'Doncaster Panthers ENG / Doncaster Panthers GB'),
            ('710b74ff-e5b1-4748-9f28-62bc07365274'::uuid,'3d549904-22b6-4edd-a044-badc38e449b4'::uuid,'Manchester Eagles / Manchester Giants'),
            ('c71d8500-0b7d-4ccf-8e46-942572e0d6f2'::uuid,'3d549904-22b6-4edd-a044-badc38e449b4'::uuid,'Manchester United BC / Manchester Giants')) AS x(source_id,target_id,label) LOOP
            SELECT "CanonicalName" INTO n FROM teams WHERE "Id"=m.target_id;
            IF n IS NULL THEN RAISE EXCEPTION 'Cannot reconcile United Kingdom identity %: target team is missing.',m.label; END IF;
            IF EXISTS(SELECT 1 FROM games WHERE ("HomeTeamId"=m.source_id AND "AwayTeamId"=m.target_id) OR ("HomeTeamId"=m.target_id AND "AwayTeamId"=m.source_id)) THEN RAISE EXCEPTION 'Cannot reconcile United Kingdom identity %: source and target appear in the same game.',m.label; END IF;
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
            UPDATE teams SET "CountryCode"='GB' WHERE "Id"='ef6c164e-714d-4eaa-96dd-2d94522775c6'::uuid;
            UPDATE teams SET "CountryCode"='FI' WHERE "Id"='fcb0e35f-30aa-4ead-9c9c-2554df01f186'::uuid;
            UPDATE teams SET "CountryCode"='NL' WHERE "Id"='980b8646-caa0-4443-98f6-53dd71401f0c'::uuid;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) { }
}
