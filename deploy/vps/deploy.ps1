param(
    [Parameter(Mandatory = $true)]
    [string]$User,

    [Parameter(Mandatory = $true)]
    [string]$Server,

    [Parameter(Mandatory = $true)]
    [string]$IdentityFile,

    [int]$SshPort = 22,
    [string]$RemoteRoot = "/opt/basket-elo",
    [string]$Runtime = "linux-x64",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$artifactRoot = Join-Path $repoRoot "artifacts\publish"
$remote = "$User@$Server"
$caddySite = Join-Path $repoRoot "deploy\caddy\basket-elo.caddy"

$services = @(
    @{
        Name = "web"
        Project = "src/BasketElo.Web/BasketElo.Web.csproj"
        Executable = "BasketElo.Web"
    },
    @{
        Name = "api"
        Project = "src/BasketElo.Api/BasketElo.Api.csproj"
        Executable = "BasketElo.Api"
    },
    @{
        Name = "worker"
        Project = "src/BasketElo.Worker/BasketElo.Worker.csproj"
        Executable = "BasketElo.Worker"
    },
    @{
        Name = "tools"
        Project = "src/BasketElo.Tools/BasketElo.Tools.csproj"
        Executable = "BasketElo.Tools"
    }
)

if (Test-Path $artifactRoot) {
    Remove-Item -LiteralPath $artifactRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $artifactRoot | Out-Null

foreach ($service in $services) {
    Write-Host "Publishing $($service.Name) for $Runtime ($Configuration)..."
    $publishDir = Join-Path $artifactRoot $service.Name
    New-Item -ItemType Directory -Path $publishDir | Out-Null

    dotnet publish (Join-Path $repoRoot $service.Project) `
        --configuration $Configuration `
        --runtime $Runtime `
        --self-contained true `
        -p:UseAppHost=true `
        --output $publishDir

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $($service.Name)."
    }

    $archive = Join-Path $artifactRoot "$($service.Name).tar.gz"
    tar -czf $archive -C $publishDir .
    if ($LASTEXITCODE -ne 0) {
        throw "Packaging failed for $($service.Name)."
    }
}

foreach ($service in $services) {
    Write-Host "Installing $($service.Name) on $remote..."
    $archive = Join-Path $artifactRoot "$($service.Name).tar.gz"
    $remoteArchive = "/tmp/basket-elo-$($service.Name).tar.gz"
    $remoteRelease = "$RemoteRoot/releases/$($service.Name)"

    ssh -i $IdentityFile -p $SshPort $remote "mkdir -p '$remoteRelease'"
    if ($LASTEXITCODE -ne 0) {
        throw "Could not create remote release directory for $($service.Name)."
    }

    scp -i $IdentityFile -P $SshPort $archive "${remote}:$remoteArchive"
    if ($LASTEXITCODE -ne 0) {
        throw "Upload failed for $($service.Name)."
    }

    $installCommand = "rm -rf '$remoteRelease'/* && tar -xzf '$remoteArchive' -C '$remoteRelease' && chmod +x '$remoteRelease/$($service.Executable)' && rm '$remoteArchive'"
    ssh -i $IdentityFile -p $SshPort $remote $installCommand
    if ($LASTEXITCODE -ne 0) {
        throw "Remote install failed for $($service.Name)."
    }
}

Write-Host "Validating and reloading the BasketElo Caddy site..."
$remoteCaddySite = "/tmp/basket-elo.caddy"
scp -i $IdentityFile -P $SshPort $caddySite "${remote}:$remoteCaddySite"
if ($LASTEXITCODE -ne 0) {
    throw "Caddy site upload failed."
}

$caddyCommand = "sudo install -m 0644 '$remoteCaddySite' /etc/caddy/sites/basket-elo.caddy && rm '$remoteCaddySite' && sudo caddy validate --config /etc/caddy/Caddyfile && sudo systemctl reload caddy"
ssh -i $IdentityFile -p $SshPort $remote $caddyCommand
if ($LASTEXITCODE -ne 0) {
    throw "Caddy validation or reload failed."
}

$restartCommand = @(
    "sudo systemctl restart basket-elo-api",
    "sudo systemctl restart basket-elo-worker",
    "sudo systemctl restart basket-elo-web",
    "sudo systemctl --no-pager --full status basket-elo-api basket-elo-worker basket-elo-web"
) -join " && "

ssh -i $IdentityFile -p $SshPort $remote $restartCommand
if ($LASTEXITCODE -ne 0) {
    throw "Remote restart or status check failed."
}

Write-Host "Running VPS health, SEO, and server-rendering checks..."
$verifyCommand = @(
    "curl --fail --silent --show-error --retry 10 --retry-connrefused --retry-delay 2 http://127.0.0.1:5101/health >/dev/null",
    "curl --fail --silent --show-error --retry 10 --retry-connrefused --retry-delay 2 http://127.0.0.1:5102/health >/dev/null",
    "curl --fail --silent --show-error --retry 10 --retry-connrefused --retry-delay 2 http://127.0.0.1:5100/health >/dev/null",
    "curl --fail --silent --show-error http://127.0.0.1:5100/robots.txt >/dev/null",
    "curl --fail --silent --show-error http://127.0.0.1:5100/sitemap.xml >/dev/null",
    "curl --fail --silent --show-error 'http://127.0.0.1:5100/?view=results&pool=nba' | grep 'Recent results' >/dev/null",
    "! curl --fail --silent --show-error 'http://127.0.0.1:5100/?view=results&pool=nba' | grep 'Loading completed results' >/dev/null",
    "curl --fail --silent --show-error 'http://127.0.0.1:5100/?view=results&pool=europe-clubs' | grep 'Recent results' >/dev/null",
    "! curl --fail --silent --show-error 'http://127.0.0.1:5100/?view=results&pool=europe-clubs' | grep 'Loading completed results' >/dev/null",
    "curl --fail --silent --show-error 'http://127.0.0.1:5100/?view=fixtures&pool=nba' | grep 'Upcoming fixtures' >/dev/null",
    "! curl --fail --silent --show-error 'http://127.0.0.1:5100/?view=fixtures&pool=nba' | grep 'Loading fixtures' >/dev/null"
) -join " && "

ssh -i $IdentityFile -p $SshPort $remote $verifyCommand
if ($LASTEXITCODE -ne 0) {
    throw "Post-deployment verification failed."
}

Write-Host "Deployment completed and verified."
Write-Host "Public site: http://${Server}:8081/"
Write-Host "Sitemap: http://${Server}:8081/sitemap.xml"
