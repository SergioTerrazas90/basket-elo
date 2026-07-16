# NBA current-season refresh operations

## Cadence

The worker can queue the active NBA season automatically. Scheduling is disabled
by default. After the authorized archive synchronization process is in place,
enable it with `NbaRefresh__Enabled=true`.

- October through June: every 12 hours.
- July through September: every 168 hours for late corrections and schedule changes.
- Scheduler due check: every 5 minutes.
- Production refresh request budget: 8 provider requests.

Override these defaults with `NbaRefresh__InSeasonIntervalHours`,
`NbaRefresh__OffSeasonIntervalHours`, `NbaRefresh__SchedulerCheckMinutes`, and
`NbaRefresh__MaxRequests`. Pending or running jobs for the same NBA season are
never queued twice.

Basketball-Reference network access remains subject to `docs/nba-source-policy.md`.
With network access disabled, update the authorized local archive before the job
runs. Enabling the scheduler does not grant or imply source permission.

## Manual refresh

Queue the active season immediately, regardless of the last completed refresh:

```powershell
curl.exe -X POST "http://localhost:5001/api/backfill/nba/current/jobs" `
  -H "Content-Type: application/json" `
  -d '{"dryRun":false,"maxRequests":8}'
```

Use `dryRun=true` after parser, archive, or source-policy changes. A manual request
still returns the existing active job instead of creating a duplicate.

## Corrections and rebuilds

Games are keyed by provider and source game ID. Re-importing a completed game
updates its score, status, teams, date, source revision, parser version, and fetch
timestamp without creating a duplicate. Review the completed job summary and
identity-health findings before rebuilding ratings.

Queue an NBA-only rebuild after a production refresh changes inserted or updated
games:

```powershell
curl.exe -X POST "http://localhost:5001/api/elo/rebuilds" `
  -H "Content-Type: application/json" `
  -d '{"rulesetVersion":"adjusted-v1","competitionName":"NBA"}'
```

Do not rebuild after a dry run or an unchanged refresh. The NBA scope preserves
ratings and histories for unrelated competitions.
