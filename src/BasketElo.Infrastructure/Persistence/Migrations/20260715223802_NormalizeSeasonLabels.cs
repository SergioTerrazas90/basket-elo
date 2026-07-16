using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeSeasonLabels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION basketelo_full_season_label(season_label text)
                RETURNS text
                LANGUAGE sql
                IMMUTABLE
                AS $$
                    SELECT CASE
                        WHEN season_label ~ '^[0-9]{4}$'
                            THEN season_label || '-' || ((season_label::int + 1)::text)
                        ELSE season_label
                    END
                $$;
                """);

            migrationBuilder.Sql("""
                WITH duplicate_seasons AS (
                    SELECT legacy."Id" AS legacy_id,
                           canonical."Id" AS canonical_id
                    FROM seasons legacy
                    JOIN seasons canonical
                      ON canonical."CompetitionId" = legacy."CompetitionId"
                     AND canonical."Label" = basketelo_full_season_label(legacy."Label")
                    WHERE legacy."Label" ~ '^[0-9]{4}$'
                )
                UPDATE games
                SET "SeasonId" = duplicate_seasons.canonical_id
                FROM duplicate_seasons
                WHERE games."SeasonId" = duplicate_seasons.legacy_id;
                """);

            migrationBuilder.Sql("""
                WITH duplicate_seasons AS (
                    SELECT legacy."Id" AS legacy_id
                    FROM seasons legacy
                    JOIN seasons canonical
                      ON canonical."CompetitionId" = legacy."CompetitionId"
                     AND canonical."Label" = basketelo_full_season_label(legacy."Label")
                    WHERE legacy."Label" ~ '^[0-9]{4}$'
                )
                DELETE FROM seasons
                USING duplicate_seasons
                WHERE seasons."Id" = duplicate_seasons.legacy_id;
                """);

            migrationBuilder.Sql("""
                UPDATE seasons
                SET "Label" = basketelo_full_season_label("Label"),
                    "StartDateUtc" = make_timestamptz("Label"::int, 7, 1, 0, 0, 0),
                    "EndDateUtc" = make_timestamptz("Label"::int + 1, 6, 30, 23, 59, 59)
                WHERE "Label" ~ '^[0-9]{4}$';
                """);

            migrationBuilder.Sql("""
                DELETE FROM backfill_inspection_decisions legacy
                USING backfill_inspection_decisions canonical
                WHERE legacy."Season" ~ '^[0-9]{4}$'
                  AND canonical."Provider" = legacy."Provider"
                  AND canonical."Country" = legacy."Country"
                  AND canonical."LeagueName" = legacy."LeagueName"
                  AND canonical."Season" = basketelo_full_season_label(legacy."Season");
                """);

            migrationBuilder.Sql("""
                UPDATE backfill_inspection_decisions
                SET "Season" = basketelo_full_season_label("Season")
                WHERE "Season" ~ '^[0-9]{4}$';
                """);

            migrationBuilder.Sql("""
                UPDATE backfill_jobs
                SET "Season" = basketelo_full_season_label("Season")
                WHERE "Season" ~ '^[0-9]{4}$';
                """);

            migrationBuilder.Sql("""
                UPDATE backfill_jobs
                SET "SummaryJson" = jsonb_set("SummaryJson", '{Season}', to_jsonb(basketelo_full_season_label("SummaryJson"->>'Season')), false)
                WHERE "SummaryJson" IS NOT NULL
                  AND "SummaryJson" ? 'Season'
                  AND "SummaryJson"->>'Season' ~ '^[0-9]{4}$';
                """);

            migrationBuilder.Sql("""
                UPDATE backfill_jobs
                SET "SummaryJson" = jsonb_set("SummaryJson", '{season}', to_jsonb(basketelo_full_season_label("SummaryJson"->>'season')), false)
                WHERE "SummaryJson" IS NOT NULL
                  AND "SummaryJson" ? 'season'
                  AND "SummaryJson"->>'season' ~ '^[0-9]{4}$';
                """);

            migrationBuilder.Sql("""
                UPDATE identity_health_check_runs
                SET "Season" = basketelo_full_season_label("Season")
                WHERE "Season" IS NOT NULL
                  AND "Season" ~ '^[0-9]{4}$';
                """);

            migrationBuilder.Sql("""
                WITH scoped_runs AS (
                    SELECT "Id",
                           substring("ScopeKey" from 'season=([0-9]{4})(\||$)') AS season_year
                    FROM identity_health_check_runs
                    WHERE "ScopeKey" ~ 'season=[0-9]{4}(\||$)'
                )
                UPDATE identity_health_check_runs
                SET "ScopeKey" = replace(
                    identity_health_check_runs."ScopeKey",
                    'season=' || scoped_runs.season_year,
                    'season=' || basketelo_full_season_label(scoped_runs.season_year))
                FROM scoped_runs
                WHERE identity_health_check_runs."Id" = scoped_runs."Id";
                """);

            migrationBuilder.Sql("""
                UPDATE identity_health_check_findings
                SET "Season" = basketelo_full_season_label("Season")
                WHERE "Season" IS NOT NULL
                  AND "Season" ~ '^[0-9]{4}$';
                """);

            migrationBuilder.Sql("""
                UPDATE model_lab_run_predictions
                SET "Season" = basketelo_full_season_label("Season")
                WHERE "Season" ~ '^[0-9]{4}$';
                """);

            migrationBuilder.Sql("""
                WITH pairs AS (
                    SELECT canonical."Id" AS canonical_id,
                           legacy."Id" AS legacy_id,
                           canonical."ScoredGames" + legacy."ScoredGames" AS scored_games,
                           canonical."CorrectWinners" + legacy."CorrectWinners" AS correct_winners,
                           canonical."BaselineScoredGames" + legacy."BaselineScoredGames" AS baseline_scored_games,
                           canonical."BaselineCorrectWinners" + legacy."BaselineCorrectWinners" AS baseline_correct_winners,
                           (canonical."BrierScore" * canonical."ScoredGames") + (legacy."BrierScore" * legacy."ScoredGames") AS brier_score_sum,
                           (canonical."LogLoss" * canonical."ScoredGames") + (legacy."LogLoss" * legacy."ScoredGames") AS log_loss_sum,
                           (canonical."AverageMarginError" * canonical."ScoredGames") + (legacy."AverageMarginError" * legacy."ScoredGames") AS margin_error_sum,
                           (canonical."AveragePredictedHomeWinProbability" * canonical."ScoredGames") + (legacy."AveragePredictedHomeWinProbability" * legacy."ScoredGames") AS probability_sum,
                           (canonical."BaselineBrierScore" * canonical."BaselineScoredGames") + (legacy."BaselineBrierScore" * legacy."BaselineScoredGames") AS baseline_brier_score_sum,
                           (canonical."BaselineLogLoss" * canonical."BaselineScoredGames") + (legacy."BaselineLogLoss" * legacy."BaselineScoredGames") AS baseline_log_loss_sum,
                           (canonical."BaselineAverageMarginError" * canonical."BaselineScoredGames") + (legacy."BaselineAverageMarginError" * legacy."BaselineScoredGames") AS baseline_margin_error_sum,
                           (canonical."BaselineAveragePredictedHomeWinProbability" * canonical."BaselineScoredGames") + (legacy."BaselineAveragePredictedHomeWinProbability" * legacy."BaselineScoredGames") AS baseline_probability_sum
                    FROM model_lab_run_metric_breakdowns legacy
                    JOIN model_lab_run_metric_breakdowns canonical
                      ON canonical."RunId" = legacy."RunId"
                     AND canonical."SegmentType" = legacy."SegmentType"
                     AND canonical."SegmentKey" = basketelo_full_season_label(legacy."SegmentKey")
                    WHERE legacy."SegmentType" = 'season'
                      AND legacy."SegmentKey" ~ '^[0-9]{4}$'
                )
                UPDATE model_lab_run_metric_breakdowns canonical
                SET "ScoredGames" = pairs.scored_games,
                    "CorrectWinners" = pairs.correct_winners,
                    "WinnerAccuracy" = CASE WHEN pairs.scored_games = 0 THEN 0 ELSE round((pairs.correct_winners::numeric / pairs.scored_games) * 100, 2) END,
                    "BrierScore" = CASE WHEN pairs.scored_games = 0 THEN 0 ELSE round(pairs.brier_score_sum / pairs.scored_games, 4) END,
                    "LogLoss" = CASE WHEN pairs.scored_games = 0 THEN 0 ELSE round(pairs.log_loss_sum / pairs.scored_games, 4) END,
                    "AverageMarginError" = CASE WHEN pairs.scored_games = 0 THEN 0 ELSE round(pairs.margin_error_sum / pairs.scored_games, 2) END,
                    "AveragePredictedHomeWinProbability" = CASE WHEN pairs.scored_games = 0 THEN 0 ELSE round(pairs.probability_sum / pairs.scored_games, 2) END,
                    "BaselineScoredGames" = pairs.baseline_scored_games,
                    "BaselineCorrectWinners" = pairs.baseline_correct_winners,
                    "BaselineWinnerAccuracy" = CASE WHEN pairs.baseline_scored_games = 0 THEN 0 ELSE round((pairs.baseline_correct_winners::numeric / pairs.baseline_scored_games) * 100, 2) END,
                    "BaselineBrierScore" = CASE WHEN pairs.baseline_scored_games = 0 THEN 0 ELSE round(pairs.baseline_brier_score_sum / pairs.baseline_scored_games, 4) END,
                    "BaselineLogLoss" = CASE WHEN pairs.baseline_scored_games = 0 THEN 0 ELSE round(pairs.baseline_log_loss_sum / pairs.baseline_scored_games, 4) END,
                    "BaselineAverageMarginError" = CASE WHEN pairs.baseline_scored_games = 0 THEN 0 ELSE round(pairs.baseline_margin_error_sum / pairs.baseline_scored_games, 2) END,
                    "BaselineAveragePredictedHomeWinProbability" = CASE WHEN pairs.baseline_scored_games = 0 THEN 0 ELSE round(pairs.baseline_probability_sum / pairs.baseline_scored_games, 2) END
                FROM pairs
                WHERE canonical."Id" = pairs.canonical_id;
                """);

            migrationBuilder.Sql("""
                DELETE FROM model_lab_run_metric_breakdowns legacy
                USING model_lab_run_metric_breakdowns canonical
                WHERE legacy."SegmentType" = 'season'
                  AND legacy."SegmentKey" ~ '^[0-9]{4}$'
                  AND canonical."RunId" = legacy."RunId"
                  AND canonical."SegmentType" = legacy."SegmentType"
                  AND canonical."SegmentKey" = basketelo_full_season_label(legacy."SegmentKey");
                """);

            migrationBuilder.Sql("""
                UPDATE model_lab_run_metric_breakdowns
                SET "Season" = CASE
                        WHEN "Season" IS NOT NULL AND "Season" ~ '^[0-9]{4}$'
                            THEN basketelo_full_season_label("Season")
                        ELSE "Season"
                    END,
                    "SegmentKey" = CASE
                        WHEN "SegmentType" = 'season' AND "SegmentKey" ~ '^[0-9]{4}$'
                            THEN basketelo_full_season_label("SegmentKey")
                        ELSE "SegmentKey"
                    END,
                    "Label" = CASE
                        WHEN "SegmentType" = 'season' AND "Label" ~ '^[0-9]{4}$'
                            THEN basketelo_full_season_label("Label")
                        ELSE "Label"
                    END
                WHERE ("Season" IS NOT NULL AND "Season" ~ '^[0-9]{4}$')
                   OR ("SegmentType" = 'season' AND ("SegmentKey" ~ '^[0-9]{4}$' OR "Label" ~ '^[0-9]{4}$'));
                """);

            migrationBuilder.Sql("""
                DROP FUNCTION basketelo_full_season_label(text);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Irreversible data normalization: single-year labels were merged into full season labels.
        }
    }
}
