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

## Run with Docker Compose

```powershell
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

## Troubleshooting (Windows + Docker)

If `postgres:16-alpine` fails with "no matching manifest for windows/amd64", Docker is in Windows container mode.
Switch Docker Desktop to Linux containers and retry:

```powershell
& "$Env:ProgramFiles\Docker\Docker\DockerCli.exe" -SwitchLinuxEngine
docker compose up -d --build
```
