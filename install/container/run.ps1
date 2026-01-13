#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Runs the Homespun Docker container locally with proper configuration.

.DESCRIPTION
    This script:
    - Validates Docker is running and the homespun:local image exists
    - Reads GitHub token from environment variables or .NET user secrets
    - Creates the ~/.homespun-container/data directory
    - Mounts SSH keys for git operations
    - Runs the container in interactive mode on port 8080

.EXAMPLE
    .\run.ps1
    Runs the Homespun container with default settings.

.EXAMPLE
    .\run.ps1 -TailscaleAuthKey "tskey-auth-..."
    Runs with Tailscale enabled.

.NOTES
    Container name: homespun-local
    Port: 8080
    Data directory: ~/.homespun-container/data
    Environment: Production

    Environment Variables (checked in order):
    - HSP_GITHUB_TOKEN / GITHUB_TOKEN - GitHub personal access token
    - HSP_TAILSCALE_AUTH / TAILSCALE_AUTH_KEY - Tailscale auth key
#>

#Requires -Version 7.0

[CmdletBinding()]
param(
    [string]$TailscaleAuthKey,
    [string]$TailscaleHostname = "homespun-container"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ============================================================================
# Functions
# ============================================================================

function Test-DockerRunning {
    <#
    .SYNOPSIS
        Checks if Docker is running.
    #>
    try {
        $null = docker version 2>$null
        return $true
    }
    catch {
        return $false
    }
}

function Test-DockerImageExists {
    <#
    .SYNOPSIS
        Checks if the specified Docker image exists.
    #>
    param(
        [Parameter(Mandatory)]
        [string]$ImageName
    )

    $images = docker images --format "{{.Repository}}:{{.Tag}}" 2>$null
    return $images -contains $ImageName
}

function Get-GitHubToken {
    <#
    .SYNOPSIS
        Gets the GitHub token from environment variables or .NET user secrets.
    #>
    param(
        [Parameter(Mandatory)]
        [string]$UserSecretsId
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

    if (-not (Test-Path $secretsPath)) {
        Write-Warning "GitHub token not found in environment or user secrets."
        Write-Warning "Set HSP_GITHUB_TOKEN or GITHUB_TOKEN environment variable,"
        Write-Warning "or configure with: dotnet user-secrets set 'GitHub:Token' 'your_token_here' --project src/Homespun"
        return $null
    }

    try {
        $secrets = Get-Content $secretsPath -Raw | ConvertFrom-Json
        $token = $secrets.'GitHub:Token'

        if ([string]::IsNullOrWhiteSpace($token)) {
            Write-Warning "GitHub:Token not found in user secrets"
            return $null
        }

        return $token
    }
    catch {
        Write-Warning "Failed to read user secrets: $_"
        return $null
    }
}

function Get-TailscaleAuthKey {
    <#
    .SYNOPSIS
        Gets the Tailscale auth key from parameter or environment variables.
    #>
    param(
        [string]$ParamValue
    )

    # Parameter takes precedence
    if (-not [string]::IsNullOrWhiteSpace($ParamValue)) {
        return $ParamValue
    }

    # Check environment variables (HSP_TAILSCALE_AUTH takes precedence)
    $key = $env:HSP_TAILSCALE_AUTH
    if (-not [string]::IsNullOrWhiteSpace($key)) {
        return $key
    }

    $key = $env:TAILSCALE_AUTH_KEY
    if (-not [string]::IsNullOrWhiteSpace($key)) {
        return $key
    }

    return $null
}

function Stop-ExistingContainer {
    <#
    .SYNOPSIS
        Stops and removes an existing container if it exists.
    #>
    param(
        [Parameter(Mandatory)]
        [string]$ContainerName
    )

    $existing = docker ps -a --filter "name=$ContainerName" --format "{{.Names}}" 2>$null

    if ($existing -eq $ContainerName) {
        Write-Host "Stopping existing container '$ContainerName'..." -ForegroundColor Yellow
        docker stop $ContainerName 2>&1 | Out-Null
        docker rm $ContainerName 2>&1 | Out-Null
        Write-Host "Existing container removed." -ForegroundColor Green
    }
}

# ============================================================================
# Main Script
# ============================================================================

Write-Host "`n=== Homespun Docker Container Runner ===" -ForegroundColor Cyan
Write-Host ""

# Step 1: Validate Docker is running
Write-Host "[1/6] Checking Docker..." -ForegroundColor Cyan
if (-not (Test-DockerRunning)) {
    Write-Error "Docker is not running. Please start Docker Desktop and try again."
    exit 1
}
Write-Host "      Docker is running." -ForegroundColor Green

# Step 2: Check if image exists
Write-Host "[2/6] Checking for homespun:local image..." -ForegroundColor Cyan
if (-not (Test-DockerImageExists -ImageName "homespun:local")) {
    Write-Error @"
Docker image 'homespun:local' not found.

Please build the image first:
    docker build -t homespun:local .

Then run this script again.
"@
    exit 1
}
Write-Host "      Image found." -ForegroundColor Green

# Step 3: Read GitHub token
Write-Host "[3/6] Reading GitHub token..." -ForegroundColor Cyan
$userSecretsId = "2cfc6c57-72da-4b56-944b-08f2c1df76f6"
$githubToken = Get-GitHubToken -UserSecretsId $userSecretsId

if ([string]::IsNullOrWhiteSpace($githubToken)) {
    Write-Warning "      GitHub token not found. Container will run without GitHub integration."
    $githubToken = ""
}
else {
    $maskedToken = $githubToken.Substring(0, [Math]::Min(10, $githubToken.Length)) + "..."
    Write-Host "      GitHub token found: $maskedToken" -ForegroundColor Green
}

# Read Tailscale auth key
$tailscaleKey = Get-TailscaleAuthKey -ParamValue $TailscaleAuthKey
if (-not [string]::IsNullOrWhiteSpace($tailscaleKey)) {
    $maskedTsKey = $tailscaleKey.Substring(0, [Math]::Min(15, $tailscaleKey.Length)) + "..."
    Write-Host "      Tailscale auth key found: $maskedTsKey" -ForegroundColor Green
}

# Step 4: Set up paths
Write-Host "[4/6] Setting up directories..." -ForegroundColor Cyan

# Expand ~ to full path
$homeDir = [Environment]::GetFolderPath('UserProfile')

# Construct paths
$dataDir = Join-Path $homeDir ".homespun-container" "data"
$sshDir = Join-Path $homeDir ".ssh"

# Create data directory if it doesn't exist
if (-not (Test-Path $dataDir)) {
    New-Item -ItemType Directory -Path $dataDir -Force | Out-Null
    Write-Host "      Created data directory: $dataDir" -ForegroundColor Green
}
else {
    Write-Host "      Data directory exists: $dataDir" -ForegroundColor Green
}

# Check SSH directory
if (-not (Test-Path $sshDir)) {
    Write-Warning "      SSH directory not found: $sshDir"
    Write-Warning "      Git operations requiring SSH may not work."
    $mountSsh = $false
}
else {
    Write-Host "      SSH directory found: $sshDir" -ForegroundColor Green
    $mountSsh = $true
}

# Step 5: Stop existing container
Write-Host "[5/6] Checking for existing container..." -ForegroundColor Cyan
Stop-ExistingContainer -ContainerName "homespun-local"

# Step 6: Run container
Write-Host "[6/6] Starting container..." -ForegroundColor Cyan
Write-Host ""
Write-Host "======================================" -ForegroundColor Cyan
Write-Host "  Container Configuration" -ForegroundColor Cyan
Write-Host "======================================" -ForegroundColor Cyan
Write-Host "  Name:        homespun-local"
Write-Host "  Port:        8080"
Write-Host "  URL:         http://localhost:8080"
Write-Host "  Environment: Production"
Write-Host "  Data mount:  $dataDir"
if ($mountSsh) {
    Write-Host "  SSH mount:   $sshDir (read-only)"
}
if (-not [string]::IsNullOrWhiteSpace($tailscaleKey)) {
    Write-Host "  Tailscale:   Enabled ($TailscaleHostname)"
}
Write-Host "======================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Starting container in interactive mode..." -ForegroundColor Yellow
Write-Host "Press Ctrl+C to stop the container." -ForegroundColor Yellow
Write-Host ""

# Convert Windows paths to Unix-style for Docker (use forward slashes)
$dataDirUnix = $dataDir -replace '\\', '/'
$sshDirUnix = $sshDir -replace '\\', '/'

# Build docker run command
$dockerArgs = @(
    "run"
    "--rm"
    "-it"
    "--name", "homespun-local"
    "-p", "8080:8080"
    "-v", "${dataDirUnix}:/data"
    "-v", "${dataDirUnix}:/home/containeruser"
    "-e", "HOME=/home/containeruser"
    "-e", "ASPNETCORE_ENVIRONMENT=Production"
)

# Add SSH mount if directory exists
if ($mountSsh) {
    $dockerArgs += "-v"
    $dockerArgs += "${sshDirUnix}:/home/containeruser/.ssh:ro"
}

# Add GitHub token if available
if (-not [string]::IsNullOrWhiteSpace($githubToken)) {
    $dockerArgs += "-e"
    $dockerArgs += "GITHUB_TOKEN=$githubToken"
}

# Add Tailscale config if available
if (-not [string]::IsNullOrWhiteSpace($tailscaleKey)) {
    $dockerArgs += "-e"
    $dockerArgs += "TAILSCALE_AUTH_KEY=$tailscaleKey"
    $dockerArgs += "-e"
    $dockerArgs += "TAILSCALE_HOSTNAME=$TailscaleHostname"
}

# Add HSP_HOST_DATA_PATH for beads daemon path translation
# (May not be needed on Windows if beads daemon isn't running on host)
$dockerArgs += "-e"
$dockerArgs += "HSP_HOST_DATA_PATH=$dataDirUnix"

# Add image name
$dockerArgs += "homespun:local"

# Run the container
try {
    & docker $dockerArgs
}
catch {
    Write-Error "Failed to start container: $_"
    exit 1
}

Write-Host ""
Write-Host "Container stopped." -ForegroundColor Yellow
