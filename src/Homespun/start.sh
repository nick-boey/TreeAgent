#!/bin/bash
set -e

# Tailscale networking is handled by a sidecar container (see docker-compose.yml)
# This script simply starts the Homespun application
#
# Note: The Dockerfile pre-creates /home/homespun with world-writable permissions
# to support docker-compose user override (HOST_UID/HOST_GID) for proper volume ownership

# Ensure HOME is set correctly (Windows Docker may pass incorrect HOME)
if [ "$(id -u)" = "0" ]; then
    export HOME=/root
else
    export HOME=/home/homespun
fi

# Configure git to trust mounted directories (avoids "dubious ownership" errors)
# This needs to run at startup because the user context may differ from build time
git config --global --add safe.directory '*' 2>/dev/null || true

echo "Starting Homespun..."

# Build command line arguments
ARGS=""

# Check for test agent auto-start
if [ -n "$START_TEST_AGENT_PROJECT" ]; then
    echo "Test agent auto-start enabled for project: $START_TEST_AGENT_PROJECT"
    ARGS="$ARGS --start-test-agent=$START_TEST_AGENT_PROJECT"
fi

exec dotnet Homespun.dll $ARGS
