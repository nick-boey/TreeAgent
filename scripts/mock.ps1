#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Starts Homespun in mock mode with seeded demo data.

.DESCRIPTION
    This script runs Homespun with mock services and demo data.
    No external dependencies (GitHub API, Claude API, etc.) are required.
    Useful for UI development and testing.

.EXAMPLE
    .\mock.ps1
    Starts the application in mock mode at https://localhost:5094

.NOTES
    URL: https://localhost:5094
    Mock data includes demo projects, features, and issues.
#>

#Requires -Version 7.0

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Get script directory
$ScriptDir = $PSScriptRoot
$ProjectDir = Split-Path $ScriptDir -Parent

Write-Host "=== Homespun Mock Mode ===" -ForegroundColor Cyan
Write-Host "Starting with mock services and demo data..." -ForegroundColor Cyan
Write-Host ""

& dotnet run --project "$ProjectDir\src\Homespun" --launch-profile mock
