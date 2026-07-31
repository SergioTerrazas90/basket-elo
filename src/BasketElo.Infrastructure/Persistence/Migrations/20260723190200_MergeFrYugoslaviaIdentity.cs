using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MergeFrYugoslaviaIdentity : Migration
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
                    SELECT duplicate."Id"
                    INTO duplicate_id
                    FROM teams duplicate
                    WHERE duplicate."CanonicalName" = 'FR Yugoslavia'
                       OR duplicate."CountryCode" = 'SMN'
                    ORDER BY duplicate."Id"
                    LIMIT 1;

                    SELECT target."Id"
                    INTO target_id
                    FROM teams target
                    WHERE target."CanonicalName" = 'Serbia and Montenegro'
                      AND target."CountryCode" = 'SCG'
                    LIMIT 1;

                    IF duplicate_id IS NOT NULL AND target_id IS NOT NULL AND duplicate_id <> target_id THEN
                        DELETE FROM team_ratings duplicate_rating
                        WHERE duplicate_rating."TeamId" = duplicate_id
                          AND EXISTS (
                              SELECT 1 FROM team_ratings target_rating
                              WHERE target_rating."TeamId" = target_id
                                AND target_rating."EloPoolKey" = duplicate_rating."EloPoolKey"
                                AND target_rating."RulesetVersion" = duplicate_rating."RulesetVersion");

                        DELETE FROM model_lab_run_ratings duplicate_rating
                        WHERE duplicate_rating."TeamId" = duplicate_id
                          AND EXISTS (
                              SELECT 1 FROM model_lab_run_ratings target_rating
                              WHERE target_rating."TeamId" = target_id
                                AND target_rating."RunId" = duplicate_rating."RunId");

                        DELETE FROM team_aliases duplicate_alias
                        WHERE duplicate_alias."TeamId" = duplicate_id
                          AND EXISTS (
                              SELECT 1 FROM team_aliases target_alias
                              WHERE target_alias."TeamId" = target_id
                                AND target_alias."Source" = duplicate_alias."Source"
                                AND target_alias."SourceTeamId" = duplicate_alias."SourceTeamId"
                                AND target_alias."AliasName" = duplicate_alias."AliasName");

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

                        UPDATE team_aliases
                        SET "TeamId" = target_id
                        WHERE "TeamId" = duplicate_id;

                        UPDATE team_ratings
                        SET "TeamId" = target_id
                        WHERE "TeamId" = duplicate_id;

                        UPDATE model_lab_run_ratings
                        SET "TeamId" = target_id
                        WHERE "TeamId" = duplicate_id;

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
