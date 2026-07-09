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
    }
)

if (Test-Path $artifactRoot) {
    Remove-Item -LiteralPath $artifactRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $artifactRoot | Out-Null

foreach ($service in $services) {
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

Write-Host "Deployment completed. Open http://${Server}:8081/"

