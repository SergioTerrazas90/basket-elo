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
- Provider support includes `api-sports` and `basketball-reference`.

Basketball-Reference imports use authorized local archives by default. Mirror the
source paths under the configured root, for example
`data/basketball-reference/leagues/BAA_1947_games.html` and
`data/basketball-reference/playoffs/BAA_1947_games.html`.

```powershell
$env:BasketballReference__ArchiveRoot="C:\authorized-data\basketball-reference"
$env:BasketballReference__NetworkAccessEnabled="false"
```

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

## Troubleshooting (Windows + Docker)

If `postgres:16-alpine` fails with "no matching manifest for windows/amd64", Docker is in Windows container mode.
Switch Docker Desktop to Linux containers and retry:

```powershell
& "$Env:ProgramFiles\Docker\Docker\DockerCli.exe" -SwitchLinuxEngine
docker compose up -d --build
```
