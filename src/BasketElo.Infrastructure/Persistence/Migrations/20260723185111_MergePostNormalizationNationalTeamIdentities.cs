using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MergePostNormalizationNationalTeamIdentities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                DECLARE merge_record record;
                BEGIN
                    FOR merge_record IN
                        WITH stats AS (
                            SELECT t."Id", t."CanonicalName", count(*) AS games
                            FROM teams t
                            INNER JOIN (
                                SELECT "HomeTeamId" AS "TeamId", "CompetitionId" FROM games
                                UNION ALL
                                SELECT "AwayTeamId" AS "TeamId", "CompetitionId" FROM games
                            ) appearances ON appearances."TeamId" = t."Id"
                            INNER JOIN competitions c ON c."Id" = appearances."CompetitionId"
                            WHERE c."EloPoolKey" = 'national-teams'
                            GROUP BY t."Id", t."CanonicalName"
                        )
                        SELECT duplicate."Id" AS duplicate_id,
                               (SELECT target."Id" FROM stats target
                                WHERE target."CanonicalName" = duplicate."CanonicalName"
                                ORDER BY target.games DESC, target."Id" LIMIT 1) AS target_id
                        FROM stats duplicate
                        WHERE duplicate."Id" <> (SELECT target."Id" FROM stats target
                            WHERE target."CanonicalName" = duplicate."CanonicalName"
                            ORDER BY target.games DESC, target."Id" LIMIT 1)
                    LOOP
                        DELETE FROM team_ratings d
                        WHERE d."TeamId" = merge_record.duplicate_id
                          AND EXISTS (SELECT 1 FROM team_ratings t
                                      WHERE t."TeamId" = merge_record.target_id
                                        AND t."EloPoolKey" = d."EloPoolKey"
                                        AND t."RulesetVersion" = d."RulesetVersion");

                        DELETE FROM model_lab_run_ratings d
                        WHERE d."TeamId" = merge_record.duplicate_id
                          AND EXISTS (SELECT 1 FROM model_lab_run_ratings t
                                      WHERE t."TeamId" = merge_record.target_id AND t."RunId" = d."RunId");

                        DELETE FROM team_aliases d
                        WHERE d."TeamId" = merge_record.duplicate_id
                          AND EXISTS (SELECT 1 FROM team_aliases t
                                      WHERE t."TeamId" = merge_record.target_id
                                        AND t."Source" = d."Source"
                                        AND t."SourceTeamId" = d."SourceTeamId"
                                        AND t."AliasName" = d."AliasName");

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
                            "AwayTeamId" = CASE WHEN "AwayTeamId" = merge_record.duplicate_id THEN merge_record.target_id ELSE "AwayTeamId" END
                        WHERE "HomeTeamId" = merge_record.duplicate_id OR "AwayTeamId" = merge_record.duplicate_id;

                        UPDATE identity_health_check_findings
                        SET "AffectedTeamId" = CASE WHEN "AffectedTeamId" = merge_record.duplicate_id THEN merge_record.target_id ELSE "AffectedTeamId" END,
                            "RelatedTeamId" = CASE WHEN "RelatedTeamId" = merge_record.duplicate_id THEN merge_record.target_id ELSE "RelatedTeamId" END
                        WHERE "AffectedTeamId" = merge_record.duplicate_id OR "RelatedTeamId" = merge_record.duplicate_id;

                        UPDATE identity_review_decisions
                        SET "AffectedTeamId" = CASE WHEN "AffectedTeamId" = merge_record.duplicate_id THEN merge_record.target_id ELSE "AffectedTeamId" END,
                            "RelatedTeamId" = CASE WHEN "RelatedTeamId" = merge_record.duplicate_id THEN merge_record.target_id ELSE "RelatedTeamId" END
                        WHERE "AffectedTeamId" = merge_record.duplicate_id OR "RelatedTeamId" = merge_record.duplicate_id;

                        UPDATE team_aliases SET "TeamId" = merge_record.target_id
                        WHERE "TeamId" = merge_record.duplicate_id;
                        UPDATE team_ratings SET "TeamId" = merge_record.target_id
                        WHERE "TeamId" = merge_record.duplicate_id;
                        UPDATE model_lab_run_ratings SET "TeamId" = merge_record.target_id
                        WHERE "TeamId" = merge_record.duplicate_id;
                        DELETE FROM teams WHERE "Id" = merge_record.duplicate_id;
                    END LOOP;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Canonical team merges are intentionally irreversible.
        }
    }
}
