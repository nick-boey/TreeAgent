#!/bin/bash
set -e

# Tailscale networking is handled by a sidecar container (see docker-compose.yml)
# This script simply starts the Homespun application
#
# Note: The Dockerfile pre-creates /home/homespun with world-writable permissions
# to support docker-compose user override (HOST_UID/HOST_GID) for proper volume ownership

echo "Starting Homespun..."
exec dotnet Homespun.dll
