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
