using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20261110223000_ReconcileIdentityHealthPairs")]
public partial class ReconcileIdentityHealthPairs : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            DECLARE
                merge_record record;
                source_team_name text;
                target_team_name text;
                cb_canarias_id uuid := 'c3e0ce4d-7618-4309-8930-83c7870ca76c';
                gran_canaria_id uuid := '895ffadc-36b0-469e-8a83-b66d55ed97df';
                hapoel_emek_id uuid := '85acf8be-d6dd-4396-89eb-f41da22bd7da';
                hapoel_galil_elyon_id uuid := 'bd19987e-1f41-49d2-861d-880492f0b3b8';
            BEGIN
                -- Gran Canaria and Tenerife-based CB Canarias were collapsed into
                -- one team. Dbasket embeds the home/away abbreviations after the
                -- date; official ACB URLs retain the historical team slug. All
                -- post-1992 rows on the collapsed identity are Gran Canaria.
                UPDATE games
                SET "HomeTeamId" = gran_canaria_id,
                    "UpdatedAtUtc" = NOW()
                WHERE "HomeTeamId" = cb_canarias_id
                  AND (("Source" = 'acb-dbasket' AND right("SourceGameId", 6) LIKE 'GCA%')
                    OR ("Source" = 'acb-official-tournaments' AND
                        ("SourceUrl" LIKE '%/gran-canaria-%' OR "SourceUrl" LIKE '%/canarias-telecom-%'))
                    OR "GameDateTimeUtc" >= '1992-07-01T00:00:00Z'::timestamptz);

                UPDATE games
                SET "AwayTeamId" = gran_canaria_id,
                    "UpdatedAtUtc" = NOW()
                WHERE "AwayTeamId" = cb_canarias_id
                  AND (("Source" = 'acb-dbasket' AND right("SourceGameId", 6) LIKE '%GCA')
                    OR ("Source" = 'acb-official-tournaments' AND
                        ("SourceUrl" LIKE '%-vs-gran-canaria-%' OR "SourceUrl" LIKE '%-vs-canarias-telecom-%'))
                    OR "GameDateTimeUtc" >= '1992-07-01T00:00:00Z'::timestamptz);

                UPDATE team_aliases
                SET "TeamId" = gran_canaria_id,
                    "MappingMethod" = 'curated',
                    "MappingConfidence" = 100
                WHERE "TeamId" = cb_canarias_id
                  AND ("AliasName" ILIKE '%Gran Canaria%' OR "AliasName" = 'Canarias Telecom');

                UPDATE teams
                SET "CanonicalName" = 'CB Gran Canaria',
                    "CountryCode" = 'ES',
                    "IsActive" = TRUE,
                    "Description" = 'Club from Las Palmas de Gran Canaria founded in 1963. Sponsor-era names including Canarias Telecom, Auna Gran Canaria, Kalise Gran Canaria, Herbalife Gran Canaria and Dreamland Gran Canaria belong to this identity.'
                WHERE "Id" = gran_canaria_id;

                UPDATE teams
                SET "Description" = 'Historical Tenerife-based CB Canarias identity, including the Cafisa, Lucky and CajaCanarias sponsor eras. It is distinct from Las Palmas-based CB Gran Canaria.'
                WHERE "Id" = cb_canarias_id;

                -- Gilboa/Galil is the lineage now branded Hapoel HaEmek. It is
                -- distinct from Hapoel Galil Elyon despite their 2008 licence
                -- arrangement. Move only the provider identity and fixtures that
                -- explicitly belong to Gilboa/Galil.
                UPDATE team_aliases
                SET "TeamId" = hapoel_emek_id,
                    "MappingMethod" = 'curated',
                    "MappingConfidence" = 100
                WHERE "TeamId" = hapoel_galil_elyon_id
                  AND "Source" = 'livescore'
                  AND "SourceTeamId" = 'team:israel:hapoel-gilboa-galil';

                UPDATE games
                SET "HomeTeamId" = hapoel_emek_id,
                    "UpdatedAtUtc" = NOW()
                WHERE "HomeTeamId" = hapoel_galil_elyon_id
                  AND "Source" = 'livescore'
                  AND "SourceGameId" IN ('1884654', '1884666', '1884671', '1884675');

                UPDATE games
                SET "AwayTeamId" = hapoel_emek_id,
                    "UpdatedAtUtc" = NOW()
                WHERE "AwayTeamId" = hapoel_galil_elyon_id
                  AND "Source" = 'livescore'
                  AND "SourceGameId" IN ('1884652', '1884678');

                UPDATE teams
                SET "Description" = 'Northern Israeli Gilboa/Galil lineage, now branded Hapoel HaEmek. It is distinct from Hapoel Galil Elyon.'
                WHERE "Id" = hapoel_emek_id;

                UPDATE teams
                SET "CanonicalName" = 'Hapoel Galil Elyon',
                    "Description" = 'Upper Galilee club based in Kfar Blum. It is distinct from the Gilboa/Galil lineage now branded Hapoel HaEmek.'
                WHERE "Id" = hapoel_galil_elyon_id;

                FOR merge_record IN
                    SELECT * FROM (VALUES
                        ('2085a0bc-1bad-4c73-b953-0dc53bab133c'::uuid, '416db3b0-a3ec-4f0d-8ab1-fe5e0c226d02'::uuid, 'Barons LU into BK Barons'),
                        ('aad2e896-a546-4e6a-9c9c-1836d4140367'::uuid, '3485e9de-5a1a-4426-be0e-68e486f1ce7a'::uuid, 'BC Haifa into Maccabi Haifa'),
                        ('433a9a5f-38ad-4fa3-9f69-68280c49c6cb'::uuid, '74912bba-71ab-4554-a82b-1c3a121902be'::uuid, 'KK Bistrica sponsor identity')
                    ) AS merges(source_team_id, target_team_id, description)
                LOOP
                    SELECT "CanonicalName" INTO source_team_name FROM teams WHERE "Id" = merge_record.source_team_id;
                    IF source_team_name IS NULL THEN
                        CONTINUE;
                    END IF;
                    SELECT "CanonicalName" INTO target_team_name FROM teams WHERE "Id" = merge_record.target_team_id;
                    IF target_team_name IS NULL THEN
                        RAISE EXCEPTION 'Cannot merge identity %: target team is missing.', merge_record.description;
                    END IF;
                    IF EXISTS (
                        SELECT 1 FROM games
                        WHERE ("HomeTeamId" = merge_record.source_team_id AND "AwayTeamId" = merge_record.target_team_id)
                           OR ("HomeTeamId" = merge_record.target_team_id AND "AwayTeamId" = merge_record.source_team_id)
                    ) THEN
                        RAISE EXCEPTION 'Cannot merge identity %: source and target occur in the same game.', merge_record.description;
                    END IF;

                    DELETE FROM team_aliases source_alias
                    WHERE source_alias."TeamId" = merge_record.source_team_id
                      AND EXISTS (SELECT 1 FROM team_aliases target_alias
                                  WHERE target_alias."TeamId" = merge_record.target_team_id
                                    AND target_alias."Source" = source_alias."Source"
                                    AND target_alias."SourceTeamId" = source_alias."SourceTeamId"
                                    AND target_alias."AliasName" = source_alias."AliasName");
                    UPDATE team_aliases SET "TeamId" = merge_record.target_team_id WHERE "TeamId" = merge_record.source_team_id;

                    DELETE FROM team_search_names source_name
                    WHERE source_name."TeamId" = merge_record.source_team_id
                      AND EXISTS (SELECT 1 FROM team_search_names target_name
                                  WHERE target_name."TeamId" = merge_record.target_team_id
                                    AND target_name."Locale" = source_name."Locale"
                                    AND target_name."NormalizedName" = source_name."NormalizedName");
                    UPDATE team_search_names SET "TeamId" = merge_record.target_team_id WHERE "TeamId" = merge_record.source_team_id;

                    DELETE FROM model_lab_run_evolution_points source_point
                    WHERE source_point."TeamId" = merge_record.source_team_id
                      AND EXISTS (SELECT 1 FROM model_lab_run_evolution_points target_point
                                  WHERE target_point."TeamId" = merge_record.target_team_id
                                    AND target_point."RunId" = source_point."RunId"
                                    AND target_point."GameId" = source_point."GameId");
                    UPDATE model_lab_run_evolution_points
                    SET "TeamId" = merge_record.target_team_id, "TeamName" = target_team_name
                    WHERE "TeamId" = merge_record.source_team_id;

                    DELETE FROM model_lab_run_ratings source_rating
                    WHERE source_rating."TeamId" = merge_record.source_team_id
                      AND EXISTS (SELECT 1 FROM model_lab_run_ratings target_rating
                                  WHERE target_rating."TeamId" = merge_record.target_team_id
                                    AND target_rating."RunId" = source_rating."RunId");
                    UPDATE model_lab_run_ratings SET "TeamId" = merge_record.target_team_id WHERE "TeamId" = merge_record.source_team_id;

                    UPDATE model_lab_run_predictions
                    SET "HomeTeamName" = CASE WHEN "HomeTeamId" = merge_record.source_team_id THEN target_team_name ELSE "HomeTeamName" END,
                        "AwayTeamName" = CASE WHEN "AwayTeamId" = merge_record.source_team_id THEN target_team_name ELSE "AwayTeamName" END,
                        "HomeTeamId" = CASE WHEN "HomeTeamId" = merge_record.source_team_id THEN merge_record.target_team_id ELSE "HomeTeamId" END,
                        "AwayTeamId" = CASE WHEN "AwayTeamId" = merge_record.source_team_id THEN merge_record.target_team_id ELSE "AwayTeamId" END
                    WHERE "HomeTeamId" = merge_record.source_team_id OR "AwayTeamId" = merge_record.source_team_id;

                    DELETE FROM rating_history source_history
                    WHERE source_history."TeamId" = merge_record.source_team_id
                      AND EXISTS (SELECT 1 FROM rating_history target_history
                                  WHERE target_history."TeamId" = merge_record.target_team_id
                                    AND target_history."GameId" = source_history."GameId"
                                    AND target_history."EloPoolKey" = source_history."EloPoolKey"
                                    AND target_history."RulesetVersion" = source_history."RulesetVersion");
                    UPDATE rating_history
                    SET "TeamId" = CASE WHEN "TeamId" = merge_record.source_team_id THEN merge_record.target_team_id ELSE "TeamId" END,
                        "OpponentTeamId" = CASE WHEN "OpponentTeamId" = merge_record.source_team_id THEN merge_record.target_team_id ELSE "OpponentTeamId" END
                    WHERE "TeamId" = merge_record.source_team_id OR "OpponentTeamId" = merge_record.source_team_id;

                    DELETE FROM team_ratings source_rating
                    WHERE source_rating."TeamId" = merge_record.source_team_id
                      AND EXISTS (SELECT 1 FROM team_ratings target_rating
                                  WHERE target_rating."TeamId" = merge_record.target_team_id
                                    AND target_rating."EloPoolKey" = source_rating."EloPoolKey"
                                    AND target_rating."RulesetVersion" = source_rating."RulesetVersion");
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

                    DELETE FROM identity_review_decisions source_decision
                    WHERE (source_decision."AffectedTeamId" = merge_record.source_team_id OR source_decision."RelatedTeamId" = merge_record.source_team_id)
                      AND EXISTS (SELECT 1 FROM identity_review_decisions target_decision
                                  WHERE target_decision."DecisionKey" = replace(source_decision."DecisionKey",
                                      replace(merge_record.source_team_id::text, '-', ''), replace(merge_record.target_team_id::text, '-', '')));
                    UPDATE identity_review_decisions
                    SET "AffectedTeamId" = CASE WHEN "AffectedTeamId" = merge_record.source_team_id THEN merge_record.target_team_id ELSE "AffectedTeamId" END,
                        "RelatedTeamId" = CASE WHEN "RelatedTeamId" = merge_record.source_team_id THEN merge_record.target_team_id ELSE "RelatedTeamId" END,
                        "DecisionKey" = replace("DecisionKey", replace(merge_record.source_team_id::text, '-', ''), replace(merge_record.target_team_id::text, '-', ''))
                    WHERE "AffectedTeamId" = merge_record.source_team_id OR "RelatedTeamId" = merge_record.source_team_id;

                    UPDATE teams SET "PredecessorTeamId" = merge_record.target_team_id WHERE "PredecessorTeamId" = merge_record.source_team_id;
                    UPDATE teams SET "SuccessorTeamId" = merge_record.target_team_id WHERE "SuccessorTeamId" = merge_record.source_team_id;
                    DELETE FROM teams WHERE "Id" = merge_record.source_team_id;
                END LOOP;

                UPDATE teams SET "CanonicalName" = 'BK Barons', "IsActive" = FALSE,
                    "Description" = 'Riga club whose sponsor-era names included Barons LU, Barons/LMT, Barons Kvartals and Barons/LDz.'
                WHERE "Id" = '416db3b0-a3ec-4f0d-8ab1-fe5e0c226d02';
                UPDATE teams SET "CanonicalName" = 'Maccabi Haifa',
                    "Description" = 'Haifa club also recorded as BC Haifa, Haifa Basketball Club and Haifa BC from 2000 to 2005.'
                WHERE "Id" = '3485e9de-5a1a-4426-be0e-68e486f1ce7a';
                UPDATE teams SET "CanonicalName" = 'KK Bistrica',
                    "Description" = 'Slovenian club from Slovenska Bistrica, also competing under the sponsor name Bistrica Kety Emmi.'
                WHERE "Id" = '74912bba-71ab-4554-a82b-1c3a121902be';

                INSERT INTO team_search_names ("Id", "TeamId", "Locale", "Name", "NormalizedName", "CreatedAtUtc")
                SELECT names.* FROM (VALUES
                    ('711be337-06ec-44df-aa7f-111111111101'::uuid, gran_canaria_id, 'und', 'CB Gran Canaria', 'CBGRANCANARIA', NOW()),
                    ('711be337-06ec-44df-aa7f-111111111102', gran_canaria_id, 'und', 'Canarias Telecom', 'CANARIASTELECOM', NOW()),
                    ('711be337-06ec-44df-aa7f-111111111103', hapoel_emek_id, 'und', 'Hapoel Gilboa Galil', 'HAPOELGILBOAGALIL', NOW()),
                    ('711be337-06ec-44df-aa7f-111111111104', hapoel_galil_elyon_id, 'und', 'Hapoel Galil Elyon', 'HAPOELGALILELYON', NOW()),
                    ('711be337-06ec-44df-aa7f-111111111105', '416db3b0-a3ec-4f0d-8ab1-fe5e0c226d02', 'und', 'Barons LU', 'BARONSLU', NOW()),
                    ('711be337-06ec-44df-aa7f-111111111106', '3485e9de-5a1a-4426-be0e-68e486f1ce7a', 'und', 'BC Haifa', 'BCHAIFA', NOW()),
                    ('711be337-06ec-44df-aa7f-111111111107', '74912bba-71ab-4554-a82b-1c3a121902be', 'und', 'Bistrica Kety Emmi', 'BISTRICAKETYEMMI', NOW())
                ) AS names("Id", "TeamId", "Locale", "Name", "NormalizedName", "CreatedAtUtc")
                WHERE EXISTS (SELECT 1 FROM teams WHERE teams."Id" = names."TeamId")
                ON CONFLICT ("TeamId", "Locale", "NormalizedName") DO UPDATE SET "Name" = EXCLUDED."Name";

                INSERT INTO identity_review_decisions (
                    "Id", "DecisionKey", "FindingType", "ResolutionAction", "AffectedTeamId", "RelatedTeamId", "Note", "CreatedBy", "CreatedAtUtc")
                SELECT decisions.* FROM (VALUES
                    ('711be337-06ec-44df-aa7f-111111111201'::uuid, 'distinct_teams|teams=1bdae3713cdb41b78a7dcac610023f94:5ea7739b38254556b5ae032c061182b9', 'possible_cross_season_split', 'keep_separate', '1bdae371-3cdb-41b7-8a7d-cac610023f94'::uuid, '5ea7739b-3825-4556-b5ae-032c061182b9'::uuid, 'Modern FMP descends from Radnicki Novi Sad/Radnicki Basket; BKK Radnicki Beograd is a different club.', 'curated-migration', NOW()),
                    ('711be337-06ec-44df-aa7f-111111111202', 'distinct_teams|teams=2f2d155a4bf94e9da06685cc3d13e107:6ae30513bb6e42cd9d4b83cff818dffc', 'possible_cross_season_split', 'keep_separate', '2f2d155a-4bf9-4e9d-a066-85cc3d13e107', '6ae30513-bb6e-42cd-9d4b-83cff818dffc', 'Cedevita Junior renamed to Virtus Zagreb in 2026; it remains distinct from KK Zagreb.', 'curated-migration', NOW()),
                    ('711be337-06ec-44df-aa7f-111111111203', 'distinct_teams|teams=2f2d155a4bf94e9da06685cc3d13e107:798a1d788531418b870cdda97f7b50fb', 'possible_cross_source_match', 'keep_separate', '2f2d155a-4bf9-4e9d-a066-85cc3d13e107', '798a1d78-8531-418b-870c-dda97f7b50fb', 'Cedevita/Virtus Zagreb and KK Zagreb are separate Zagreb clubs.', 'curated-migration', NOW()),
                    ('711be337-06ec-44df-aa7f-111111111204', 'distinct_teams|teams=5ea7739b38254556b5ae032c061182b9:b9d20d02ade34daf9f620a31f91e6f44', 'possible_cross_season_split', 'keep_separate', '5ea7739b-3825-4556-b5ae-032c061182b9', 'b9d20d02-ade3-4daf-9f62-0a31f91e6f44', 'BKK Radnicki Beograd and SPD Radnicki Kragujevac are separate clubs from different cities.', 'curated-migration', NOW()),
                    ('711be337-06ec-44df-aa7f-111111111205', 'distinct_teams|teams=85acf8bed6dd439689ebf41da22bd7da:bd19987e1f4149d2861d880492f0b3b8', 'possible_cross_source_match', 'keep_separate', hapoel_emek_id, hapoel_galil_elyon_id, 'Hapoel HaEmek/Gilboa Galil and Hapoel Galil Elyon are distinct identities despite the 2008 licence arrangement.', 'curated-migration', NOW()),
                    ('711be337-06ec-44df-aa7f-111111111206', 'distinct_teams|teams=89177760b9a44072b0c80941993d35a4:ed5cbc5f71e847d882ad9c914a7f647f', 'possible_cross_source_match', 'keep_separate', '89177760-b9a4-4072-b0c8-0941993d35a4', 'ed5cbc5f-71e8-47d8-82ad-9c914a7f647f', 'Keila Basket and Keila KK parted ways in 2024 and compete as separate clubs.', 'curated-migration', NOW()),
                    ('711be337-06ec-44df-aa7f-111111111207', 'distinct_teams|teams=895ffadc36b0469e8a83b66d55ed97df:c3e0ce4d76184309893083c7870ca76c', 'possible_cross_season_split', 'keep_separate', gran_canaria_id, cb_canarias_id, 'Las Palmas-based CB Gran Canaria and Tenerife-based CB Canarias are separate clubs.', 'curated-migration', NOW())
                ) AS decisions("Id", "DecisionKey", "FindingType", "ResolutionAction", "AffectedTeamId", "RelatedTeamId", "Note", "CreatedBy", "CreatedAtUtc")
                WHERE EXISTS (SELECT 1 FROM teams WHERE teams."Id" = decisions."AffectedTeamId")
                  AND EXISTS (SELECT 1 FROM teams WHERE teams."Id" = decisions."RelatedTeamId")
                ON CONFLICT ("DecisionKey") DO UPDATE
                SET "AffectedTeamId" = EXCLUDED."AffectedTeamId", "RelatedTeamId" = EXCLUDED."RelatedTeamId",
                    "ResolutionAction" = EXCLUDED."ResolutionAction", "Note" = EXCLUDED."Note", "CreatedBy" = EXCLUDED."CreatedBy";

                UPDATE identity_health_check_runs
                SET "InvalidatedAtUtc" = NOW()
                WHERE "InvalidatedAtUtc" IS NULL AND "ScopeKey" LIKE '%pool=europe-clubs%';
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The curated merges and historical game ownership corrections require
        // the pre-deployment database backup to reverse safely.
    }
}
