using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260920170000_ReconcileFmpClubIdentities")]
public partial class ReconcileFmpClubIdentities : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            DECLARE
                old_fmp_id uuid := '5f4f1b19-7284-4dfc-a231-baefd6b871bb'::uuid;
                modern_fmp_id uuid := '1bdae371-3cdb-41b7-8a7d-cac610023f94'::uuid;
                academy_fmp_id uuid := '0285308f-954c-49f2-8401-a59099f4f18c'::uuid;
                merge_record record;
                source_team_name text;
                target_team_name text;
                split_game_count integer;
                duplicate_game_count integer;
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM teams WHERE "Id" = old_fmp_id) THEN
                    RAISE EXCEPTION 'Cannot reconcile FMP identities: original FMP target is missing.';
                END IF;
                IF NOT EXISTS (SELECT 1 FROM teams WHERE "Id" = modern_fmp_id) THEN
                    RAISE EXCEPTION 'Cannot reconcile FMP identities: modern FMP target is missing.';
                END IF;
                IF NOT EXISTS (SELECT 1 FROM teams WHERE "Id" = academy_fmp_id) THEN
                    RAISE EXCEPTION 'Cannot reconcile FMP identities: Akademija FMP target is missing.';
                END IF;

                SELECT COUNT(*) INTO duplicate_game_count
                FROM games
                WHERE "Source" = 'fiba'
                  AND "SourceGameId" IN (
                      'wiki-fiba-5409c4a5d408617042b9',
                      'wiki-fiba-0ef98bec6edaf3799beb',
                      'wiki-fiba-04e799c32bc78afeefc0',
                      'wiki-fiba-a56ea524ebfcfaabc670'
                  );
                IF duplicate_game_count NOT IN (0, 4) THEN
                    RAISE EXCEPTION 'Cannot reconcile FMP identities: expected zero or four inferior duplicate FIBA games, found %.', duplicate_game_count;
                END IF;
                IF EXISTS (
                    SELECT 1
                    FROM model_lab_run_predictions prediction
                    JOIN games game ON game."Id" = prediction."GameId"
                    WHERE game."Source" = 'fiba'
                      AND game."SourceGameId" IN (
                          'wiki-fiba-5409c4a5d408617042b9',
                          'wiki-fiba-0ef98bec6edaf3799beb',
                          'wiki-fiba-04e799c32bc78afeefc0',
                          'wiki-fiba-a56ea524ebfcfaabc670'
                      )
                    UNION ALL
                    SELECT 1
                    FROM model_lab_run_evolution_points point
                    JOIN games game ON game."Id" = point."GameId"
                    WHERE game."Source" = 'fiba'
                      AND game."SourceGameId" IN (
                          'wiki-fiba-5409c4a5d408617042b9',
                          'wiki-fiba-0ef98bec6edaf3799beb',
                          'wiki-fiba-04e799c32bc78afeefc0',
                          'wiki-fiba-a56ea524ebfcfaabc670'
                      )
                ) THEN
                    RAISE EXCEPTION 'Cannot reconcile FMP identities: a saved Model Lab run references an inferior duplicate FIBA game.';
                END IF;
                DELETE FROM games
                WHERE "Source" = 'fiba'
                  AND "SourceGameId" IN (
                      'wiki-fiba-5409c4a5d408617042b9',
                      'wiki-fiba-0ef98bec6edaf3799beb',
                      'wiki-fiba-04e799c32bc78afeefc0',
                      'wiki-fiba-a56ea524ebfcfaabc670'
                  );

                SELECT COUNT(*) INTO split_game_count
                FROM games
                WHERE ("HomeTeamId" = modern_fmp_id OR "AwayTeamId" = modern_fmp_id)
                  AND "GameDateTimeUtc" < '2011-07-01T00:00:00Z'::timestamptz;
                IF split_game_count NOT IN (0, 105) THEN
                    RAISE EXCEPTION 'Cannot reconcile FMP identities: expected zero or 105 pre-July-2011 games on the modern target, found %.', split_game_count;
                END IF;

                UPDATE model_lab_run_evolution_points point
                SET "TeamId" = old_fmp_id,
                    "TeamName" = 'FMP Železnik'
                FROM games game
                WHERE point."GameId" = game."Id"
                  AND point."TeamId" = modern_fmp_id
                  AND (game."HomeTeamId" = modern_fmp_id OR game."AwayTeamId" = modern_fmp_id)
                  AND game."GameDateTimeUtc" < '2011-07-01T00:00:00Z'::timestamptz;

                UPDATE model_lab_run_predictions prediction
                SET "HomeTeamId" = CASE WHEN prediction."HomeTeamId" = modern_fmp_id THEN old_fmp_id ELSE prediction."HomeTeamId" END,
                    "AwayTeamId" = CASE WHEN prediction."AwayTeamId" = modern_fmp_id THEN old_fmp_id ELSE prediction."AwayTeamId" END,
                    "HomeTeamName" = CASE WHEN prediction."HomeTeamId" = modern_fmp_id THEN 'FMP Železnik' ELSE prediction."HomeTeamName" END,
                    "AwayTeamName" = CASE WHEN prediction."AwayTeamId" = modern_fmp_id THEN 'FMP Železnik' ELSE prediction."AwayTeamName" END
                FROM games game
                WHERE prediction."GameId" = game."Id"
                  AND (prediction."HomeTeamId" = modern_fmp_id OR prediction."AwayTeamId" = modern_fmp_id)
                  AND (game."HomeTeamId" = modern_fmp_id OR game."AwayTeamId" = modern_fmp_id)
                  AND game."GameDateTimeUtc" < '2011-07-01T00:00:00Z'::timestamptz;

                UPDATE rating_history history
                SET "TeamId" = CASE WHEN history."TeamId" = modern_fmp_id THEN old_fmp_id ELSE history."TeamId" END,
                    "OpponentTeamId" = CASE WHEN history."OpponentTeamId" = modern_fmp_id THEN old_fmp_id ELSE history."OpponentTeamId" END
                FROM games game
                WHERE history."GameId" = game."Id"
                  AND (history."TeamId" = modern_fmp_id OR history."OpponentTeamId" = modern_fmp_id)
                  AND (game."HomeTeamId" = modern_fmp_id OR game."AwayTeamId" = modern_fmp_id)
                  AND game."GameDateTimeUtc" < '2011-07-01T00:00:00Z'::timestamptz;

                UPDATE games
                SET "HomeTeamId" = CASE WHEN "HomeTeamId" = modern_fmp_id THEN old_fmp_id ELSE "HomeTeamId" END,
                    "AwayTeamId" = CASE WHEN "AwayTeamId" = modern_fmp_id THEN old_fmp_id ELSE "AwayTeamId" END,
                    "UpdatedAtUtc" = NOW()
                WHERE ("HomeTeamId" = modern_fmp_id OR "AwayTeamId" = modern_fmp_id)
                  AND "GameDateTimeUtc" < '2011-07-01T00:00:00Z'::timestamptz;

                FOR merge_record IN
                    SELECT *
                    FROM (VALUES
                        ('30db76ef-b8cd-47a3-9269-daa5457ac58f'::uuid, old_fmp_id, 'BC FMP Zeleznik'),
                        ('444b0e2b-73b3-46ac-b94f-63295c98bcd8'::uuid, old_fmp_id, 'FMP Zeleznik FRY'),
                        ('978cd4b6-281b-4a42-aaf6-07be9b77029d'::uuid, old_fmp_id, 'FMP Zeleznik Belgrad'),
                        ('8b21daff-1e77-4a64-995b-c0c9a8fdf237'::uuid, old_fmp_id, 'KK FMP Zeleznik'),
                        ('64c6fcbd-e2c5-4a85-8d9a-261eb739738a'::uuid, modern_fmp_id, 'KK FMP'),
                        ('5d0fd2a3-7f5f-4857-bd74-576835f873da'::uuid, modern_fmp_id, 'Radnicki FMP')
                    ) AS merges(source_team_id, target_team_id, description)
                LOOP
                    SELECT "CanonicalName" INTO source_team_name FROM teams WHERE "Id" = merge_record.source_team_id;
                    IF source_team_name IS NULL THEN
                        CONTINUE;
                    END IF;
                    SELECT "CanonicalName" INTO target_team_name FROM teams WHERE "Id" = merge_record.target_team_id;
                    IF target_team_name IS NULL THEN
                        RAISE EXCEPTION 'Cannot merge FMP identity %: target team is missing.', merge_record.description;
                    END IF;
                    IF EXISTS (
                        SELECT 1 FROM games
                        WHERE ("HomeTeamId" = merge_record.source_team_id AND "AwayTeamId" = merge_record.target_team_id)
                           OR ("HomeTeamId" = merge_record.target_team_id AND "AwayTeamId" = merge_record.source_team_id)
                    ) THEN
                        RAISE EXCEPTION 'Cannot merge FMP identity %: source and target occur in the same game.', merge_record.description;
                    END IF;

                    DELETE FROM team_aliases source_alias
                    WHERE source_alias."TeamId" = merge_record.source_team_id
                      AND EXISTS (
                          SELECT 1 FROM team_aliases target_alias
                          WHERE target_alias."TeamId" = merge_record.target_team_id
                            AND target_alias."Source" = source_alias."Source"
                            AND target_alias."SourceTeamId" = source_alias."SourceTeamId"
                            AND target_alias."AliasName" = source_alias."AliasName"
                      );
                    UPDATE team_aliases SET "TeamId" = merge_record.target_team_id WHERE "TeamId" = merge_record.source_team_id;

                    DELETE FROM team_search_names source_name
                    WHERE source_name."TeamId" = merge_record.source_team_id
                      AND EXISTS (
                          SELECT 1 FROM team_search_names target_name
                          WHERE target_name."TeamId" = merge_record.target_team_id
                            AND target_name."Locale" = source_name."Locale"
                            AND target_name."NormalizedName" = source_name."NormalizedName"
                      );
                    UPDATE team_search_names SET "TeamId" = merge_record.target_team_id WHERE "TeamId" = merge_record.source_team_id;

                    DELETE FROM model_lab_run_evolution_points source_point
                    WHERE source_point."TeamId" = merge_record.source_team_id
                      AND EXISTS (
                          SELECT 1 FROM model_lab_run_evolution_points target_point
                          WHERE target_point."TeamId" = merge_record.target_team_id
                            AND target_point."RunId" = source_point."RunId"
                            AND target_point."GameId" = source_point."GameId"
                      );
                    UPDATE model_lab_run_evolution_points
                    SET "TeamId" = merge_record.target_team_id,
                        "TeamName" = target_team_name
                    WHERE "TeamId" = merge_record.source_team_id;

                    DELETE FROM model_lab_run_ratings source_rating
                    WHERE source_rating."TeamId" = merge_record.source_team_id
                      AND EXISTS (
                          SELECT 1 FROM model_lab_run_ratings target_rating
                          WHERE target_rating."TeamId" = merge_record.target_team_id
                            AND target_rating."RunId" = source_rating."RunId"
                      );
                    UPDATE model_lab_run_ratings SET "TeamId" = merge_record.target_team_id WHERE "TeamId" = merge_record.source_team_id;

                    UPDATE model_lab_run_predictions
                    SET "HomeTeamName" = CASE WHEN "HomeTeamId" = merge_record.source_team_id THEN target_team_name ELSE "HomeTeamName" END,
                        "AwayTeamName" = CASE WHEN "AwayTeamId" = merge_record.source_team_id THEN target_team_name ELSE "AwayTeamName" END,
                        "HomeTeamId" = CASE WHEN "HomeTeamId" = merge_record.source_team_id THEN merge_record.target_team_id ELSE "HomeTeamId" END,
                        "AwayTeamId" = CASE WHEN "AwayTeamId" = merge_record.source_team_id THEN merge_record.target_team_id ELSE "AwayTeamId" END
                    WHERE "HomeTeamId" = merge_record.source_team_id OR "AwayTeamId" = merge_record.source_team_id;

                    DELETE FROM rating_history source_history
                    WHERE source_history."TeamId" = merge_record.source_team_id
                      AND EXISTS (
                          SELECT 1 FROM rating_history target_history
                          WHERE target_history."TeamId" = merge_record.target_team_id
                            AND target_history."GameId" = source_history."GameId"
                            AND target_history."EloPoolKey" = source_history."EloPoolKey"
                            AND target_history."RulesetVersion" = source_history."RulesetVersion"
                      );
                    UPDATE rating_history
                    SET "TeamId" = CASE WHEN "TeamId" = merge_record.source_team_id THEN merge_record.target_team_id ELSE "TeamId" END,
                        "OpponentTeamId" = CASE WHEN "OpponentTeamId" = merge_record.source_team_id THEN merge_record.target_team_id ELSE "OpponentTeamId" END
                    WHERE "TeamId" = merge_record.source_team_id OR "OpponentTeamId" = merge_record.source_team_id;

                    DELETE FROM team_ratings source_rating
                    WHERE source_rating."TeamId" = merge_record.source_team_id
                      AND EXISTS (
                          SELECT 1 FROM team_ratings target_rating
                          WHERE target_rating."TeamId" = merge_record.target_team_id
                            AND target_rating."EloPoolKey" = source_rating."EloPoolKey"
                            AND target_rating."RulesetVersion" = source_rating."RulesetVersion"
                      );
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
                      AND EXISTS (
                          SELECT 1 FROM identity_review_decisions target_decision
                          WHERE target_decision."DecisionKey" = replace(
                              source_decision."DecisionKey",
                              replace(merge_record.source_team_id::text, '-', ''),
                              replace(merge_record.target_team_id::text, '-', '')
                          )
                      );
                    UPDATE identity_review_decisions
                    SET "AffectedTeamId" = CASE WHEN "AffectedTeamId" = merge_record.source_team_id THEN merge_record.target_team_id ELSE "AffectedTeamId" END,
                        "RelatedTeamId" = CASE WHEN "RelatedTeamId" = merge_record.source_team_id THEN merge_record.target_team_id ELSE "RelatedTeamId" END,
                        "DecisionKey" = replace(
                            "DecisionKey",
                            replace(merge_record.source_team_id::text, '-', ''),
                            replace(merge_record.target_team_id::text, '-', '')
                        )
                    WHERE "AffectedTeamId" = merge_record.source_team_id OR "RelatedTeamId" = merge_record.source_team_id;

                    UPDATE teams SET "PredecessorTeamId" = merge_record.target_team_id WHERE "PredecessorTeamId" = merge_record.source_team_id;
                    UPDATE teams SET "SuccessorTeamId" = merge_record.target_team_id WHERE "SuccessorTeamId" = merge_record.source_team_id;
                    DELETE FROM teams WHERE "Id" = merge_record.source_team_id;
                END LOOP;

                UPDATE teams
                SET "CanonicalName" = 'FMP Železnik',
                    "CountryCode" = 'RS',
                    "IsActive" = FALSE,
                    "Description" = 'Historic Serbian club from Železnik, founded as KK ILR Železnik in 1975. The senior team was inactive from 1986 until its 1991 reactivation as FMP Železnik, competed as Reflex from 2003 to 2005, and again as FMP through 2011. Its senior operation entered the Crvena zvezda reorganisation in 2011. This identity is distinct from the modern FMP descended from Radnički Novi Sad.',
                    "PredecessorTeamId" = NULL,
                    "SuccessorTeamId" = NULL
                WHERE "Id" = old_fmp_id;

                UPDATE teams
                SET "CanonicalName" = 'FMP',
                    "CountryCode" = 'RS',
                    "IsActive" = TRUE,
                    "Description" = 'Serbian club lineage founded in Novi Sad in 1970 as KK Radnički. It competed as Radnički Invest, moved to Belgrade in 2009 as Radnički Basket, became Radnički FMP in 2011, and adopted the FMP name in 2013. It uses the FMP brand and Železnik venue but is kept distinct from the original 1975–2011 FMP Železnik identity.',
                    "PredecessorTeamId" = NULL,
                    "SuccessorTeamId" = NULL
                WHERE "Id" = modern_fmp_id;

                UPDATE teams
                SET "CanonicalName" = 'Akademija FMP Skopje',
                    "CountryCode" = 'MK',
                    "IsActive" = TRUE,
                    "Description" = 'North Macedonian basketball club from Skopje, also known as FMP Akademija. It is unrelated to the Serbian FMP clubs.'
                WHERE "Id" = academy_fmp_id;

                UPDATE team_aliases
                SET "ValidToUtc" = LEAST(COALESCE("ValidToUtc", '2011-08-11T23:59:59Z'::timestamptz), '2011-08-11T23:59:59Z'::timestamptz),
                    "MappingMethod" = COALESCE("MappingMethod", 'curated'),
                    "MappingConfidence" = COALESCE("MappingConfidence", 100)
                WHERE "TeamId" = old_fmp_id;

                UPDATE team_aliases
                SET "ValidFromUtc" = COALESCE("ValidFromUtc", '2013-07-01T00:00:00Z'::timestamptz),
                    "MappingMethod" = COALESCE("MappingMethod", 'curated'),
                    "MappingConfidence" = COALESCE("MappingConfidence", 100)
                WHERE "TeamId" = modern_fmp_id
                  AND "Source" IN ('api-sports', 'global-sports-archive', 'livescore');

                UPDATE team_aliases
                SET "ValidFromUtc" = COALESCE("ValidFromUtc", '2006-07-01T00:00:00Z'::timestamptz),
                    "ValidToUtc" = COALESCE("ValidToUtc", '2013-06-30T23:59:59Z'::timestamptz),
                    "MappingMethod" = COALESCE("MappingMethod", 'curated'),
                    "MappingConfidence" = COALESCE("MappingConfidence", 100)
                WHERE "TeamId" = modern_fmp_id
                  AND "Source" = 'serbian-historical';

                UPDATE team_aliases
                SET "MappingMethod" = COALESCE("MappingMethod", 'curated'),
                    "MappingConfidence" = COALESCE("MappingConfidence", 100)
                WHERE "TeamId" = academy_fmp_id;

                INSERT INTO team_search_names ("Id", "TeamId", "Locale", "Name", "NormalizedName", "CreatedAtUtc") VALUES
                    ('c62ce203-c201-4ef7-8337-824f64a88101', old_fmp_id, 'und', 'FMP Železnik', 'FMPZELEZNIK', NOW()),
                    ('c62ce203-c201-4ef7-8337-824f64a88102', old_fmp_id, 'und', 'KK FMP Železnik', 'KKFMPZELEZNIK', NOW()),
                    ('c62ce203-c201-4ef7-8337-824f64a88103', old_fmp_id, 'und', 'BC FMP Zeleznik', 'BCFMPZELEZNIK', NOW()),
                    ('c62ce203-c201-4ef7-8337-824f64a88104', old_fmp_id, 'und', 'FMP Železnik Belgrad', 'FMPZELEZNIKBELGRAD', NOW()),
                    ('c62ce203-c201-4ef7-8337-824f64a88105', old_fmp_id, 'und', 'Reflex', 'REFLEX', NOW()),
                    ('c62ce203-c201-4ef7-8337-824f64a88106', old_fmp_id, 'und', 'KK Reflex', 'KKREFLEX', NOW()),
                    ('c62ce203-c201-4ef7-8337-824f64a88107', old_fmp_id, 'und', 'KK ILR Železnik', 'KKILRZELEZNIK', NOW()),
                    ('c62ce203-c201-4ef7-8337-824f64a88108', old_fmp_id, 'und', 'FMP', 'FMP', NOW()),
                    ('c62ce203-c201-4ef7-8337-824f64a88109', modern_fmp_id, 'und', 'FMP', 'FMP', NOW()),
                    ('c62ce203-c201-4ef7-8337-824f64a8810a', modern_fmp_id, 'und', 'KK FMP', 'KKFMP', NOW()),
                    ('c62ce203-c201-4ef7-8337-824f64a8810b', modern_fmp_id, 'und', 'FMP Beograd', 'FMPBEOGRAD', NOW()),
                    ('c62ce203-c201-4ef7-8337-824f64a8810c', modern_fmp_id, 'und', 'Radnički FMP', 'RADNICKIFMP', NOW()),
                    ('c62ce203-c201-4ef7-8337-824f64a8810d', modern_fmp_id, 'und', 'Radnički Basket', 'RADNICKIBASKET', NOW()),
                    ('c62ce203-c201-4ef7-8337-824f64a8810e', modern_fmp_id, 'und', 'Radnički Invest', 'RADNICKIINVEST', NOW()),
                    ('c62ce203-c201-4ef7-8337-824f64a8810f', modern_fmp_id, 'und', 'Radnički Novi Sad', 'RADNICKINOVISAD', NOW()),
                    ('c62ce203-c201-4ef7-8337-824f64a88110', academy_fmp_id, 'und', 'Akademija FMP Skopje', 'AKADEMIJAFMPSKOPJE', NOW()),
                    ('c62ce203-c201-4ef7-8337-824f64a88111', academy_fmp_id, 'und', 'FMP Akademija', 'FMPAKADEMIJA', NOW())
                ON CONFLICT ("TeamId", "Locale", "NormalizedName") DO UPDATE
                SET "Name" = EXCLUDED."Name";

                INSERT INTO identity_review_decisions (
                    "Id", "DecisionKey", "FindingType", "ResolutionAction",
                    "AffectedTeamId", "RelatedTeamId", "Note", "CreatedBy", "CreatedAtUtc")
                VALUES (
                    'c62ce203-c201-4ef7-8337-824f64a88201',
                    'alias_collision|1dc6ba735b50cd5e8054efceb27b3098e2c8a74e34ba31c3ce80e20186596b23',
                    'alias_collision', 'accept_alias_group', old_fmp_id, modern_fmp_id,
                    'Intentional historical alias overlap: the original FMP Železnik ended as an independent senior identity in 2011; the Radnički-lineage club adopted FMP in 2013.',
                    'curated-migration', NOW())
                ON CONFLICT ("DecisionKey") DO UPDATE
                SET "AffectedTeamId" = EXCLUDED."AffectedTeamId",
                    "RelatedTeamId" = EXCLUDED."RelatedTeamId",
                    "Note" = EXCLUDED."Note";

                INSERT INTO identity_review_decisions (
                    "Id", "DecisionKey", "FindingType", "ResolutionAction",
                    "AffectedTeamId", "RelatedTeamId", "Note", "CreatedBy", "CreatedAtUtc")
                VALUES (
                    'c62ce203-c201-4ef7-8337-824f64a88202',
                    'distinct_teams|teams=1bdae3713cdb41b78a7dcac610023f94:5f4f1b1972844dfca231baefd6b871bb',
                    'possible_cross_source_match', 'keep_separate', old_fmp_id, modern_fmp_id,
                    'The original 1975–2011 FMP Železnik and the modern Radnički-lineage FMP are distinct club identities despite their shared name and venue.',
                    'curated-migration', NOW())
                ON CONFLICT ("DecisionKey") DO UPDATE
                SET "AffectedTeamId" = EXCLUDED."AffectedTeamId",
                    "RelatedTeamId" = EXCLUDED."RelatedTeamId",
                    "Note" = EXCLUDED."Note";

                INSERT INTO elo_rebuild_runs (
                    "Id", "QueuedAtUtc", "RulesetVersion", "EloPoolKey", "CompetitionName",
                    "Status", "GamesProcessed", "TeamsRated", "Notes", "CreatedAtUtc")
                SELECT gen_random_uuid(), NOW(), ruleset.version, 'europe-clubs', '',
                    'pending', 0, 0,
                    'Queued after reconciling the original and modern FMP club identities.', NOW()
                FROM (VALUES ('adjusted-v1'), ('basic-elo-v1')) AS ruleset(version)
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM elo_rebuild_runs active
                    WHERE active."EloPoolKey" = 'europe-clubs'
                      AND active."RulesetVersion" = ruleset.version
                      AND active."Status" IN ('pending', 'running')
                );
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The merged identities, deleted duplicate games, and corrected
        // historical split cannot be reversed safely without the backup.
    }
}
