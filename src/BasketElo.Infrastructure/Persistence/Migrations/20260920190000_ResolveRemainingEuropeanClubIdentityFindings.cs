using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260920190000_ResolveRemainingEuropeanClubIdentityFindings")]
public partial class ResolveRemainingEuropeanClubIdentityFindings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            DECLARE
                dubrava_id uuid := '2347f2f0-621b-4455-a9f0-ff7af806edb7'::uuid;
                skrljevo_id uuid := 'f2f6951f-9aee-4320-8464-10fe9df39b0e'::uuid;
                alias_count integer;
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM teams WHERE "Id" = dubrava_id) OR
                   NOT EXISTS (SELECT 1 FROM teams WHERE "Id" = skrljevo_id) THEN
                    RAISE EXCEPTION 'Cannot reconcile Dubrava/Skrljevo: a canonical team is missing.';
                END IF;

                SELECT COUNT(*) INTO alias_count
                FROM team_aliases
                WHERE "Source" = 'livescore'
                  AND "SourceTeamId" = 'team:croatia:kk-skrljevo';
                IF alias_count <> 1 THEN
                    RAISE EXCEPTION 'Expected one livescore Skrljevo alias, found %.', alias_count;
                END IF;

                UPDATE team_aliases
                SET "TeamId" = skrljevo_id,
                    "AliasName" = 'KK Skrljevo',
                    "MappingMethod" = 'manual',
                    "MappingConfidence" = 100
                WHERE "Source" = 'livescore'
                  AND "SourceTeamId" = 'team:croatia:kk-skrljevo';

                IF EXISTS (
                    SELECT 1
                    FROM games
                    WHERE "Id" = '82ed2b7e-d344-4bdd-a039-44b840f0a796'
                      AND ("Source" <> 'livescore' OR
                           "SourceGameId" <> '1875100' OR
                           "HomeTeamId" <> dubrava_id OR
                           "AwayTeamId" NOT IN (dubrava_id, skrljevo_id))
                ) THEN
                    RAISE EXCEPTION 'The Dubrava-Skrljevo fixture no longer has the expected shape.';
                END IF;

                UPDATE games
                SET "AwayTeamId" = skrljevo_id,
                    "UpdatedAtUtc" = NOW()
                WHERE "Id" = '82ed2b7e-d344-4bdd-a039-44b840f0a796'
                  AND "Source" = 'livescore'
                  AND "SourceGameId" = '1875100'
                  AND "HomeTeamId" = dubrava_id
                  AND "AwayTeamId" = dubrava_id;
            END $$;

            UPDATE teams
            SET "CanonicalName" = 'Falco KC Szombathely',
                "CountryCode" = 'HU',
                "IsActive" = TRUE,
                "Description" = 'Hungarian club from Szombathely, founded in 1980 and commonly known as Falco KC Szombathely. Historical Falco, Falco KC, and Szombathely records belong to this single club identity.'
            WHERE "Id" = '4b298ff7-2cbe-4a80-ad69-8a27356b5f18';

            UPDATE teams
            SET "IsActive" = FALSE,
                "Description" = 'Turkish development club founded in 2005 as Genç Banvitliler and later known as Bandırma Kırmızı. It ceased competing after the Banvit organisation withdrew in 2020 and is distinct from Bandırma Bordo, founded in 2023.'
            WHERE "Id" = '23796753-1002-49ea-a29d-03e2a22ae475';

            UPDATE teams
            SET "CountryCode" = 'TR',
                "IsActive" = TRUE,
                "Description" = 'Turkish club from Bandırma founded in 2023 to revive the city''s basketball tradition. It is a new organisation and is distinct from the earlier Bandırma Kırmızı development club.'
            WHERE "Id" = 'c0692765-f228-4aab-8f3f-1dadd0b445e2';

            UPDATE teams
            SET "Description" = 'Croatian club from Zagreb''s Dubrava district. Sponsor-era names such as Furnir and DONA Dubrava belong to this identity; it is distinct from KK Škrljevo.'
            WHERE "Id" = '2347f2f0-621b-4455-a9f0-ff7af806edb7';

            UPDATE teams
            SET "Description" = 'Croatian club from Škrljevo, also styled KK or DepoLink Škrljevo. It is distinct from Zagreb-based KK Dubrava.'
            WHERE "Id" = 'f2f6951f-9aee-4320-8464-10fe9df39b0e';

            UPDATE teams
            SET "CanonicalName" = 'Pallalcesto Amatori Udine (1999–2011)',
                "Description" = 'Italian club from Udine founded in 1999 and dissolved in 2011, widely known as Snaidero Udine. It is the predecessor of, but a separate legal identity from, Amici Pallacanestro Udinese founded in 2011.'
            WHERE "Id" = '704a1b38-77a2-4db2-820e-096803c8edb4';

            UPDATE teams
            SET "Description" = 'Italian club from Udine founded in 2011 after Pallalcesto Amatori Udine dissolved. It is commonly known as APU Udine and is linked as the successor while remaining a separate club identity.'
            WHERE "Id" = '2101accc-d122-4c3b-8ee4-974252cc9ead';

            UPDATE teams
            SET "CanonicalName" = 'Scaligera Basket Verona (1951–2002)',
                "IsActive" = FALSE,
                "Description" = 'Historical Verona club founded in 1951, including the Glaxo, Mash Jeans, Birex and Müller sponsor eras through 2002. It is kept separate from the modern Tezenis-era Verona organisation.'
            WHERE "Id" = '34786a25-4e06-486b-8a85-c059c1938cd9';

            UPDATE teams
            SET "CanonicalName" = 'Scaligera Basket Verona',
                "IsActive" = TRUE,
                "Description" = 'Modern Verona club identity established through Basket Scaligero in 2007 and commonly known by the sponsor name Tezenis Verona. It is kept separate from the historical 1951–2002 Scaligera entity.'
            WHERE "Id" = 'f3cd842b-8a11-4af7-a7f3-b8ae7a1ac250';

            UPDATE teams
            SET "Description" = 'Milan club founded in 1952 and currently competing as Urania Basket Milano. It is a separate club from Olimpia Milano.'
            WHERE "Id" = '7637c148-89be-444e-ada2-cd8e085b8b06';

            INSERT INTO team_search_names ("Id", "TeamId", "Locale", "Name", "NormalizedName", "CreatedAtUtc") VALUES
                ('d74365bb-d4a4-4cdb-a5af-c217eb24a101', '4b298ff7-2cbe-4a80-ad69-8a27356b5f18', 'und', 'Falco KC Szombathely', 'FALCOKCSZOMBATHELY', NOW()),
                ('d74365bb-d4a4-4cdb-a5af-c217eb24a102', '4b298ff7-2cbe-4a80-ad69-8a27356b5f18', 'und', 'Falco KC', 'FALCOKC', NOW()),
                ('d74365bb-d4a4-4cdb-a5af-c217eb24a103', '4b298ff7-2cbe-4a80-ad69-8a27356b5f18', 'und', 'Szombathely', 'SZOMBATHELY', NOW()),
                ('d74365bb-d4a4-4cdb-a5af-c217eb24a104', '704a1b38-77a2-4db2-820e-096803c8edb4', 'und', 'Pallalcesto Amatori Udine', 'PALLALCESTOAMATORIUDINE', NOW()),
                ('d74365bb-d4a4-4cdb-a5af-c217eb24a105', '34786a25-4e06-486b-8a85-c059c1938cd9', 'und', 'Müller Verona', 'MULLERVERONA', NOW()),
                ('d74365bb-d4a4-4cdb-a5af-c217eb24a106', 'f3cd842b-8a11-4af7-a7f3-b8ae7a1ac250', 'und', 'Tezenis Verona', 'TEZENISVERONA', NOW())
            ON CONFLICT ("TeamId", "Locale", "NormalizedName") DO UPDATE
            SET "Name" = EXCLUDED."Name";

            INSERT INTO identity_review_decisions (
                "Id", "DecisionKey", "FindingType", "ResolutionAction",
                "AffectedTeamId", "RelatedTeamId", "Note", "CreatedBy", "CreatedAtUtc") VALUES
                ('d74365bb-d4a4-4cdb-a5af-c217eb24a201', 'distinct_teams|teams=23796753100249eaa29d03e2a22ae475:c0692765f2284aab8f3f1dadd0b445e2', 'possible_cross_source_match', 'keep_separate', '23796753-1002-49ea-a29d-03e2a22ae475', 'c0692765-f228-4aab-8f3f-1dadd0b445e2', 'Bandırma Kırmızı (founded 2005) and Bandırma Bordo (founded 2023) are separate organisations.', 'curated-migration', NOW()),
                ('d74365bb-d4a4-4cdb-a5af-c217eb24a202', 'distinct_teams|teams=2347f2f0621b4455a9f0ff7af806edb7:f2f6951f9aee4320846410fe9df39b0e', 'possible_cross_source_match', 'keep_separate', '2347f2f0-621b-4455-a9f0-ff7af806edb7', 'f2f6951f-9aee-4320-8464-10fe9df39b0e', 'KK Dubrava and KK Škrljevo are separate Croatian clubs; a livescore Škrljevo alias was corrected from Dubrava.', 'curated-migration', NOW()),
                ('d74365bb-d4a4-4cdb-a5af-c217eb24a203', 'distinct_teams|teams=34786a254e06486b8a85c059c1938cd9:f3cd842b8a114af7a7f3b8ae7a1ac250', 'possible_cross_source_match', 'keep_separate', '34786a25-4e06-486b-8a85-c059c1938cd9', 'f3cd842b-8a11-4af7-a7f3-b8ae7a1ac250', 'The historical 1951–2002 Scaligera entity and the modern Tezenis-era Verona organisation are stored separately.', 'curated-migration', NOW()),
                ('d74365bb-d4a4-4cdb-a5af-c217eb24a204', 'distinct_teams|teams=2101acccd1224c3b8ee4974252cc9ead:704a1b3877a24db2820e096803c8edb4', 'possible_cross_source_match', 'keep_separate', '2101accc-d122-4c3b-8ee4-974252cc9ead', '704a1b38-77a2-4db2-820e-096803c8edb4', 'Pallalcesto Amatori Udine dissolved in 2011; Amici Pallacanestro Udinese was newly founded that year.', 'curated-migration', NOW()),
                ('d74365bb-d4a4-4cdb-a5af-c217eb24a205', 'distinct_teams|teams=50fab06c649f44ecb6bd9734b406f327:7637c14889be444eada2cd8e085b8b06', 'possible_cross_source_match', 'keep_separate', '50fab06c-649f-44ec-b6bd-9734b406f327', '7637c148-89be-444e-ada2-cd8e085b8b06', 'Olimpia Milano and Urania Basket Milano are separate Milan clubs.', 'curated-migration', NOW()),
                ('d74365bb-d4a4-4cdb-a5af-c217eb24a206', 'distinct_teams|teams=0c54e2fd99794ed898fe45ec72cb37ea:4cfd7322bc3e4a499461ae9d64acbd68', 'possible_cross_source_match', 'keep_separate', '0c54e2fd-9979-4ed8-98fe-45ec72cb37ea', '4cfd7322-bc3e-4a49-9461-ae9d64acbd68', 'KK Crvena zvezda and OKK Beograd are separate Belgrade clubs.', 'curated-migration', NOW())
            ON CONFLICT ("DecisionKey") DO UPDATE
            SET "AffectedTeamId" = EXCLUDED."AffectedTeamId",
                "RelatedTeamId" = EXCLUDED."RelatedTeamId",
                "ResolutionAction" = EXCLUDED."ResolutionAction",
                "Note" = EXCLUDED."Note",
                "CreatedBy" = EXCLUDED."CreatedBy";

            UPDATE identity_review_decisions
            SET "Note" = CASE "DecisionKey"
                WHEN 'distinct_teams|teams=23796753100249eaa29d03e2a22ae475:c0692765f2284aab8f3f1dadd0b445e2' THEN 'Bandırma Kırmızı (founded 2005) and Bandırma Bordo (founded 2023) are separate organisations.'
                WHEN 'distinct_teams|teams=2347f2f0621b4455a9f0ff7af806edb7:f2f6951f9aee4320846410fe9df39b0e' THEN 'KK Dubrava and KK Škrljevo are separate Croatian clubs; a livescore Škrljevo alias was corrected from Dubrava.'
                WHEN 'distinct_teams|teams=34786a254e06486b8a85c059c1938cd9:f3cd842b8a114af7a7f3b8ae7a1ac250' THEN 'The historical 1951–2002 Scaligera entity and the modern Tezenis-era Verona organisation are stored separately.'
                WHEN 'distinct_teams|teams=2101acccd1224c3b8ee4974252cc9ead:704a1b3877a24db2820e096803c8edb4' THEN 'Pallalcesto Amatori Udine dissolved in 2011; Amici Pallacanestro Udinese was newly founded that year.'
                WHEN 'distinct_teams|teams=50fab06c649f44ecb6bd9734b406f327:7637c14889be444eada2cd8e085b8b06' THEN 'Olimpia Milano and Urania Basket Milano are separate Milan clubs.'
                WHEN 'distinct_teams|teams=0c54e2fd99794ed898fe45ec72cb37ea:4cfd7322bc3e4a499461ae9d64acbd68' THEN 'KK Crvena zvezda and OKK Beograd are separate Belgrade clubs.'
                ELSE "Note"
            END,
                "CreatedBy" = 'curated-migration'
            WHERE "DecisionKey" IN (
                'distinct_teams|teams=23796753100249eaa29d03e2a22ae475:c0692765f2284aab8f3f1dadd0b445e2',
                'distinct_teams|teams=2347f2f0621b4455a9f0ff7af806edb7:f2f6951f9aee4320846410fe9df39b0e',
                'distinct_teams|teams=34786a254e06486b8a85c059c1938cd9:f3cd842b8a114af7a7f3b8ae7a1ac250',
                'distinct_teams|teams=2101acccd1224c3b8ee4974252cc9ead:704a1b3877a24db2820e096803c8edb4',
                'distinct_teams|teams=50fab06c649f44ecb6bd9734b406f327:7637c14889be444eada2cd8e085b8b06',
                'distinct_teams|teams=0c54e2fd99794ed898fe45ec72cb37ea:4cfd7322bc3e4a499461ae9d64acbd68'
            );

            WITH active_global_runs AS (
                SELECT "Id",
                       ROW_NUMBER() OVER (ORDER BY "CheckedAtUtc" DESC, "CreatedAtUtc" DESC) AS position
                FROM identity_health_check_runs
                WHERE "ScopeKey" = 'source=*|season=*|country=*|competition=*|pool=europe-clubs'
                  AND "InvalidatedAtUtc" IS NULL
            )
            UPDATE identity_health_check_runs run
            SET "InvalidatedAtUtc" = NOW()
            FROM active_global_runs ranked
            WHERE run."Id" = ranked."Id"
              AND ranked.position > 1;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Identity decisions and corrected provider/game mappings are not
        // reversed automatically. Restore the pre-reconciliation backup if
        // rollback is required.
    }
}
