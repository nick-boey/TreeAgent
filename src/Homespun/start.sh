#!/bin/bash
set -e

# Tailscale networking is handled by a sidecar container (see docker-compose.yml)
# This script simply starts the Homespun application

echo "Starting Homespun..."
exec dotnet Homespun.dll
