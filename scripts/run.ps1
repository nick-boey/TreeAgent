#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Runs the Homespun Docker container using Docker Compose.

.DESCRIPTION
    This script:
    - Validates Docker is running
    - Reads GitHub token from environment variables or .NET user secrets
    - Creates the ~/.homespun-container/data directory
    - Runs Homespun via Docker Compose with optional Tailscale sidecar

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
    .\run.ps1 -Tailscale -TailscaleAuthKey "tskey-auth-..."
    Runs with Tailscale sidecar for tailnet access.

.EXAMPLE
    .\run.ps1 -ExternalHostname "homespun.tail1234.ts.net"
    Runs with external hostname for agent URLs.

.EXAMPLE
    .\run.ps1 -Stop
    Stops all containers.

.EXAMPLE
    .\run.ps1 -Logs
    Shows container logs.

.NOTES
    Container name: homespun
    Port: 8080 (or via Tailscale)
    Data directory: ~/.homespun-container/data

    Environment Variables (checked in order, with .env file fallback):
    - HSP_GITHUB_TOKEN / GITHUB_TOKEN - GitHub personal access token
    - HSP_TAILSCALE_AUTH_KEY / TAILSCALE_AUTH_KEY - Tailscale auth key
    - HSP_EXTERNAL_HOSTNAME - External hostname for agent URLs
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
    [switch]$Tailscale,

    [Parameter(ParameterSetName = 'Run')]
    [string]$TailscaleAuthKey,

    [Parameter(ParameterSetName = 'Run')]
    [string]$TailscaleHostname = "homespun",

    [Parameter(ParameterSetName = 'Run')]
    [string]$ExternalHostname,

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
        Write-Host "Stopping containers..." -ForegroundColor Cyan
        docker compose --profile production --profile tailscale --profile standalone down 2>$null
        if ($LASTEXITCODE -ne 0) {
            docker compose down
        }
        Write-Host "Containers stopped." -ForegroundColor Green
        exit 0
    }

    # Handle Logs action
    if ($Logs) {
        Write-Host "Following container logs (Ctrl+C to exit)..." -ForegroundColor Cyan
        docker compose logs -f homespun
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
        $Tailscale = $true
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
    $dataDir = Join-Path $homeDir ".homespun-container" "data"
    $sshDir = Join-Path $homeDir ".ssh"

    if (-not (Test-Path $dataDir)) {
        New-Item -ItemType Directory -Path $dataDir -Force | Out-Null
        Write-Host "      Created data directory: $dataDir" -ForegroundColor Green
    }
    else {
        Write-Host "      Data directory exists: $dataDir" -ForegroundColor Green
    }

    if (-not (Test-Path $sshDir)) {
        Write-Warning "      SSH directory not found: $sshDir"
        $sshDir = ""
    }

    # Step 5: Start containers
    Write-Host "[5/5] Starting containers..." -ForegroundColor Cyan
    Write-Host ""

    # Convert paths for Docker
    $dataDirUnix = $dataDir -replace '\\', '/'
    $sshDirUnix = if ($sshDir) { $sshDir -replace '\\', '/' } else { "/dev/null" }

    # Set environment variables for docker-compose
    $env:HOMESPUN_IMAGE = $ImageName
    $env:DATA_DIR = $dataDirUnix
    $env:SSH_DIR = $sshDirUnix
    $env:GITHUB_TOKEN = $githubToken
    $env:TAILSCALE_AUTH_KEY = $tailscaleKey
    $env:TAILSCALE_HOSTNAME = $TailscaleHostname
    $env:HSP_EXTERNAL_HOSTNAME = $externalHostnameValue

    # Determine compose profiles
    $composeProfiles = @()
    if (-not $Local) {
        $composeProfiles += "--profile"
        $composeProfiles += "production"
    }
    if ($Tailscale) {
        $composeProfiles += "--profile"
        $composeProfiles += "tailscale"
    }
    else {
        $composeProfiles += "--profile"
        $composeProfiles += "standalone"
    }

    Write-Host "======================================" -ForegroundColor Cyan
    Write-Host "  Container Configuration" -ForegroundColor Cyan
    Write-Host "======================================" -ForegroundColor Cyan
    Write-Host "  Image:       $ImageName"
    Write-Host "  Port:        8080"
    Write-Host "  URL:         http://localhost:8080"
    Write-Host "  Data mount:  $dataDir"
    if ($sshDir) {
        Write-Host "  SSH mount:   $sshDir (read-only)"
    }
    if ($Tailscale) {
        Write-Host "  Tailscale:   Enabled via sidecar ($TailscaleHostname)"
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

    # Determine run mode
    $runDetached = $Detach -or (-not $Interactive)

    if ($runDetached) {
        Write-Host "Starting containers in detached mode..." -ForegroundColor Cyan
        $composeArgs = @("compose") + $composeProfiles + @("up", "-d")
        & docker @composeArgs

        Write-Host ""
        Write-Host "Containers started successfully!" -ForegroundColor Green
        Write-Host ""
        Write-Host "Access URLs:"
        if ($Tailscale) {
            Write-Host "  Tailnet:     https://$TailscaleHostname.<your-tailnet>.ts.net"
            Write-Host "  OpenCode:    https://$TailscaleHostname.<your-tailnet>.ts.net:4096-4105"
        }
        else {
            Write-Host "  Local:       http://localhost:8080"
        }
        Write-Host ""
        Write-Host "Useful commands:"
        Write-Host "  View logs:     .\run.ps1 -Logs"
        Write-Host "  Stop:          .\run.ps1 -Stop"
        Write-Host "  Health check:  curl http://localhost:8080/health"
        Write-Host ""
    }
    else {
        Write-Warning "Starting containers in interactive mode..."
        Write-Warning "Press Ctrl+C to stop."
        Write-Host ""
        $composeArgs = @("compose") + $composeProfiles + @("up")
        & docker @composeArgs
        Write-Host ""
        Write-Warning "Containers stopped."
    }
}
finally {
    Pop-Location
}
