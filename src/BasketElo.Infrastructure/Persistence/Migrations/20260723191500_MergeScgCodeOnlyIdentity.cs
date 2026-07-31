using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MergeScgCodeOnlyIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    duplicate_id uuid;
                    target_id uuid;
                BEGIN
                    SELECT "Id" INTO duplicate_id
                    FROM teams
                    WHERE "CanonicalName" = 'SCG' AND "CountryCode" = 'SCG'
                    LIMIT 1;

                    SELECT "Id" INTO target_id
                    FROM teams
                    WHERE "CanonicalName" = 'Serbia and Montenegro' AND "CountryCode" = 'SCG'
                    LIMIT 1;

                    IF duplicate_id IS NOT NULL AND target_id IS NOT NULL AND duplicate_id <> target_id THEN
                        DELETE FROM team_ratings d
                        WHERE d."TeamId" = duplicate_id
                          AND EXISTS (SELECT 1 FROM team_ratings t
                                      WHERE t."TeamId" = target_id
                                        AND t."EloPoolKey" = d."EloPoolKey"
                                        AND t."RulesetVersion" = d."RulesetVersion");

                        DELETE FROM model_lab_run_ratings d
                        WHERE d."TeamId" = duplicate_id
                          AND EXISTS (SELECT 1 FROM model_lab_run_ratings t
                                      WHERE t."TeamId" = target_id AND t."RunId" = d."RunId");

                        DELETE FROM team_aliases d
                        WHERE d."TeamId" = duplicate_id
                          AND EXISTS (SELECT 1 FROM team_aliases t
                                      WHERE t."TeamId" = target_id
                                        AND t."Source" = d."Source"
                                        AND t."SourceTeamId" = d."SourceTeamId"
                                        AND t."AliasName" = d."AliasName");

                        UPDATE games
                        SET "HomeTeamId" = CASE WHEN "HomeTeamId" = duplicate_id THEN target_id ELSE "HomeTeamId" END,
                            "AwayTeamId" = CASE WHEN "AwayTeamId" = duplicate_id THEN target_id ELSE "AwayTeamId" END
                        WHERE "HomeTeamId" = duplicate_id OR "AwayTeamId" = duplicate_id;

                        UPDATE rating_history
                        SET "TeamId" = CASE WHEN "TeamId" = duplicate_id THEN target_id ELSE "TeamId" END,
                            "OpponentTeamId" = CASE WHEN "OpponentTeamId" = duplicate_id THEN target_id ELSE "OpponentTeamId" END
                        WHERE "TeamId" = duplicate_id OR "OpponentTeamId" = duplicate_id;

                        UPDATE model_lab_run_predictions
                        SET "HomeTeamId" = CASE WHEN "HomeTeamId" = duplicate_id THEN target_id ELSE "HomeTeamId" END,
                            "AwayTeamId" = CASE WHEN "AwayTeamId" = duplicate_id THEN target_id ELSE "AwayTeamId" END
                        WHERE "HomeTeamId" = duplicate_id OR "AwayTeamId" = duplicate_id;

                        UPDATE identity_health_check_findings
                        SET "AffectedTeamId" = CASE WHEN "AffectedTeamId" = duplicate_id THEN target_id ELSE "AffectedTeamId" END,
                            "RelatedTeamId" = CASE WHEN "RelatedTeamId" = duplicate_id THEN target_id ELSE "RelatedTeamId" END
                        WHERE "AffectedTeamId" = duplicate_id OR "RelatedTeamId" = duplicate_id;

                        UPDATE identity_review_decisions
                        SET "AffectedTeamId" = CASE WHEN "AffectedTeamId" = duplicate_id THEN target_id ELSE "AffectedTeamId" END,
                            "RelatedTeamId" = CASE WHEN "RelatedTeamId" = duplicate_id THEN target_id ELSE "RelatedTeamId" END
                        WHERE "AffectedTeamId" = duplicate_id OR "RelatedTeamId" = duplicate_id;

                        UPDATE team_aliases SET "TeamId" = target_id WHERE "TeamId" = duplicate_id;
                        UPDATE team_ratings SET "TeamId" = target_id WHERE "TeamId" = duplicate_id;
                        UPDATE model_lab_run_ratings SET "TeamId" = target_id WHERE "TeamId" = duplicate_id;
                        DELETE FROM teams WHERE "Id" = duplicate_id;
                    END IF;
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
