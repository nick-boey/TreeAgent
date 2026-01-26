#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Runs the Homespun Docker container in development mode with isolated storage.

.DESCRIPTION
    This script wraps run.ps1 with dev-specific defaults:
    - Uses locally built image (--local)
    - Isolated data directory (~/.homespun-dev-container)
    - Separate container name (homespun-dev)
    - Interactive mode by default

.EXAMPLE
    .\dev.ps1
    Starts the dev container in interactive mode.

.EXAMPLE
    .\dev.ps1 -Detach
    Starts the dev container in detached mode.

.EXAMPLE
    .\dev.ps1 -Stop
    Stops the dev container and deletes the data directory.

.EXAMPLE
    .\dev.ps1 -Logs
    Shows dev container logs.

.EXAMPLE
    .\dev.ps1 -DebugBuild
    Builds and runs in Debug configuration.

.NOTES
    Data directory: ~/.homespun-dev-container
    Container name: homespun-dev
#>

#Requires -Version 7.0

[CmdletBinding(DefaultParameterSetName = 'Run')]
param(
    [Parameter(ParameterSetName = 'Run')]
    [switch]$DebugBuild,

    [Parameter(ParameterSetName = 'Run')]
    [switch]$Detach,

    [Parameter(ParameterSetName = 'Run')]
    [switch]$Pull,

    [Parameter(ParameterSetName = 'Stop')]
    [switch]$Stop,

    [Parameter(ParameterSetName = 'Logs')]
    [switch]$Logs
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Get script directory
$ScriptDir = $PSScriptRoot

# Dev-specific settings
$homeDir = [Environment]::GetFolderPath('UserProfile')
$DevDataDir = Join-Path $homeDir ".homespun-dev-container"
$DevContainerName = "homespun-dev"

# Colors
function Write-Cyan { param($Message) Write-Host $Message -ForegroundColor Cyan }
function Write-Green { param($Message) Write-Host $Message -ForegroundColor Green }
function Write-Yellow { param($Message) Write-Host $Message -ForegroundColor Yellow }

if ($Stop) {
    # Stop mode: stop the container and delete the data directory
    Write-Cyan "=== Homespun Dev Stop ==="
    Write-Host ""

    # Call run.ps1 with stop and container name
    & "$ScriptDir\run.ps1" -Stop -ContainerName $DevContainerName

    # Delete the dev data directory
    if (Test-Path $DevDataDir) {
        Write-Yellow "Deleting dev data directory: $DevDataDir"
        Remove-Item -Recurse -Force $DevDataDir
        Write-Green "Dev data directory deleted."
    }
    else {
        Write-Cyan "Dev data directory does not exist: $DevDataDir"
    }

    exit 0
}

if ($Logs) {
    # Logs mode: pass through to run.ps1 with container name
    & "$ScriptDir\run.ps1" -Logs -ContainerName $DevContainerName
    exit 0
}

Write-Cyan "=== Homespun Dev Runner ==="
Write-Host ""
Write-Cyan "Data directory: $DevDataDir"
Write-Cyan "Container name: $DevContainerName"
Write-Host ""

# Build arguments for run.ps1
$runArgs = @{
    Local = $true
    DataDir = $DevDataDir
    ContainerName = $DevContainerName
}

if ($DebugBuild) {
    $runArgs.DebugBuild = $true
}

if ($Detach) {
    $runArgs.Detach = $true
}
else {
    # Default to interactive mode
    $runArgs.Interactive = $true
}

if ($Pull) {
    $runArgs.Pull = $true
}

# Run with dev settings
& "$ScriptDir\run.ps1" @runArgs
