using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260908220000_ReconcileDuplicateCurrentNationalTeamResults")]
public partial class ReconcileDuplicateCurrentNationalTeamResults : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TEMP TABLE current_results_duplicate_map ON COMMIT DROP AS
            SELECT livescore."Id" AS duplicate_id,
                   fiba."Id" AS canonical_id,
                   livescore."SourceGameId" AS livescore_source_game_id
            FROM games livescore
            JOIN games fiba
              ON fiba."CompetitionId" = livescore."CompetitionId"
             AND fiba."GameDateTimeUtc" = livescore."GameDateTimeUtc"
             AND fiba."HomeTeamId" = livescore."HomeTeamId"
             AND fiba."AwayTeamId" = livescore."AwayTeamId"
             AND fiba."HomeScore" = livescore."HomeScore"
             AND fiba."AwayScore" = livescore."AwayScore"
            WHERE livescore."Source" = 'livescore'
              AND fiba."Source" = 'fiba'
              AND (livescore."SourceGameId", fiba."SourceGameId") IN (
                ('1825053', '127291'),
                ('1825055', '127294'),
                ('1825125', '127301'),
                ('1825128', '127300')
              );

            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM games livescore
                    WHERE livescore."Source" = 'livescore'
                      AND livescore."SourceGameId" IN ('1825053', '1825055', '1825125', '1825128')
                      AND NOT EXISTS (
                          SELECT 1
                          FROM current_results_duplicate_map mapped
                          WHERE mapped.duplicate_id = livescore."Id"
                      )
                ) THEN
                    RAISE EXCEPTION 'A known LiveScore duplicate no longer matches its canonical FIBA game';
                END IF;
            END $$;

            UPDATE current_result_reviews review
            SET "AssignedGameId" = mapped.canonical_id,
                "ResolutionAction" = 'data_reconcile',
                "ResolutionNote" = 'Reconciled with the canonical FIBA game after completed-fixture duplicate cleanup.',
                "UpdatedAtUtc" = now()
            FROM current_results_duplicate_map mapped
            WHERE review."Source" = 'livescore'
              AND review."SourceGameId" = mapped.livescore_source_game_id;

            CREATE TEMP TABLE affected_model_lab_runs ON COMMIT DROP AS
            SELECT DISTINCT prediction."RunId" AS run_id
            FROM model_lab_run_predictions prediction
            JOIN current_results_duplicate_map mapped ON mapped.duplicate_id = prediction."GameId"
            UNION
            SELECT DISTINCT point."RunId"
            FROM model_lab_run_evolution_points point
            JOIN current_results_duplicate_map mapped ON mapped.duplicate_id = point."GameId";

            DELETE FROM model_lab_run_scopes scope
            USING affected_model_lab_runs affected
            WHERE scope."RunId" = affected.run_id;

            DELETE FROM model_lab_run_predictions prediction
            USING affected_model_lab_runs affected
            WHERE prediction."RunId" = affected.run_id;

            DELETE FROM model_lab_run_ratings rating
            USING affected_model_lab_runs affected
            WHERE rating."RunId" = affected.run_id;

            DELETE FROM model_lab_run_evolution_points point
            USING affected_model_lab_runs affected
            WHERE point."RunId" = affected.run_id;

            DELETE FROM model_lab_run_period_metrics metric
            USING affected_model_lab_runs affected
            WHERE metric."RunId" = affected.run_id;

            DELETE FROM model_lab_run_metric_breakdowns breakdown
            USING affected_model_lab_runs affected
            WHERE breakdown."RunId" = affected.run_id;

            UPDATE model_lab_runs run
            SET "Status" = 'failed',
                "HangfireJobId" = NULL,
                "ProgressPercent" = 0,
                "ProgressStage" = 'Retry required',
                "CompletedAtUtc" = now(),
                "ErrorMessage" = 'A duplicate source game used by this run was reconciled. Retry the run against the corrected data.'
            FROM affected_model_lab_runs affected
            WHERE run."Id" = affected.run_id;

            DELETE FROM games game
            USING current_results_duplicate_map mapped
            WHERE game."Id" = mapped.duplicate_id;

            INSERT INTO elo_rebuild_runs
                ("Id", "QueuedAtUtc", "RulesetVersion", "EloPoolKey", "CompetitionName",
                 "Status", "GamesProcessed", "TeamsRated", "Notes", "CreatedAtUtc")
            SELECT gen_random_uuid(), now(), ruleset.version, 'national-teams', '',
                   'pending', 0, 0,
                   'Queued after reconciling duplicate current national-team results.', now()
            FROM (VALUES ('adjusted-v1'), ('basic-elo-v1')) AS ruleset(version)
            WHERE EXISTS (SELECT 1 FROM current_results_duplicate_map)
              AND NOT EXISTS (
                  SELECT 1
                  FROM elo_rebuild_runs active
                  WHERE active."EloPoolKey" = 'national-teams'
                    AND active."RulesetVersion" = ruleset.version
                    AND active."Status" IN ('pending', 'running')
              );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) { }
}
