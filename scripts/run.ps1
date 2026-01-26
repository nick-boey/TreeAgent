#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Runs the Homespun Docker container using Docker Compose.

.DESCRIPTION
    This script:
    - Validates Docker is running
    - Reads GitHub token from environment variables or .NET user secrets
    - Creates the ~/.homespun-container/data directory
    - Runs Homespun via Docker Compose with optional Tailscale

.EXAMPLE
    .\run.ps1
    Runs Homespun in production mode (GHCR image + Watchtower).

.EXAMPLE
    .\run.ps1 -Local
    Runs with locally built image (no Watchtower).

.EXAMPLE
    .\run.ps1 -Local -Debug
    Runs with locally built image in Debug configuration.

.EXAMPLE
    .\run.ps1 -TailscaleAuthKey "tskey-auth-..."
    Runs with Tailscale enabled for HTTPS access.

.EXAMPLE
    .\run.ps1 -ExternalHostname "homespun.tail1234.ts.net"
    Runs with external hostname for agent URLs.

.EXAMPLE
    .\run.ps1 -DataDir "C:\custom\data" -ContainerName "homespun-custom"
    Runs with custom data directory and container name.

.EXAMPLE
    .\run.ps1 -Stop
    Stops all containers.

.EXAMPLE
    .\run.ps1 -Logs
    Shows container logs.

.NOTES
    Container name: homespun
    Port: 8080 (or via Tailscale HTTPS)
    Data directory: ~/.homespun-container/data

    Environment Variables (checked in order, with .env file fallback):
    - HSP_GITHUB_TOKEN / GITHUB_TOKEN - GitHub personal access token
    - HSP_TAILSCALE_AUTH_KEY / TAILSCALE_AUTH_KEY - Tailscale auth key
    - HSP_EXTERNAL_HOSTNAME - External hostname for agent URLs

    Volume Mounts:
    - Claude Code config (~/.claude) is automatically mounted for OAuth authentication
#>

#Requires -Version 7.0

