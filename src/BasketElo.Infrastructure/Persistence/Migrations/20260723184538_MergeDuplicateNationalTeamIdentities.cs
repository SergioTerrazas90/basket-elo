using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MergeDuplicateNationalTeamIdentities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    merge_record record;
                BEGIN
                    FOR merge_record IN
                        WITH national_team_stats AS (
                            SELECT t."Id",
                                   t."CanonicalName",
                                   count(*) AS "NationalGames"
                            FROM teams t
                            INNER JOIN (
                                SELECT "HomeTeamId" AS "TeamId", "CompetitionId" FROM games
                                UNION ALL
                                SELECT "AwayTeamId" AS "TeamId", "CompetitionId" FROM games
                            ) appearances ON appearances."TeamId" = t."Id"
                            INNER JOIN competitions c ON c."Id" = appearances."CompetitionId"
                            WHERE c."EloPoolKey" = 'national-teams'
                            GROUP BY t."Id", t."CanonicalName"
                        ), selected_targets AS (
                            SELECT d."Id" AS duplicate_id,
                                   (
                                       SELECT target."Id"
                                       FROM national_team_stats target
                                       WHERE target."CanonicalName" = d."CanonicalName"
                                       ORDER BY target."NationalGames" DESC, target."Id"
                                       LIMIT 1
                                   ) AS target_id
                            FROM national_team_stats d
                        )
                        SELECT duplicate_id, target_id
                        FROM selected_targets
                        WHERE duplicate_id <> target_id
                    LOOP
                        DELETE FROM team_ratings duplicate_rating
                        WHERE duplicate_rating."TeamId" = merge_record.duplicate_id
                          AND EXISTS (
                              SELECT 1 FROM team_ratings canonical_rating
                              WHERE canonical_rating."TeamId" = merge_record.target_id
                                AND canonical_rating."EloPoolKey" = duplicate_rating."EloPoolKey"
                                AND canonical_rating."RulesetVersion" = duplicate_rating."RulesetVersion"
                          );

                        DELETE FROM model_lab_run_ratings duplicate_rating
                        WHERE duplicate_rating."TeamId" = merge_record.duplicate_id
                          AND EXISTS (
                              SELECT 1 FROM model_lab_run_ratings canonical_rating
                              WHERE canonical_rating."TeamId" = merge_record.target_id
                                AND canonical_rating."RunId" = duplicate_rating."RunId"
                          );

                        DELETE FROM team_aliases duplicate_alias
                        WHERE duplicate_alias."TeamId" = merge_record.duplicate_id
                          AND EXISTS (
                              SELECT 1 FROM team_aliases canonical_alias
                              WHERE canonical_alias."TeamId" = merge_record.target_id
                                AND canonical_alias."Source" = duplicate_alias."Source"
                                AND canonical_alias."SourceTeamId" = duplicate_alias."SourceTeamId"
                                AND canonical_alias."AliasName" = duplicate_alias."AliasName"
                          );

                        UPDATE games
                        SET "HomeTeamId" = CASE WHEN "HomeTeamId" = merge_record.duplicate_id THEN merge_record.target_id ELSE "HomeTeamId" END,
                            "AwayTeamId" = CASE WHEN "AwayTeamId" = merge_record.duplicate_id THEN merge_record.target_id ELSE "AwayTeamId" END
                        WHERE "HomeTeamId" = merge_record.duplicate_id OR "AwayTeamId" = merge_record.duplicate_id;

                        UPDATE rating_history
                        SET "TeamId" = CASE WHEN "TeamId" = merge_record.duplicate_id THEN merge_record.target_id ELSE "TeamId" END,
                            "OpponentTeamId" = CASE WHEN "OpponentTeamId" = merge_record.duplicate_id THEN merge_record.target_id ELSE "OpponentTeamId" END
                        WHERE "TeamId" = merge_record.duplicate_id OR "OpponentTeamId" = merge_record.duplicate_id;

                        UPDATE model_lab_run_predictions
                        SET "HomeTeamId" = CASE WHEN "HomeTeamId" = merge_record.duplicate_id THEN merge_record.target_id ELSE "HomeTeamId" END,
                            "AwayTeamId" = CASE WHEN "AwayTeamId" = merge_record.duplicate_id THEN merge_record.target_id ELSE "AwayTeamId" END,
                            "HomeTeamName" = CASE WHEN "HomeTeamId" = merge_record.duplicate_id THEN target_team."CanonicalName" ELSE "HomeTeamName" END,
                            "AwayTeamName" = CASE WHEN "AwayTeamId" = merge_record.duplicate_id THEN target_team."CanonicalName" ELSE "AwayTeamName" END
                        FROM teams target_team
                        WHERE target_team."Id" = merge_record.target_id
                          AND ("HomeTeamId" = merge_record.duplicate_id OR "AwayTeamId" = merge_record.duplicate_id);

                        UPDATE identity_health_check_findings
                        SET "AffectedTeamId" = CASE WHEN "AffectedTeamId" = merge_record.duplicate_id THEN merge_record.target_id ELSE "AffectedTeamId" END,
                            "RelatedTeamId" = CASE WHEN "RelatedTeamId" = merge_record.duplicate_id THEN merge_record.target_id ELSE "RelatedTeamId" END
                        WHERE "AffectedTeamId" = merge_record.duplicate_id OR "RelatedTeamId" = merge_record.duplicate_id;

                        UPDATE identity_review_decisions
                        SET "AffectedTeamId" = CASE WHEN "AffectedTeamId" = merge_record.duplicate_id THEN merge_record.target_id ELSE "AffectedTeamId" END,
                            "RelatedTeamId" = CASE WHEN "RelatedTeamId" = merge_record.duplicate_id THEN merge_record.target_id ELSE "RelatedTeamId" END
                        WHERE "AffectedTeamId" = merge_record.duplicate_id OR "RelatedTeamId" = merge_record.duplicate_id;

                        UPDATE team_aliases
                        SET "TeamId" = merge_record.target_id
                        WHERE "TeamId" = merge_record.duplicate_id;

                        UPDATE team_ratings
                        SET "TeamId" = merge_record.target_id
                        WHERE "TeamId" = merge_record.duplicate_id;

                        UPDATE model_lab_run_ratings
                        SET "TeamId" = merge_record.target_id
                        WHERE "TeamId" = merge_record.duplicate_id;

                        DELETE FROM teams WHERE "Id" = merge_record.duplicate_id;
                    END LOOP;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Team merges are intentionally not reversed because source
            // aliases and rebuilt ratings depend on the canonical identity.
        }
    }
}
