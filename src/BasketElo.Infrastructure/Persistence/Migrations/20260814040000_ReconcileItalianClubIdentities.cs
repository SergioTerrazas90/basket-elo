using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260814040000_ReconcileItalianClubIdentities")]
public partial class ReconcileItalianClubIdentities : Migration
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
                        ('0f9ae08d-4fb7-4cb8-acae-1f6cebb35d28'::uuid, '50fab06c-649f-44ec-b6bd-9734b406f327'::uuid, 'AJ Milano / Olimpia Milano'),
                        ('320f89e5-df26-458e-90f2-da8935f5712e'::uuid, '50fab06c-649f-44ec-b6bd-9734b406f327'::uuid, 'Olimpia Bevi Billy / Olimpia Milano'),
                        ('9f2c8b7f-c803-405c-b8e2-6a718d5234e0'::uuid, '50fab06c-649f-44ec-b6bd-9734b406f327'::uuid, 'Olimpia Philips / Olimpia Milano'),
                        ('9bacb544-e74f-4e2d-80e2-96b85dc8b56e'::uuid, '50fab06c-649f-44ec-b6bd-9734b406f327'::uuid, 'Pall. Olimpia Simac / Olimpia Milano'),
                        ('062ef744-77c1-4d21-8850-69806ff4a688'::uuid, '50fab06c-649f-44ec-b6bd-9734b406f327'::uuid, 'Breil Milano / Olimpia Milano'),
                        ('db8d1e6c-353f-4eed-ab7e-55e71ab5d7ae'::uuid, '50fab06c-649f-44ec-b6bd-9734b406f327'::uuid, 'Cinzano Pallancanestro / Olimpia Milano'),
                        ('98f9aa28-a4b7-4d87-9f18-e9fe702f0f34'::uuid, '50fab06c-649f-44ec-b6bd-9734b406f327'::uuid, '{{Basket Milano / Olimpia Milano'),
                        ('fb3793ed-e43a-43ae-a94e-057babdf27c6'::uuid, 'b9e0d913-358c-4162-ae7a-3ad406f67a67'::uuid, 'Aeroporti / Virtus Roma'),
                        ('1dcd8625-80b7-47d2-af8a-ad9f3ab0658b'::uuid, 'b9e0d913-358c-4162-ae7a-3ad406f67a67'::uuid, 'Lottomatica Roma / Virtus Roma'),
                        ('61ec0db7-d353-4794-b07f-fc15475d91bc'::uuid, 'b9e0d913-358c-4162-ae7a-3ad406f67a67'::uuid, 'Pall. Virtus-Banco / Virtus Roma'),
                        ('2a42787c-dc46-444f-b113-ac5e3acf6459'::uuid, 'eed5a4ad-db1c-46fe-b7f9-9cca044211ac'::uuid, 'Benetton Treviso / Treviso'),
                        ('158bda32-2d76-4fab-bdfe-fb7445e301da'::uuid, '5f38578e-d721-45e8-a32e-60a0e0cc6cef'::uuid, 'Birra Forst Cantu / Tisettanta Cantu'),
                        ('2c59f85f-5fed-4336-9ef9-3085dfbd52b6'::uuid, '5f38578e-d721-45e8-a32e-60a0e0cc6cef'::uuid, 'Jollycolombani Cantu / Tisettanta Cantu'),
                        ('96165607-09d9-4768-a2db-713ccb9aeaf7'::uuid, '5f38578e-d721-45e8-a32e-60a0e0cc6cef'::uuid, 'Pall. Arexons Cantu / Tisettanta Cantu'),
                        ('f022fd1d-6f19-4f29-9d84-19add51287d4'::uuid, '5f38578e-d721-45e8-a32e-60a0e0cc6cef'::uuid, 'Pallacanestro Arexons Cantu / Tisettanta Cantu'),
                        ('1dff10b5-14a9-4e79-a8c6-54329fe19b5f'::uuid, '5f38578e-d721-45e8-a32e-60a0e0cc6cef'::uuid, 'Pallacanestro Cantu SPA / Tisettanta Cantu'),
                        ('6ac1b074-1222-4eeb-aa75-42692ad0f3fb'::uuid, '5f38578e-d721-45e8-a32e-60a0e0cc6cef'::uuid, 'Shampoo Clear Cantu / Tisettanta Cantu'),
                        ('50b506a0-3174-453a-81b3-35a3852e0843'::uuid, '5f38578e-d721-45e8-a32e-60a0e0cc6cef'::uuid, 'Vertical Vision / Tisettanta Cantu'),
                        ('1fd349ba-f22c-4bbf-aa3b-430ceb57680c'::uuid, '5f38578e-d721-45e8-a32e-60a0e0cc6cef'::uuid, 'Wiwa Vismara Cantu / Tisettanta Cantu'),
                        ('9a14c119-a119-488c-8f34-e4c5580aac0a'::uuid, '66fb3158-6ca2-4755-b1a8-caffc08ded46'::uuid, 'Casti Group Varese / Cimberio Varese'),
                        ('d5ed0471-5c0f-4526-b2bf-b4d4070d2612'::uuid, '66fb3158-6ca2-4755-b1a8-caffc08ded46'::uuid, 'Metis Varese / Cimberio Varese'),
                        ('0d38b9e4-d0bb-4692-ae80-5e78da5b1a79'::uuid, '481ba974-3700-4be9-a759-7b9a36bfd5e9'::uuid, 'Jolly Colombani Forli / C. Montana Forli'),
                        ('2a5f6c36-3204-404c-a169-3e2021fb90aa'::uuid, '09ffdcc8-a0e9-4606-b5d1-e25f366d3b01'::uuid, 'Arrigoni Rieti / Solsonica Rieti'),
                        ('799db878-344b-47f9-987e-02495df8b99e'::uuid, 'fb3a7371-139c-4386-be53-552471034abe'::uuid, 'Snaidero Caserta / Caserta'),
                        ('c8854b9c-deda-4831-81ef-61879bbb4abc'::uuid, 'fb3a7371-139c-4386-be53-552471034abe'::uuid, 'Phonolia Caserta / Caserta'),
                        ('869e68f1-b57e-4352-9026-f473d22fddb6'::uuid, '77a86446-7229-4e2a-8b3a-e97e5c9e4261'::uuid, 'Scavolini Pesaro / Pesaro'),
                        ('04717942-cce7-4aec-a58f-b5b94a073dd8'::uuid, '77a86446-7229-4e2a-8b3a-e97e5c9e4261'::uuid, 'VL Scavolini Basket / Pesaro'),
                        ('1f4050f7-40ac-4c10-bd3b-9bb469cbf9a3'::uuid, '89236f41-d483-454a-9479-29ae8f0a15d4'::uuid, 'Olimpia Pistoia / Pistoia'),
                        ('0d252ba8-998f-4a94-b427-1714a867072f'::uuid, '34786a25-4e06-486b-8a85-c059c1938cd9'::uuid, 'Scaligera Basket / Muller Verona'),
                        ('5e142fbf-16c2-4080-8bf8-6a0cd39d9536'::uuid, '34786a25-4e06-486b-8a85-c059c1938cd9'::uuid, '{{Basket Verona / Muller Verona'),
                        ('2fc1a831-b8d5-4b2b-91aa-1a02fd9e3333'::uuid, 'fb5b251c-7a40-42de-bcc1-0d27321b2423'::uuid, 'Auxilium Pallacanestro Torino / Auxilium Torino'),
                        ('08c0cac9-1f59-4cba-aa06-79641739b4e6'::uuid, '439a07d3-2717-48ca-8f9f-a7c4146784b1'::uuid, 'Canon-Reyer Venezia / Venezia'),
                        ('d183d582-5a17-4949-9a41-cd8231012760'::uuid, '1a47d77a-4c5c-4a5d-be41-a6609cdc7bef'::uuid, 'Montecatini SC 2000 / BingoSNAI Montecatini'),
                        ('2ecaf5a3-40be-44da-80f0-f7ec69f8582c'::uuid, '361f4cdf-4662-405e-a0b3-29650d6d8434'::uuid, 'US Ceramica Pagnossin / S.D.A.G. Gorizia'),
                        ('ce8f4556-e5a5-4cf8-bcc5-fa7aa03255b9'::uuid, '62fa53b4-db29-49e1-9454-2e09765dc769'::uuid, 'Cestistica Pfizer / Viola Reggio Calabria'),
                        ('3e181a08-7c07-4bf9-923f-af11b1377356'::uuid, '0bada83f-c086-4cdf-adb9-625dd4547947'::uuid, 'BC Lebole Mestre / Lebole Mestre'),
                        ('02e1f9a6-2a1f-4ce3-8929-d5225a5caad3'::uuid, '0bada83f-c086-4cdf-adb9-625dd4547947'::uuid, 'Basketball Mestre / Lebole Mestre'),
                        ('c0ceb158-2f43-45c2-a1ac-a14f4b606cc2'::uuid, '0efe5f1d-0dfb-42e5-8f6f-1a9b0e9c81dd'::uuid, 'Caripe Pescara / Facar Pescara'),
                        ('b9219711-9d2f-41ef-9b28-53b802b2bd5e'::uuid, '0efe5f1d-0dfb-42e5-8f6f-1a9b0e9c81dd'::uuid, '{{Basket Pescara / Facar Pescara'),
                        ('53c22bc2-768d-4331-92fe-f9fbf27748a7'::uuid, 'ac83ffb8-8a99-41ea-8227-b1a519b26c44'::uuid, 'Pepsi Basket Fiera / Basket Rimini')
                    ) AS merges(source_team_id, target_team_id, description)
                LOOP
                    source_team_name := NULL;
                    target_team_name := NULL;

                    SELECT "CanonicalName" INTO source_team_name FROM teams WHERE "Id" = merge_record.source_team_id;
                    SELECT "CanonicalName" INTO target_team_name FROM teams WHERE "Id" = merge_record.target_team_id;

                    IF target_team_name IS NULL THEN
                        RAISE EXCEPTION 'Cannot reconcile Italian identity %: target team is missing.', merge_record.description;
                    END IF;

                    IF source_team_name IS NULL AND NOT EXISTS (SELECT 1 FROM games WHERE "HomeTeamId" = merge_record.source_team_id OR "AwayTeamId" = merge_record.source_team_id)
                       AND NOT EXISTS (SELECT 1 FROM rating_history WHERE "TeamId" = merge_record.source_team_id OR "OpponentTeamId" = merge_record.source_team_id)
                       AND NOT EXISTS (SELECT 1 FROM team_ratings WHERE "TeamId" = merge_record.source_team_id)
                       AND NOT EXISTS (SELECT 1 FROM team_aliases WHERE "TeamId" = merge_record.source_team_id) THEN
                        CONTINUE;
                    END IF;

                    IF EXISTS (SELECT 1 FROM games WHERE ("HomeTeamId" = merge_record.source_team_id AND "AwayTeamId" = merge_record.target_team_id) OR ("HomeTeamId" = merge_record.target_team_id AND "AwayTeamId" = merge_record.source_team_id)) THEN
                        RAISE EXCEPTION 'Cannot reconcile Italian identity %: source and target appear in the same game.', merge_record.description;
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

                    DELETE FROM identity_review_decisions duplicate_decision
                    WHERE (duplicate_decision."AffectedTeamId" = merge_record.source_team_id OR duplicate_decision."RelatedTeamId" = merge_record.source_team_id)
                      AND EXISTS (SELECT 1 FROM identity_review_decisions target_decision WHERE target_decision."DecisionKey" = replace(duplicate_decision."DecisionKey", replace(merge_record.source_team_id::text, '-', ''), replace(merge_record.target_team_id::text, '-', '')));
                    UPDATE identity_review_decisions
                    SET "AffectedTeamId" = CASE WHEN "AffectedTeamId" = merge_record.source_team_id THEN merge_record.target_team_id ELSE "AffectedTeamId" END,
                        "RelatedTeamId" = CASE WHEN "RelatedTeamId" = merge_record.source_team_id THEN merge_record.target_team_id ELSE "RelatedTeamId" END,
                        "DecisionKey" = replace("DecisionKey", replace(merge_record.source_team_id::text, '-', ''), replace(merge_record.target_team_id::text, '-', ''))
                    WHERE "AffectedTeamId" = merge_record.source_team_id OR "RelatedTeamId" = merge_record.source_team_id;

                    UPDATE teams SET "PredecessorTeamId" = merge_record.target_team_id WHERE "PredecessorTeamId" = merge_record.source_team_id AND "Id" <> merge_record.target_team_id;
                    UPDATE teams SET "SuccessorTeamId" = merge_record.target_team_id WHERE "SuccessorTeamId" = merge_record.source_team_id AND "Id" <> merge_record.target_team_id;
                    DELETE FROM teams WHERE "Id" = merge_record.source_team_id;
                END LOOP;

                UPDATE teams
                SET "CountryCode" = 'GB'
                WHERE "Id" = '409f1612-fbff-4fea-8095-8437174c080a'::uuid
                  AND upper(coalesce("CountryCode", '')) IN ('IT', 'ITA');
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // These provider identities are intentionally not split after their games and aliases have been consolidated.
    }
}