[CmdletBinding(DefaultParameterSetName = 'Run')]
param(
    [Parameter(ParameterSetName = 'Run')]
    [switch]$Local,

    [Parameter(ParameterSetName = 'Run')]
    [switch]$DebugBuild,

    [Parameter(ParameterSetName = 'Run')]
    [switch]$Interactive,

    [Parameter(ParameterSetName = 'Run')]
    [switch]$Detach,

    [Parameter(ParameterSetName = 'Run')]
    [switch]$Pull,

    [Parameter(ParameterSetName = 'Run')]
    [string]$TailscaleAuthKey,

    [Parameter(ParameterSetName = 'Run')]
    [string]$TailscaleHostname = "homespun",

    [Parameter(ParameterSetName = 'Run')]
    [string]$ExternalHostname,

    [Parameter(ParameterSetName = 'Run')]
    [Parameter(ParameterSetName = 'Stop')]
    [Parameter(ParameterSetName = 'Logs')]
    [string]$DataDir,

    [Parameter(ParameterSetName = 'Run')]
    [Parameter(ParameterSetName = 'Stop')]
    [Parameter(ParameterSetName = 'Logs')]
    [string]$ContainerName = "homespun",

    [Parameter(ParameterSetName = 'Stop')]
    [switch]$Stop,

    [Parameter(ParameterSetName = 'Logs')]
    [switch]$Logs
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Get script directory and repository root
$ScriptDir = $PSScriptRoot
$RepoRoot = (Resolve-Path (Join-Path $ScriptDir "..")).Path

# Constants
$UserSecretsId = "2cfc6c57-72da-4b56-944b-08f2c1df76f6"
$EnvFilePath = Join-Path $RepoRoot ".env"

# ============================================================================
# Functions
# ============================================================================

function Get-EnvFileValue {
    param(
        [string]$Key,
        [string]$EnvFilePath
    )

    if (-not (Test-Path $EnvFilePath)) {
        return $null
    }

    try {
        $content = Get-Content $EnvFilePath -Raw
        # Match KEY=value, handling optional quotes
        if ($content -match "(?m)^$Key=([`"']?)(.+?)\1\s*$") {
            return $Matches[2]
        }
        # Also try without quotes for simple values
        if ($content -match "(?m)^$Key=(.+?)\s*$") {
            $value = $Matches[1] -replace '^["'']|["'']$', ''
            return $value
        }
        return $null
    }
    catch {
        return $null
    }
}

function Test-DockerRunning {
    try {
        $null = docker version 2>$null
        return $LASTEXITCODE -eq 0
    }
    catch {
        return $false
    }
}

function Test-DockerComposeAvailable {
    try {
        $null = docker compose version 2>$null
        return $LASTEXITCODE -eq 0
    }
    catch {
        return $false
    }
}

function Test-DockerImageExists {
    param([string]$ImageName)
    $images = docker images --format "{{.Repository}}:{{.Tag}}" 2>$null
    return $images -contains $ImageName
}

function Get-GitHubToken {
    param(
        [string]$UserSecretsId,
        [string]$EnvFilePath
    )

    # Check environment variables first (HSP_GITHUB_TOKEN takes precedence)
    $token = $env:HSP_GITHUB_TOKEN
    if (-not [string]::IsNullOrWhiteSpace($token)) {
        return $token
    }

    $token = $env:GITHUB_TOKEN
    if (-not [string]::IsNullOrWhiteSpace($token)) {
        return $token
    }

    # Fall back to .NET user secrets
    $secretsPath = Join-Path $env:APPDATA "Microsoft\UserSecrets\$UserSecretsId\secrets.json"

    if (Test-Path $secretsPath) {
        try {
            $secrets = Get-Content $secretsPath -Raw | ConvertFrom-Json
            $token = $secrets.'GitHub:Token'
            if (-not [string]::IsNullOrWhiteSpace($token)) {
                return $token
            }
        }
        catch {
            # Continue to next fallback
        }
    }

    # Fall back to .env file
    $token = Get-EnvFileValue -Key "GITHUB_TOKEN" -EnvFilePath $EnvFilePath
    if (-not [string]::IsNullOrWhiteSpace($token)) {
        return $token
    }

    return $null
}

function Get-TailscaleAuthKey {
    param(
        [string]$ParamValue,
        [string]$EnvFilePath
    )

    if (-not [string]::IsNullOrWhiteSpace($ParamValue)) {
        return $ParamValue
    }

    $key = $env:HSP_TAILSCALE_AUTH_KEY
    if (-not [string]::IsNullOrWhiteSpace($key)) {
        return $key
    }

    $key = $env:TAILSCALE_AUTH_KEY
    if (-not [string]::IsNullOrWhiteSpace($key)) {
        return $key
    }

    # Fall back to .env file
    $key = Get-EnvFileValue -Key "TAILSCALE_AUTH_KEY" -EnvFilePath $EnvFilePath
    if (-not [string]::IsNullOrWhiteSpace($key)) {
        return $key
    }

    return $null
}

function Get-ExternalHostname {
    param(
        [string]$ParamValue,
        [string]$EnvFilePath
    )

    if (-not [string]::IsNullOrWhiteSpace($ParamValue)) {
        return $ParamValue
    }

    $hostname = $env:HSP_EXTERNAL_HOSTNAME
    if (-not [string]::IsNullOrWhiteSpace($hostname)) {
        return $hostname
    }

    # Fall back to .env file
    $hostname = Get-EnvFileValue -Key "HSP_EXTERNAL_HOSTNAME" -EnvFilePath $EnvFilePath
    if (-not [string]::IsNullOrWhiteSpace($hostname)) {
        return $hostname
    }

    return $null
}

# ============================================================================
# Main Script
# ============================================================================

Write-Host ""
Write-Host "=== Homespun Docker Compose Runner ===" -ForegroundColor Cyan
Write-Host ""

# Change to repository root for docker-compose
Push-Location $RepoRoot
try {
    # Handle Stop action
    if ($Stop) {
        Write-Host "Stopping container '$ContainerName'..." -ForegroundColor Cyan
        docker stop $ContainerName 2>$null
        docker rm $ContainerName 2>$null
        docker stop watchtower 2>$null
        docker rm watchtower 2>$null
        Write-Host "Containers stopped." -ForegroundColor Green
        exit 0
    }

    # Handle Logs action
    if ($Logs) {
        Write-Host "Following container logs (Ctrl+C to exit)..." -ForegroundColor Cyan
        docker logs -f $ContainerName
        exit 0
    }

    # Step 1: Validate Docker is running
    Write-Host "[1/5] Checking Docker..." -ForegroundColor Cyan
    if (-not (Test-DockerRunning)) {
        Write-Error "Docker is not running. Please start Docker and try again."
    }
    if (-not (Test-DockerComposeAvailable)) {
        Write-Error "Docker Compose is not available. Please install Docker Compose."
    }
    Write-Host "      Docker and Docker Compose are available." -ForegroundColor Green

    # Step 2: Check/build image
    Write-Host "[2/5] Checking container image..." -ForegroundColor Cyan
    if ($Local) {
        $ImageName = "homespun:local"
        $BuildConfig = if ($DebugBuild) { "Debug" } else { "Release" }
        Write-Host "      Building local image from $RepoRoot ($BuildConfig)..." -ForegroundColor Cyan
        docker build -t $ImageName --build-arg BUILD_CONFIGURATION=$BuildConfig $RepoRoot
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Failed to build Docker image."
        }
        Write-Host "      Local image built: $ImageName ($BuildConfig)" -ForegroundColor Green
    }
    else {
        $ImageName = "ghcr.io/nick-boey/homespun:latest"
        if ($Pull) {
            Write-Host "      Pulling latest image..." -ForegroundColor Cyan
            docker pull $ImageName
        }
        Write-Host "      Using GHCR image: $ImageName" -ForegroundColor Green
    }

    # Step 3: Read GitHub token
    Write-Host "[3/5] Reading GitHub token..." -ForegroundColor Cyan
    $githubToken = Get-GitHubToken -UserSecretsId $UserSecretsId -EnvFilePath $EnvFilePath

    if ([string]::IsNullOrWhiteSpace($githubToken)) {
        Write-Warning "      GitHub token not found."
        Write-Warning "      Set HSP_GITHUB_TOKEN or GITHUB_TOKEN environment variable."
    }
    else {
        $maskedToken = $githubToken.Substring(0, [Math]::Min(10, $githubToken.Length)) + "..."
        Write-Host "      GitHub token found: $maskedToken" -ForegroundColor Green
    }

    # Read Tailscale auth key
    $tailscaleKey = Get-TailscaleAuthKey -ParamValue $TailscaleAuthKey -EnvFilePath $EnvFilePath
    if (-not [string]::IsNullOrWhiteSpace($tailscaleKey)) {
        $maskedTsKey = $tailscaleKey.Substring(0, [Math]::Min(15, $tailscaleKey.Length)) + "..."
        Write-Host "      Tailscale auth key found: $maskedTsKey" -ForegroundColor Green
    }

    # Read external hostname
    $externalHostnameValue = Get-ExternalHostname -ParamValue $ExternalHostname -EnvFilePath $EnvFilePath
    if (-not [string]::IsNullOrWhiteSpace($externalHostnameValue)) {
        Write-Host "      External hostname: $externalHostnameValue" -ForegroundColor Green
    }

    # Step 4: Set up directories
    Write-Host "[4/5] Setting up directories..." -ForegroundColor Cyan
    $homeDir = [Environment]::GetFolderPath('UserProfile')
    # Use DataDir parameter if provided, otherwise default
    if ([string]::IsNullOrWhiteSpace($DataDir)) {
        $dataDir = Join-Path $homeDir ".homespun-container" "data"
    }
    else {
        $dataDir = $DataDir
    }
    $sshDir = Join-Path $homeDir ".ssh"
    $claudeCredentialsFile = Join-Path $homeDir ".claude\.credentials.json"
    $tailscaleStateDir = Join-Path $dataDir "tailscale"

    if (-not (Test-Path $dataDir)) {
        New-Item -ItemType Directory -Path $dataDir -Force | Out-Null
        Write-Host "      Created data directory: $dataDir" -ForegroundColor Green
    }
    else {
        Write-Host "      Data directory exists: $dataDir" -ForegroundColor Green
    }

    # Create Tailscale state directory
    if (-not (Test-Path $tailscaleStateDir)) {
        New-Item -ItemType Directory -Path $tailscaleStateDir -Force | Out-Null
        Write-Host "      Created Tailscale state directory: $tailscaleStateDir" -ForegroundColor Green
    }

    if (-not (Test-Path $sshDir)) {
        Write-Warning "      SSH directory not found: $sshDir"
        $sshDir = ""
    }

    # Check Claude Code credentials file (for OAuth authentication)
    if (-not (Test-Path $claudeCredentialsFile)) {
        Write-Warning "      Claude credentials not found: $claudeCredentialsFile"
        Write-Warning "      Run 'claude login' on host to authenticate Claude Code."
        $claudeCredentialsFile = ""
    }
    else {
        Write-Host "      Claude credentials found: $claudeCredentialsFile" -ForegroundColor Green
    }

    # Step 5: Start containers
    Write-Host "[5/5] Starting containers..." -ForegroundColor Cyan
    Write-Host ""

    # Convert paths for Docker
    $dataDirUnix = $dataDir -replace '\\', '/'
    $sshDirUnix = if ($sshDir) { $sshDir -replace '\\', '/' } else { "/dev/null" }
    $claudeCredentialsFileUnix = if ($claudeCredentialsFile) { $claudeCredentialsFile -replace '\\', '/' } else { "/dev/null" }
    $tailscaleStateDirUnix = $tailscaleStateDir -replace '\\', '/'

    Write-Host "======================================" -ForegroundColor Cyan
    Write-Host "  Container Configuration" -ForegroundColor Cyan
    Write-Host "======================================" -ForegroundColor Cyan
    Write-Host "  Container:   $ContainerName"
    Write-Host "  Image:       $ImageName"
    Write-Host "  Port:        8080"
    Write-Host "  URL:         http://localhost:8080"
    Write-Host "  Data mount:  $dataDir"
    if ($sshDir) {
        Write-Host "  SSH mount:   $sshDir (read-only)"
    }
    if ($claudeCredentialsFile) {
        Write-Host "  Claude auth: $claudeCredentialsFile (read-only)"
    }
    if (-not [string]::IsNullOrWhiteSpace($tailscaleKey)) {
        Write-Host "  Tailscale:   Enabled ($TailscaleHostname)"
    }
    else {
        Write-Host "  Tailscale:   Disabled (no auth key)"
    }
    if (-not [string]::IsNullOrWhiteSpace($externalHostnameValue)) {
        Write-Host "  Agent URLs:  https://$($externalHostnameValue):<port>"
    }
    if (-not $Local) {
        Write-Host "  Watchtower:  Enabled (auto-updates every 5 min)"
    }
    else {
        Write-Host "  Watchtower:  Disabled (local development mode)"
    }
    Write-Host "======================================" -ForegroundColor Cyan
    Write-Host ""

    # Stop existing containers first
    docker stop $ContainerName 2>$null
    docker rm $ContainerName 2>$null

    # Build docker run arguments
    $dockerArgs = @("run")

    # Determine run mode
    $runDetached = $Detach -or (-not $Interactive)

    if ($runDetached) {
        $dockerArgs += "-d"
    }

    $dockerArgs += "--name", $ContainerName
    $dockerArgs += "-p", "8080:8080"
    $dockerArgs += "-v", "${dataDirUnix}:/data"

    if ($sshDir) {
        $dockerArgs += "-v", "${sshDirUnix}:/home/homespun/.ssh:ro"
    }

    if ($claudeCredentialsFile) {
        $dockerArgs += "-v", "${claudeCredentialsFileUnix}:/home/homespun/.claude/.credentials.json:ro"
    }

    $dockerArgs += "-e", "HOME=/home/homespun"
    $dockerArgs += "-e", "ASPNETCORE_ENVIRONMENT=Production"
    $dockerArgs += "-e", "HSP_HOST_DATA_PATH=$dataDirUnix"

    if (-not [string]::IsNullOrWhiteSpace($githubToken)) {
        $dockerArgs += "-e", "GITHUB_TOKEN=$githubToken"
    }

    if (-not [string]::IsNullOrWhiteSpace($tailscaleKey)) {
        $dockerArgs += "-e", "TAILSCALE_AUTH_KEY=$tailscaleKey"
    }

    if (-not [string]::IsNullOrWhiteSpace($externalHostnameValue)) {
        $dockerArgs += "-e", "HSP_EXTERNAL_HOSTNAME=$externalHostnameValue"
    }

    $dockerArgs += "--restart", "unless-stopped"
    $dockerArgs += "--health-cmd", "curl -f http://localhost:8080/health || exit 1"
    $dockerArgs += "--health-interval", "30s"
    $dockerArgs += "--health-timeout", "10s"
    $dockerArgs += "--health-retries", "3"
    $dockerArgs += "--health-start-period", "10s"
    $dockerArgs += $ImageName

    if ($runDetached) {
        Write-Host "Starting container in detached mode..." -ForegroundColor Cyan
        & docker @dockerArgs

        # Start Watchtower for production mode
        if (-not $Local) {
            docker stop watchtower 2>$null
            docker rm watchtower 2>$null
            docker run -d `
                --name watchtower `
                -v /var/run/docker.sock:/var/run/docker.sock `
                -e WATCHTOWER_CLEANUP=true `
                -e WATCHTOWER_POLL_INTERVAL=300 `
                -e WATCHTOWER_INCLUDE_STOPPED=false `
                -e WATCHTOWER_ROLLING_RESTART=true `
                --restart unless-stopped `
                containrrr/watchtower $ContainerName
        }

        Write-Host ""
        Write-Host "Container started successfully!" -ForegroundColor Green
        Write-Host ""
        Write-Host "Access URLs:"
        Write-Host "  Local:       http://localhost:8080"
        if (-not [string]::IsNullOrWhiteSpace($tailscaleKey)) {
            Write-Host "  Tailnet:     https://$TailscaleHostname.<your-tailnet>.ts.net"
        }
        Write-Host ""
        Write-Host "Useful commands:"
        Write-Host "  View logs:     .\run.ps1 -Logs"
        Write-Host "  Stop:          .\run.ps1 -Stop"
        Write-Host "  Health check:  curl http://localhost:8080/health"
        Write-Host ""
    }
    else {
        Write-Warning "Starting container in interactive mode..."
        Write-Warning "Press Ctrl+C to stop."
        Write-Host ""
        & docker @dockerArgs
        Write-Host ""
        Write-Warning "Container stopped."
    }
}
finally {
    Pop-Location
}
