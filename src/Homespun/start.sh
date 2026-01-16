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

# Configure git for the container
# - core.askpass: Use askpass script for git credential prompts (created by GitHubEnvironmentService)
# - credential.helper: Disable credential helper to ensure askpass is used
# - user.name/email: Default identity for commits (can be overridden by environment)
git config --global core.askpass /data/git-askpass.sh 2>/dev/null || true
git config --global credential.helper '' 2>/dev/null || true
git config --global user.name "${GIT_AUTHOR_NAME:-Homespun Bot}" 2>/dev/null || true
git config --global user.email "${GIT_AUTHOR_EMAIL:-homespun@localhost}" 2>/dev/null || true

# Build command line arguments
ARGS=""

# Check for test agent auto-start
if [ -n "$START_TEST_AGENT_PROJECT" ]; then
    echo "Test agent auto-start enabled for project: $START_TEST_AGENT_PROJECT"
    ARGS="$ARGS --start-test-agent=$START_TEST_AGENT_PROJECT"
fi

exec dotnet Homespun.dll $ARGS
