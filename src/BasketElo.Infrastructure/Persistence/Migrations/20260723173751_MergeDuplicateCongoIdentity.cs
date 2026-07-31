using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MergeDuplicateCongoIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    canonical_team_id uuid;
                    duplicate_team_id uuid;
                BEGIN
                    SELECT "Id" INTO canonical_team_id
                    FROM teams
                    WHERE "CountryCode" = 'CGO'
                    ORDER BY "Id"
                    LIMIT 1;

                    SELECT "Id" INTO duplicate_team_id
                    FROM teams
                    WHERE "CountryCode" = 'CON'
                    ORDER BY "Id"
                    LIMIT 1;

                    IF canonical_team_id IS NOT NULL AND duplicate_team_id IS NOT NULL
                       AND canonical_team_id <> duplicate_team_id THEN
                        DELETE FROM team_ratings duplicate_rating
                        WHERE duplicate_rating."TeamId" = duplicate_team_id
                          AND EXISTS (
                              SELECT 1 FROM team_ratings canonical_rating
                              WHERE canonical_rating."TeamId" = canonical_team_id
                                AND canonical_rating."EloPoolKey" = duplicate_rating."EloPoolKey"
                                AND canonical_rating."RulesetVersion" = duplicate_rating."RulesetVersion"
                          );

                        DELETE FROM model_lab_run_ratings duplicate_rating
                        WHERE duplicate_rating."TeamId" = duplicate_team_id
                          AND EXISTS (
                              SELECT 1 FROM model_lab_run_ratings canonical_rating
                              WHERE canonical_rating."TeamId" = canonical_team_id
                                AND canonical_rating."RunId" = duplicate_rating."RunId"
                          );

                        UPDATE games
                        SET "HomeTeamId" = CASE WHEN "HomeTeamId" = duplicate_team_id THEN canonical_team_id ELSE "HomeTeamId" END,
                            "AwayTeamId" = CASE WHEN "AwayTeamId" = duplicate_team_id THEN canonical_team_id ELSE "AwayTeamId" END
                        WHERE "HomeTeamId" = duplicate_team_id OR "AwayTeamId" = duplicate_team_id;

                        UPDATE rating_history
                        SET "TeamId" = CASE WHEN "TeamId" = duplicate_team_id THEN canonical_team_id ELSE "TeamId" END,
                            "OpponentTeamId" = CASE WHEN "OpponentTeamId" = duplicate_team_id THEN canonical_team_id ELSE "OpponentTeamId" END
                        WHERE "TeamId" = duplicate_team_id OR "OpponentTeamId" = duplicate_team_id;

                        UPDATE model_lab_run_predictions
                        SET "HomeTeamId" = CASE WHEN "HomeTeamId" = duplicate_team_id THEN canonical_team_id ELSE "HomeTeamId" END,
                            "AwayTeamId" = CASE WHEN "AwayTeamId" = duplicate_team_id THEN canonical_team_id ELSE "AwayTeamId" END,
                            "HomeTeamName" = CASE WHEN "HomeTeamId" = duplicate_team_id THEN 'Republic of the Congo' ELSE "HomeTeamName" END,
                            "AwayTeamName" = CASE WHEN "AwayTeamId" = duplicate_team_id THEN 'Republic of the Congo' ELSE "AwayTeamName" END
                        WHERE "HomeTeamId" = duplicate_team_id OR "AwayTeamId" = duplicate_team_id;

                        UPDATE identity_health_check_findings
                        SET "AffectedTeamId" = CASE WHEN "AffectedTeamId" = duplicate_team_id THEN canonical_team_id ELSE "AffectedTeamId" END,
                            "RelatedTeamId" = CASE WHEN "RelatedTeamId" = duplicate_team_id THEN canonical_team_id ELSE "RelatedTeamId" END
                        WHERE "AffectedTeamId" = duplicate_team_id OR "RelatedTeamId" = duplicate_team_id;

                        UPDATE identity_review_decisions
                        SET "AffectedTeamId" = CASE WHEN "AffectedTeamId" = duplicate_team_id THEN canonical_team_id ELSE "AffectedTeamId" END,
                            "RelatedTeamId" = CASE WHEN "RelatedTeamId" = duplicate_team_id THEN canonical_team_id ELSE "RelatedTeamId" END
                        WHERE "AffectedTeamId" = duplicate_team_id OR "RelatedTeamId" = duplicate_team_id;

                        UPDATE team_aliases
                        SET "TeamId" = canonical_team_id
                        WHERE "TeamId" = duplicate_team_id;

                        UPDATE team_ratings
                        SET "TeamId" = canonical_team_id
                        WHERE "TeamId" = duplicate_team_id;

                        UPDATE model_lab_run_ratings
                        SET "TeamId" = canonical_team_id
                        WHERE "TeamId" = duplicate_team_id;

                        DELETE FROM teams WHERE "Id" = duplicate_team_id;
                    END IF;

                    UPDATE teams
                    SET "CanonicalName" = 'Republic of the Congo',
                        "CountryCode" = 'CGO'
                    WHERE "Id" = canonical_team_id;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Source aliases remain attached to the canonical identity and are
            // intentionally not split back into duplicate teams.
        }
    }
}
