# Basket ELO production deploy

This deploy shape mirrors a simple VPS setup:

- publish three self-contained Linux services plus the one-shot tools build from Windows
- copy them to the VPS over SSH
- run each app with systemd
- expose only the web app through Caddy on the VPS IP, using a dedicated port so existing Caddy sites are not overwritten

## Services

| Service | Project | Local VPS URL | Public |
| --- | --- | --- | --- |
| Web | `src/BasketElo.Web` | `http://127.0.0.1:5100` | yes, through Caddy |
| API | `src/BasketElo.Api` | `http://127.0.0.1:5101` | no |
| Worker | `src/BasketElo.Worker` | `http://127.0.0.1:5102` | no |

The frontend and backend are intentionally kept as separate services. The web app is Blazor Server and calls the API from the server side through `ApiBaseUrl`, so the API does not need to be public.

## First-time VPS setup

Copy the deploy templates to the VPS:

```powershell
scp -i "$HOME\.ssh\ovh_vps-22091453" -r .\deploy ubuntu@152.228.139.241:/tmp/basket-elo-deploy
```

Create a deploy directory and environment file:

```bash
sudo mkdir -p /opt/basket-elo /etc/basket-elo
sudo chown -R ubuntu:ubuntu /opt/basket-elo
sudo cp /tmp/basket-elo-deploy/env/basket-elo.env.example /etc/basket-elo/basket-elo.env
sudo nano /etc/basket-elo/basket-elo.env
```

Install the systemd units:

```bash
sudo cp /tmp/basket-elo-deploy/systemd/*.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable basket-elo-api basket-elo-worker basket-elo-web
```

Install the Caddy site snippet. This does not overwrite `/etc/caddy/Caddyfile`.

If your main Caddyfile already imports a sites directory, copy only the snippet:

```bash
sudo mkdir -p /etc/caddy/sites
sudo cp /tmp/basket-elo-deploy/caddy/basket-elo.caddy /etc/caddy/sites/basket-elo.caddy
sudo caddy validate --config /etc/caddy/Caddyfile
sudo systemctl reload caddy
```

If your main Caddyfile does not import snippets yet, add this line once, outside any site block:

```caddyfile
import /etc/caddy/sites/*.caddy
```

Do not replace the existing Caddyfile. The Basket ELO snippet listens on `:8081`, so it can coexist with the existing public website on `:80` or `:443`.

## Deploy from Windows

```powershell
.\deploy\vps\deploy.ps1 -User ubuntu -Server 152.228.139.241 -IdentityFile "$HOME\.ssh\ovh_vps-22091453"
```

The script publishes the three apps and tools as `linux-x64` self-contained builds, uploads them, installs them under `/opt/basket-elo`, validates and reloads the BasketElo Caddy site, and restarts the three systemd services. It finishes by checking all service health endpoints, `robots.txt`, `sitemap.xml`, and the server-rendered Results and Fixtures HTML; a failed check fails the deployment.

It also publishes the one-shot historical-ingestion tools under
`/opt/basket-elo/releases/tools`; the tools are not started as a service.

## FIBA national-team tournament VPS workflow

Deploy the current checkout with the tools included:

```powershell
.\deploy\vps\deploy.ps1 `
  -User ubuntu `
  -Server 152.228.139.241 `
  -IdentityFile "$HOME\.ssh\ovh_vps-22091453"
```

The script publishes `web`, `api`, `worker`, and `tools`, installs each archive
under `/opt/basket-elo/releases/<service>`, then restarts and reports the API,
worker, and web services. It does not run an ingestion job.

Run a read-only FIBA check from the VPS after deployment:

```bash
cd /opt/basket-elo/releases/tools
./BasketElo.Tools fiba-dry-run --country Asia --league "FIBA Asia Cup Pre-Qualifiers" --season 2025 --max-requests 2
./BasketElo.Tools fiba-dry-run --country Africa --league "FIBA AfroBasket Pre-Qualifiers" --season 2025 --max-requests 2
./BasketElo.Tools fiba-dry-run --country Europe --league "FIBA EuroBasket Pre-Qualifiers" --season 2025 --max-requests 2
./BasketElo.Tools fiba-dry-run --country Americas --league "FIBA AmeriCup Pre-Qualifiers" --season 2025 --max-requests 2
```

Before any database-writing FIBA ingest or reconciliation, create and verify a
PostgreSQL backup:

```bash
sudo install -d -m 0750 /var/backups/basket-elo
backup=/var/backups/basket-elo/basket-elo-$(date +%Y%m%d-%H%M%S).dump
sudo -u postgres pg_dump --format=custom --file="$backup" basket_elo
sudo -u postgres pg_restore --list "$backup" > /tmp/basket-elo-backup-verify.txt
```

Only after the backup is verified should one regional catalog be ingested. The
country filter keeps the run scoped to one tournament family:

```bash
sudo bash -c 'conn=$(grep "^ConnectionStrings__Postgres=" /etc/basket-elo/basket-elo.env | cut -d= -f2-); export ConnectionStrings__Postgres="$conn"; cd /opt/basket-elo/releases/tools; exec ./BasketElo.Tools fiba-ingest --country Asia --max-jobs 0 --max-requests 0'
```

Replace `--country Asia` with `Africa`, `Europe`, or `Americas` for the other
families. The ingest keeps finals, qualifiers, and pre-qualifiers in separate
competitions and assigns cross-year qualification games to their target
tournament cycle. Reconciliation migrations are backup-backed, preserve
manual-result rows, and fail without deleting anything if a candidate match is
ambiguous. See [`docs/fiba-national-team-tournaments.md`](../docs/fiba-national-team-tournaments.md)
for the current source policy, mappings, and coverage snapshot.

## Health checks

From the VPS:

```bash
curl http://127.0.0.1:5100/health
curl http://127.0.0.1:5101/health
curl http://127.0.0.1:5102/health
```

From your browser:

```text
http://152.228.139.241:8081/
```
