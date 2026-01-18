#!/bin/bash
set -e

# Homespun Container Startup Script
# This script handles:
# 1. Tailscale setup (if TS_AUTHKEY is provided)
# 2. Git configuration
# 3. Starting the Homespun application
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

# Configure git for the container
# - core.askpass: Use askpass script for git credential prompts (created by GitHubEnvironmentService)
# - credential.helper: Disable credential helper to ensure askpass is used
# - user.name/email: Default identity for commits (can be overridden by environment)
git config --global core.askpass /data/git-askpass.sh 2>/dev/null || true
git config --global credential.helper '' 2>/dev/null || true
git config --global user.name "${GIT_AUTHOR_NAME:-Homespun Bot}" 2>/dev/null || true
git config --global user.email "${GIT_AUTHOR_EMAIL:-homespun@localhost}" 2>/dev/null || true

# Start Tailscale if auth key is provided
# Accept both TAILSCALE_AUTH_KEY (preferred) and TS_AUTHKEY for compatibility
TS_AUTHKEY="${TAILSCALE_AUTH_KEY:-$TS_AUTHKEY}"
if [ -n "$TS_AUTHKEY" ]; then
    echo "Starting Tailscale..."

    # Start tailscaled daemon in userspace mode
    tailscaled --state="${TS_STATE_DIR:-/var/lib/tailscale}/tailscaled.state" \
               --socket="${TS_STATE_DIR:-/var/lib/tailscale}/tailscaled.sock" \
               --tun=userspace-networking &

    # Wait for tailscaled to be ready
    sleep 2

    # Connect to Tailscale
    tailscale --socket="${TS_STATE_DIR:-/var/lib/tailscale}/tailscaled.sock" up \
              --authkey="$TS_AUTHKEY" \
              --hostname="${TS_HOSTNAME:-homespun}" \
              --accept-routes \
              --reset

    echo "Tailscale connected as ${TS_HOSTNAME:-homespun}"

    # Enable HTTPS serving (proxies port 443 to the app on 8080)
    echo "Enabling Tailscale HTTPS serve..."
    tailscale --socket="${TS_STATE_DIR:-/var/lib/tailscale}/tailscaled.sock" serve --bg --https=443 http://127.0.0.1:8080 || true
    echo "Tailscale HTTPS proxy enabled on port 443"

    # Show Tailscale status
    tailscale --socket="${TS_STATE_DIR:-/var/lib/tailscale}/tailscaled.sock" status || true
fi

echo "Starting Homespun..."

# Build command line arguments
ARGS=""

# Check for test agent auto-start
if [ -n "$START_TEST_AGENT_PROJECT" ]; then
    echo "Test agent auto-start enabled for project: $START_TEST_AGENT_PROJECT"
    ARGS="$ARGS --start-test-agent=$START_TEST_AGENT_PROJECT"
fi

exec dotnet Homespun.dll $ARGS
