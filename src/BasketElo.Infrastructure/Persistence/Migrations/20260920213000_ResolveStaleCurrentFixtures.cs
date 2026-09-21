using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260920213000_ResolveStaleCurrentFixtures")]
public partial class ResolveStaleCurrentFixtures : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            DECLARE
                stale_id uuid;
            BEGIN
                FOR stale_id IN
                    SELECT unnest(ARRAY[
                        'd8875acb-bcb3-4e86-949b-564049df9107'::uuid,
                        '4fdf5cf0-a64c-4190-8a36-3e4447eacb9d'::uuid,
                        'e0542b55-a040-4425-9e92-ba3aebd58c78'::uuid,
                        '3e73351f-0792-4300-bd6c-d8e4d188d67d'::uuid,
                        'b19fa4e8-1876-4e78-8388-bc6efb82acc5'::uuid,
                        '2f88b295-3f64-445c-82cb-3bb9b42e21f3'::uuid,
                        'baeab881-f232-4310-8cb4-10f41da2497e'::uuid,
                        '99082ccd-75f6-43f6-895e-ba36f625ef38'::uuid,
                        '79b765a0-cbcc-4638-881e-3071ca947c3a'::uuid,
                        '3312af38-04be-470c-a2f2-705403aaa2a4'::uuid,
                        'a1852928-e40e-407e-b17b-0d139678c0ca'::uuid,
                        'a7e100c5-9748-41c7-b0ed-1e3c430174e8'::uuid,
                        'd945e6df-1430-4cbb-bb47-5ff0ddabd79d'::uuid,
                        'cdd0c3a0-ad13-4bdc-8a7d-7a6a3b9e8f92'::uuid
                    ])
                LOOP
                    IF EXISTS (
                        SELECT 1
                        FROM games
                        WHERE "Id" = stale_id
                          AND (lower("Status") NOT IN ('scheduled', 'not started') OR
                               "HomeScore" IS NOT NULL OR
                               "AwayScore" IS NOT NULL)
                    ) THEN
                        RAISE EXCEPTION 'Stale fixture % no longer has the expected unplayed shape.', stale_id;
                    END IF;
                END LOOP;

                IF EXISTS (
                    SELECT 1
                    FROM model_lab_run_predictions
                    WHERE "GameId" IN (
                        'd8875acb-bcb3-4e86-949b-564049df9107',
                        '4fdf5cf0-a64c-4190-8a36-3e4447eacb9d',
                        '2f88b295-3f64-445c-82cb-3bb9b42e21f3',
                        'baeab881-f232-4310-8cb4-10f41da2497e',
                        '79b765a0-cbcc-4638-881e-3071ca947c3a',
                        'a1852928-e40e-407e-b17b-0d139678c0ca',
                        'a7e100c5-9748-41c7-b0ed-1e3c430174e8',
                        'd945e6df-1430-4cbb-bb47-5ff0ddabd79d',
                        'cdd0c3a0-ad13-4bdc-8a7d-7a6a3b9e8f92'
                    )
                    UNION ALL
                    SELECT 1
                    FROM model_lab_run_evolution_points
                    WHERE "GameId" IN (
                        'd8875acb-bcb3-4e86-949b-564049df9107',
                        '4fdf5cf0-a64c-4190-8a36-3e4447eacb9d',
                        '2f88b295-3f64-445c-82cb-3bb9b42e21f3',
                        'baeab881-f232-4310-8cb4-10f41da2497e',
                        '79b765a0-cbcc-4638-881e-3071ca947c3a',
                        'a1852928-e40e-407e-b17b-0d139678c0ca',
                        'a7e100c5-9748-41c7-b0ed-1e3c430174e8',
                        'd945e6df-1430-4cbb-bb47-5ff0ddabd79d',
                        'cdd0c3a0-ad13-4bdc-8a7d-7a6a3b9e8f92'
                    )
                ) THEN
                    RAISE EXCEPTION 'A saved Model Lab run references a stale duplicate fixture.';
                END IF;

                WITH fixes("Id", "GameDateTimeUtc", "HomeScore", "AwayScore") AS (
                    VALUES
                        ('e0542b55-a040-4425-9e92-ba3aebd58c78'::uuid, '2026-08-27T15:30:00Z'::timestamptz, 77::smallint, 83::smallint),
                        ('3e73351f-0792-4300-bd6c-d8e4d188d67d'::uuid, '2026-08-28T18:00:00Z'::timestamptz, 94::smallint, 66::smallint),
                        ('b19fa4e8-1876-4e78-8388-bc6efb82acc5'::uuid, '2026-08-29T12:30:00Z'::timestamptz, 72::smallint, 66::smallint),
                        ('99082ccd-75f6-43f6-895e-ba36f625ef38'::uuid, '2026-08-30T15:30:00Z'::timestamptz, 91::smallint, 70::smallint),
                        ('3312af38-04be-470c-a2f2-705403aaa2a4'::uuid, '2026-08-31T19:30:00Z'::timestamptz, 73::smallint, 110::smallint)
                )
                UPDATE games game
                SET "GameDateTimeUtc" = fixes."GameDateTimeUtc",
                    "HomeScore" = fixes."HomeScore",
                    "AwayScore" = fixes."AwayScore",
                    "Status" = 'finished',
                    "EloEligible" = TRUE,
                    "EloExclusionReason" = NULL,
                    "UpdatedAtUtc" = NOW()
                FROM fixes
                WHERE game."Id" = fixes."Id";

                UPDATE current_result_reviews
                SET "Status" = 'resolved',
                    "Reason" = '',
                    "ResolvedAtUtc" = COALESCE("ResolvedAtUtc", NOW()),
                    "UpdatedAtUtc" = NOW()
                WHERE "AssignedGameId" IN (
                    'e0542b55-a040-4425-9e92-ba3aebd58c78',
                    '3e73351f-0792-4300-bd6c-d8e4d188d67d',
                    'b19fa4e8-1876-4e78-8388-bc6efb82acc5',
                    '99082ccd-75f6-43f6-895e-ba36f625ef38',
                    '3312af38-04be-470c-a2f2-705403aaa2a4'
                );

                DELETE FROM games
                WHERE "Id" IN (
                    'd8875acb-bcb3-4e86-949b-564049df9107',
                    '4fdf5cf0-a64c-4190-8a36-3e4447eacb9d',
                    '2f88b295-3f64-445c-82cb-3bb9b42e21f3',
                    'baeab881-f232-4310-8cb4-10f41da2497e',
                    '79b765a0-cbcc-4638-881e-3071ca947c3a',
                    'a1852928-e40e-407e-b17b-0d139678c0ca',
                    'a7e100c5-9748-41c7-b0ed-1e3c430174e8',
                    'd945e6df-1430-4cbb-bb47-5ff0ddabd79d',
                    'cdd0c3a0-ad13-4bdc-8a7d-7a6a3b9e8f92'
                );
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Corrected official results and removed duplicate placeholders are not
        // reconstructed automatically. Restore the pre-migration backup if needed.
    }
}
