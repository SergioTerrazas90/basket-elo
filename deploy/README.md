# Basket ELO production deploy

This deploy shape mirrors a simple VPS setup:

- publish three self-contained Linux builds from Windows
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

The script publishes all three apps as `linux-x64` self-contained builds, uploads them, installs them under `/opt/basket-elo`, and restarts the three systemd services.

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
