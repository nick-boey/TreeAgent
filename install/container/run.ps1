#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Runs the Homespun Docker container locally with proper configuration.

.DESCRIPTION
    This script:
    - Validates Docker is running and the homespun:local image exists
    - Reads GitHub token from .NET user secrets
    - Creates the ~/.homespun-container/data directory
    - Mounts SSH keys for git operations
    - Runs the container in interactive mode on port 8080

.EXAMPLE
    .\run-homespun-container.ps1
    Runs the Homespun container with default settings.

.NOTES
    Container name: homespun-local
    Port: 8080
    Data directory: ~/.homespun-container/data
    Environment: Development
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

function Get-GitHubTokenFromSecrets {
    <#
    .SYNOPSIS
        Reads the GitHub token from .NET user secrets.
    #>
    param(
        [Parameter(Mandatory)]
        [string]$UserSecretsId
    )
    
    $secretsPath = Join-Path $env:APPDATA "Microsoft\UserSecrets\$UserSecretsId\secrets.json"
    
    if (-not (Test-Path $secretsPath)) {
        Write-Warning "User secrets file not found at: $secretsPath"
        Write-Warning "Please configure GitHub token with: dotnet user-secrets set 'GitHub:Token' 'your_token_here' --project src/Homespun"
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

# Step 3: Read GitHub token from user secrets
Write-Host "[3/6] Reading GitHub token from user secrets..." -ForegroundColor Cyan
$userSecretsId = "2cfc6c57-72da-4b56-944b-08f2c1df76f6"
$githubToken = Get-GitHubTokenFromSecrets -UserSecretsId $userSecretsId

if ([string]::IsNullOrWhiteSpace($githubToken)) {
    Write-Warning "      GitHub token not found. Container will run without GitHub integration."
    $githubToken = ""
}
else {
    $maskedToken = $githubToken.Substring(0, [Math]::Min(10, $githubToken.Length)) + "..."
    Write-Host "      GitHub token found: $maskedToken" -ForegroundColor Green
}

# Step 4: Set up paths
Write-Host "[4/6] Setting up directories..." -ForegroundColor Cyan

# Expand ~ to full path
$homeDir = [Environment]::GetFolderPath('UserProfile')

# Construct paths in a cross-platform way
$dataDir = Join-Path $homeDir ".homespun-container"
$dataDir = Join-Path $dataDir "data"
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
Write-Host "  Name:        homespun-local" -ForegroundColor White
Write-Host "  Port:        8080" -ForegroundColor White
Write-Host "  URL:         http://localhost:8080" -ForegroundColor White
Write-Host "  Environment: Production" -ForegroundColor White
Write-Host "  Data mount:  $dataDir" -ForegroundColor White
if ($mountSsh) {
    Write-Host "  SSH mount:   $sshDir (read-only)" -ForegroundColor White
}
if (-not [string]::IsNullOrWhiteSpace($TailscaleAuthKey)) {
    Write-Host "  Tailscale:   Enabled ($TailscaleHostname)" -ForegroundColor White
}
Write-Host "======================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Starting container in interactive mode..." -ForegroundColor Yellow
Write-Host "Press Ctrl+C to stop the container." -ForegroundColor Yellow
Write-Host ""

# Convert Windows paths to Unix-style for Docker (use forward slashes)
# On Linux, path separators are already forward slashes, but this replace is safe
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
)

# Add SSH mount if directory exists
if ($mountSsh) {
    $dockerArgs += "-v"
    $dockerArgs += "${sshDirUnix}:/home/homespun/.ssh:ro"
}

# Add GitHub token if available
if (-not [string]::IsNullOrWhiteSpace($githubToken)) {
    $dockerArgs += "-e"
    $dockerArgs += "GITHUB_TOKEN=$githubToken"
}

# Add Tailscale config if available
if (-not [string]::IsNullOrWhiteSpace($TailscaleAuthKey)) {
    $dockerArgs += "-e"
    $dockerArgs += "TAILSCALE_AUTH_KEY=$TailscaleAuthKey"
    $dockerArgs += "-e"
    $dockerArgs += "TAILSCALE_HOSTNAME=$TailscaleHostname"
}

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
