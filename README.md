# Basket ELO

Initial scaffolding for a basketball ELO platform with a Blazor SSR frontend, .NET API, background worker, and PostgreSQL.

## Stack

- .NET 8 (upgrade path to .NET 10)
- ASP.NET Core Web API
- Blazor Web App (SSR-first)
- Worker service for ingestion/rebuild jobs
- PostgreSQL
- Docker Compose

## Solution structure

- `src/BasketElo.Web`: Blazor SSR frontend
- `src/BasketElo.Api`: REST API and health endpoint
- `src/BasketElo.Worker`: background processing host and health endpoint
- `src/BasketElo.Domain`: domain entities
- `src/BasketElo.Infrastructure`: EF Core/PostgreSQL wiring

## Prerequisites

- .NET SDK 8.0+
- Docker Desktop (or Docker Engine + Compose)

## Run locally with .NET

```powershell
dotnet restore
dotnet build BasketElo.sln
dotnet run --project src/BasketElo.Api
dotnet run --project src/BasketElo.Worker
dotnet run --project src/BasketElo.Web
```

Set API-Sports key for backfill testing (do not commit keys):

```powershell
$env:APISPORTS_API_KEY="<your_api_key>"
```

Health endpoints:

- API: `http://localhost:5001/health` (container) or launch profile URL in local run
- Worker: `http://localhost:5002/health` (container) or launch profile URL in local run
- Web: `http://localhost:5000/health` (container)
- DB status (API): `http://localhost:5001/api/system/db-status` (container)
- Swagger UI (API): `http://localhost:5001/swagger`

## EF migrations

Install local tools (first time):

```powershell
dotnet tool restore
```

Create a migration:

```powershell
dotnet tool run dotnet-ef migrations add <MigrationName> --project src/BasketElo.Infrastructure/BasketElo.Infrastructure.csproj --startup-project src/BasketElo.Api/BasketElo.Api.csproj --output-dir Persistence/Migrations --no-build
```

Apply migrations:

```powershell
dotnet tool run dotnet-ef database update --project src/BasketElo.Infrastructure/BasketElo.Infrastructure.csproj --startup-project src/BasketElo.Api/BasketElo.Api.csproj --no-build
```

## ELO rulesets

Basket ELO stores ratings by ruleset version from day one:

- `basic-elo-v1`: plain win/loss ELO.
- `point-margin-elo-v1`: legacy ruleset, adjusted by point margin.
- `adjusted-v1`: default public ruleset, adjusted by point margin with the issue #8 constants.

After an NBA import, queue a competition-scoped rebuild so ratings and history for
other competitions remain untouched:

```powershell
curl.exe -X POST "http://localhost:5001/api/elo/rebuilds" `
  -H "Content-Type: application/json" `
  -d '{"rulesetVersion":"adjusted-v1","competitionName":"NBA"}'
```

The run output includes `competitionName`, `gamesProcessed`, and `teamsRated`.
Omit `competitionName` to retain the existing global rebuild behavior. NBA playoff
and regular-season games currently use the same competition weight from the
selected ruleset.

See `docs/elo-rulesets.md` for the naming, constants, and point-margin conversion rationale.

## Run with Docker Compose

```powershell
$env:APISPORTS_API_KEY="<your_api_key>"
docker compose up -d --build
```

Services:

- Web: `http://localhost:5000`
- API: `http://localhost:5001/health`
- Worker: `http://localhost:5002/health`
- Postgres: `localhost:5432`

Stop:

```powershell
docker compose down
```

Stop and delete volumes:

```powershell
docker compose down -v
```

## Default local database connection

`Host=localhost;Port=5432;Database=basket_elo;Username=basket_elo;Password=basket_elo`

In containers, services use:

`Host=postgres;Port=5432;Database=basket_elo;Username=basket_elo;Password=basket_elo`

## Backfill job trigger

Create a backfill job (API -> Worker):

```powershell
curl.exe -X POST "http://localhost:5001/api/backfill/jobs" `
  -H "Content-Type: application/json" `
  -d "{\"provider\":\"api-sports\",\"country\":\"Spain\",\"leagueName\":\"ACB\",\"season\":\"2024-2025\",\"dryRun\":true,\"maxRequests\":2}"
```

