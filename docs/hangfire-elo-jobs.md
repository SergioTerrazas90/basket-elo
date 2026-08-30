# Hangfire ELO jobs

BasketElo uses Hangfire for durable ELO job dispatch. Hangfire stores its own
queue metadata in the `hangfire` schema of the existing PostgreSQL database;
there is no separate database or connection string.

## Processes

- `BasketElo.Api` registers Hangfire storage so request handlers can enqueue jobs.
- `BasketElo.Worker` hosts the Hangfire server and executes jobs.
- `BasketElo.Web` exposes the dashboard at `/admin/jobs` to authenticated admins.

The dashboard contains method names, job arguments, errors, and job controls. It
must not be exposed without the admin authorization filter. ELO jobs should pass
only an opaque run ID as their serialized argument.

## Concurrency and priority

`EloJobs:WorkerCount` configures the ELO worker count and is clamped to the range
1–3. The default and production ceiling are three total concurrent ELO jobs.

Hangfire.PostgreSql processes queue names alphabetically:

1. `a-system-elo` — canonical/public ELO rebuilds.
2. `z-model-lab` — user-owned sandbox backtests.

Priority is applied when a worker becomes available. It does not interrupt a job
that is already running.

## Schema initialization

Hangfire.PostgreSql prepares the `hangfire` schema at application startup when
necessary. Application EF Core migrations continue to own BasketElo domain
tables; Hangfire owns only its schema.

## Configuration

JSON:

```json
{
  "EloJobs": {
    "WorkerCount": 3
  }
}
```

Environment variable:

```text
EloJobs__WorkerCount=3
```

If the worker is stopped, enqueued jobs remain in PostgreSQL and resume when the
worker is available again.

## Health and dashboard

The worker's `/health` endpoint returns healthy only when the process can read
Hangfire storage and at least one Hangfire server heartbeat is registered. Both
the Docker Compose health check and the VPS deployment verification call this
endpoint. A failure should block deployment and prompt a check of the worker
logs and PostgreSQL connectivity.

The dashboard is available at `/admin/jobs` on the web app. It is protected by
the same authenticated-admin check as the rest of the admin surface. Never
publish the dashboard on a separate unauthenticated port.

## Model Lab lifecycle

- One queued or running Model Lab run is allowed per user.
- Automatic retry is disabled for ELO jobs. Failed Model Lab runs use the
  explicit retry action, which resets the same run and queues one fresh job.
- Cancelling changes the domain run state first and then deletes its Hangfire
  job. Atomic run claiming and terminal-state checks prevent a late or duplicate
  delivery from writing a second result.
- Stored-run quota counts completed retained results only. Queued, running,
  failed, cancelled, and temporary results do not consume that quota.
- The worker removes expired, non-retained terminal results once per hour.

## Deployment order

1. Back up PostgreSQL before applying the release migrations.
2. Deploy and start the API first. API startup applies EF Core migrations for
   the domain run and result tables.
3. Start the worker. It initializes or upgrades only the `hangfire` schema and
   begins consuming `a-system-elo`, then `z-model-lab`.
4. Start the web app and verify `/admin/jobs` as an admin.
5. Require all three `/health` probes to pass before considering the deployment
   complete.

If a release must be rolled back, stop the worker first so old code cannot claim
new jobs. Restore the prior binaries. Domain migrations in this feature are
additive, so leave them applied for an application rollback; use a verified
database backup for any schema rollback. Do not manually delete the `hangfire`
schema while jobs are pending.

## Failed jobs and restarts

Automatic Hangfire retry is disabled for ELO jobs. For a Model Lab failure,
inspect the exception in `/admin/jobs`, fix the cause, and use the run page's
explicit **Retry** action. The same domain run is reset and assigned one new
Hangfire job. A canceled, completed, or already-running run safely ignores late
or duplicate deliveries.

After an unexpected worker restart, queued jobs remain durable. A run interrupted
during shutdown is returned to `queued` with its Hangfire link cleared, allowing
the dispatcher to link it once after startup.

## PostgreSQL integration test

The queue integration test creates and drops a uniquely named temporary database.
Its PostgreSQL user must have `CREATE DATABASE` permission. Run it against a test
server only:

```powershell
$env:BASKETELO_TEST_POSTGRES='Host=127.0.0.1;Port=5432;Database=postgres;Username=basket_elo;Password=basket_elo'
dotnet test tests/BasketElo.Infrastructure.Tests/BasketElo.Infrastructure.Tests.csproj --filter FullyQualifiedName~ModelLabPostgreSqlIntegrationTests
```

Without that variable the test is reported as skipped; all non-PostgreSQL tests
still run. The test exercises the one-active-run filtered index, the four-user
three-running/one-queued scenario, atomic duplicate claiming, and canonical
rating-table isolation.