Notes:
- `dryRun=true` fetches provider data and stores summary without writing competition/team/game rows.
- `maxRequests` hard-limits provider calls for budget control.
- Provider support includes `api-sports`, `basketball-reference`, and
  `fivethirtyeight`; the latter reads a checksum-pinned, locally retained CC BY
  4.0 NBA archive.

Queue an inclusive configured season range:

```powershell
curl.exe -X POST "http://localhost:5001/api/backfill/leagues/range/jobs" `
  -H "Content-Type: application/json" `
  -d '{"provider":"basketball-reference","country":"United States","leagueName":"NBA","startSeason":"1946-1947","endSeason":"1959-1960","onlyMissing":true,"replaceExisting":false,"newestFirst":true,"dryRun":true,"maxRequests":8}'
```

Pending/running seasons are deduplicated. Transient HTTP 408, 429, 5xx, transport,
and timeout failures retry with exponential backoff. Every attempt observes the
provider rate limiter and consumes the job request budget. Configure behavior with:

```powershell
$env:BasketballReference__MinRequestIntervalSeconds="10"
$env:BasketballReference__MaxTransientRetries="3"
$env:BasketballReference__RetryBaseDelayMilliseconds="500"
$env:ApiSports__MinSecondsBetweenRequests="7"
$env:ApiSports__MaxTransientRetries="3"
$env:ApiSports__RetryBaseDelayMilliseconds="500"
```

Permanent failures affect only their season. The failed job's `SummaryJson`
contains provider, league, season, exception type, status code, attempts, request
usage, failure time, and the single-job retry endpoint.

Basketball-Reference imports use authorized local archives by default. Mirror the
source paths under the configured root, for example
`data/basketball-reference/leagues/BAA_1947_games.html` and
`data/basketball-reference/playoffs/BAA_1947_games.html`.

```powershell
$env:BasketballReference__ArchiveRoot="C:\authorized-data\basketball-reference"
$env:BasketballReference__NetworkAccessEnabled="false"
```

FiveThirtyEight historical NBA imports use the pinned CC BY 4.0 archive and do
not make runtime network requests. Download the recorded revision, verify its
SHA-256, and configure the absolute archive path before queueing `1946-1947`
through `2007-2008` newest-first:

```powershell
$env:FiveThirtyEight__ArchivePath="C:\authorized-data\fivethirtyeight\nbaallelo.csv"
$env:FiveThirtyEight__SourceRevision="4c1ff5e3aef1816ae04af63218015066e186c147"
$env:FiveThirtyEight__ExpectedSha256="d46ed3540ee8d9eca31b3e94cc8c777e0be5156173d814ebf65b8195e8d616bc"
```

The provider imports only the primary NBA row for each source game. It excludes
paired copies and all ABA records, and the checksum gate blocks a changed file.

Network fetching stays disabled unless the operator both enables it and records
the permission basis in `BasketballReference__PermissionReference`. See
`docs/nba-source-policy.md` before enabling NBA ingestion.

Run a read-only historical audit before queueing database writes:

```powershell
dotnet run --project src/BasketElo.Tools -- nba-audit `
  --start 1946-1947 --end 1959-1960 `
  --output artifacts/nba-audit-1946-1960.json `
  --resume
```

The report includes per-season game, missing-score, duplicate-ID, warning,
request, failure, and elapsed-time totals. Audit execution does not register a
database context and records `DatabaseWrites: 0` in JSON output.

Current-season NBA refresh scheduling, manual queueing, correction handling, and
the post-refresh Elo workflow are documented in `docs/nba-refresh-operations.md`.

## Troubleshooting (Windows + Docker)

If `postgres:16-alpine` fails with "no matching manifest for windows/amd64", Docker is in Windows container mode.
Switch Docker Desktop to Linux containers and retry:

```powershell
& "$Env:ProgramFiles\Docker\Docker\DockerCli.exe" -SwitchLinuxEngine
docker compose up -d --build
```
