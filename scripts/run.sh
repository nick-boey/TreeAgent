#!/bin/bash
set -e

# ============================================================================
# Homespun Docker Runner
# ============================================================================
#
# This script runs Homespun using Docker with optional Watchtower
# for automatic updates from GHCR.
#
# Usage:
#   ./run.sh                    # Production: GHCR image + Watchtower (detached)
#   ./run.sh --local            # Development: local image, no Watchtower
#   ./run.sh --local -it        # Development: interactive mode
#   ./run.sh --stop             # Stop all containers
#   ./run.sh --logs             # View container logs
#
# Options:
#   --local                     Use locally built image (homespun:local)
#   --debug                     Build in Debug configuration (use with --local)
#   -it, --interactive          Run in interactive mode (foreground)
#   -d, --detach                Run in detached mode (background) [default]
#   --stop                      Stop running containers
#   --logs                      Follow container logs
#   --pull                      Pull latest image before starting
#   --external-hostname HOST    Set external hostname for agent URLs
#   --data-dir DIR              Override data directory (default: ~/.homespun-container/data)
#   --container-name NAME       Override container name (default: homespun)
#
# Environment Variables:
#   HSP_GITHUB_TOKEN            GitHub token (preferred for VM secrets)
#   HSP_CLAUDE_CODE_OAUTH_TOKEN Claude Code OAuth token (preferred for VM secrets)
#   HSP_TAILSCALE_AUTH_KEY      Tailscale auth key (preferred for VM secrets)
#   HSP_EXTERNAL_HOSTNAME       External hostname for agent URLs
#   GITHUB_TOKEN                GitHub token (fallback)
#   CLAUDE_CODE_OAUTH_TOKEN     Claude Code OAuth token (fallback)
#   TAILSCALE_AUTH_KEY          Tailscale auth key (fallback)
#
# Configuration File:
#   Place credentials in ~/.homespun/env to auto-load them:
#     export GITHUB_TOKEN=ghp_...
#     export CLAUDE_CODE_OAUTH_TOKEN=...
#     export TAILSCALE_AUTH_KEY=tskey-auth-...
#
# Volume Mounts:
#   Claude Code config (~/.claude) is automatically mounted for OAuth authentication

# Get script directory and repository root
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

# Default values
USE_LOCAL=false
USE_DEBUG=false
DETACHED=true
ACTION="start"
PULL_FIRST=false
EXTERNAL_HOSTNAME=""
DATA_DIR_PARAM=""
CONTAINER_NAME="homespun"
USER_SECRETS_ID="2cfc6c57-72da-4b56-944b-08f2c1df76f6"

# Colors
CYAN='\033[0;36m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m' # No Color

# Helper functions
log_info() { echo -e "${CYAN}$1${NC}"; }
log_success() { echo -e "${GREEN}$1${NC}"; }
log_warn() { echo -e "${YELLOW}$1${NC}"; }
log_error() { echo -e "${RED}$1${NC}"; }

show_help() {
    head -37 "$0" | tail -32
    exit 0
}

# Parse arguments
while [[ "$#" -gt 0 ]]; do
    case $1 in
        --local) USE_LOCAL=true ;;
        --debug) USE_DEBUG=true ;;
        -it|--interactive) DETACHED=false ;;
        -d|--detach) DETACHED=true ;;
        --stop) ACTION="stop" ;;
        --logs) ACTION="logs" ;;
        --pull) PULL_FIRST=true ;;
        --external-hostname) EXTERNAL_HOSTNAME="$2"; shift ;;
        --data-dir) DATA_DIR_PARAM="$2"; shift ;;
        --container-name) CONTAINER_NAME="$2"; shift ;;
        -h|--help) show_help ;;
        *) log_error "Unknown parameter: $1"; show_help ;;
    esac
    shift
done

# Change to repository root
cd "$REPO_ROOT"

echo
log_info "=== Homespun Docker Runner ==="
echo

# Handle stop action
if [ "$ACTION" = "stop" ]; then
    log_info "Stopping containers..."
    docker stop "$CONTAINER_NAME" 2>/dev/null || true
    docker stop watchtower 2>/dev/null || true
    docker rm "$CONTAINER_NAME" 2>/dev/null || true
    docker rm watchtower 2>/dev/null || true
    log_success "Containers stopped."
    exit 0
fi

# Handle logs action
if [ "$ACTION" = "logs" ]; then
    log_info "Following container logs (Ctrl+C to exit)..."
    docker logs -f "$CONTAINER_NAME"
    exit 0
fi

# Step 1: Validate Docker is running
log_info "[1/5] Checking Docker..."
if ! docker version >/dev/null 2>&1; then
    log_error "Docker is not running. Please start Docker and try again."
    exit 1
fi
log_success "      Docker is available."

# Step 2: Check/build image
log_info "[2/5] Checking container image..."
if [ "$USE_LOCAL" = true ]; then
    IMAGE_NAME="homespun:local"
    BUILD_CONFIG="Release"
    if [ "$USE_DEBUG" = true ]; then
        BUILD_CONFIG="Debug"
    fi
    log_info "      Building local image from $REPO_ROOT ($BUILD_CONFIG)..."
    if ! docker build -t "$IMAGE_NAME" --build-arg BUILD_CONFIGURATION="$BUILD_CONFIG" "$REPO_ROOT"; then
        log_error "Failed to build Docker image."
        exit 1
    fi
    log_success "      Local image built: $IMAGE_NAME ($BUILD_CONFIG)"
else
    IMAGE_NAME="ghcr.io/nick-boey/homespun:latest"
    if [ "$PULL_FIRST" = true ]; then
        log_info "      Pulling latest image..."
        docker pull "$IMAGE_NAME"
    fi
    log_success "      Using GHCR image: $IMAGE_NAME"
fi

# Step 3: Read credentials
log_info "[3/5] Reading credentials..."

# Source ~/.homespun/env if it exists (recommended location for credentials)
HOMESPUN_ENV_FILE="$HOME/.homespun/env"
if [ -f "$HOMESPUN_ENV_FILE" ]; then
    log_info "      Loading credentials from $HOMESPUN_ENV_FILE"
    source "$HOMESPUN_ENV_FILE"
fi

# GitHub Token: Check environment variables in order of preference
# 1. HSP_GITHUB_TOKEN (for VM secrets)
# 2. GITHUB_TOKEN (standard)
GITHUB_TOKEN="${HSP_GITHUB_TOKEN:-${GITHUB_TOKEN:-}}"

# If not in environment, try reading from .NET user secrets JSON
if [ -z "$GITHUB_TOKEN" ]; then
    SECRETS_PATH="$HOME/.microsoft/usersecrets/$USER_SECRETS_ID/secrets.json"
    if [ -f "$SECRETS_PATH" ]; then
        if command -v python3 &>/dev/null; then
            GITHUB_TOKEN=$(python3 -c "import json, sys; print(json.load(open('$SECRETS_PATH')).get('GitHub:Token', ''))" 2>/dev/null)
        elif command -v jq &>/dev/null; then
            GITHUB_TOKEN=$(jq -r '."GitHub:Token" // empty' "$SECRETS_PATH")
        fi
    fi
fi

# Try reading from .env file
if [ -z "$GITHUB_TOKEN" ] && [ -f "$REPO_ROOT/.env" ]; then
    GITHUB_TOKEN=$(grep -E "^GITHUB_TOKEN=" "$REPO_ROOT/.env" 2>/dev/null | cut -d'=' -f2- | tr -d '"' | tr -d "'" || true)
fi

if [ -z "$GITHUB_TOKEN" ]; then
    log_warn "      GitHub token not found."
    log_warn "      Set GITHUB_TOKEN in ~/.homespun/env or environment."
else
    MASKED_TOKEN="${GITHUB_TOKEN:0:10}..."
    log_success "      GitHub token found: $MASKED_TOKEN"
fi

# Claude Code OAuth Token: Check environment variables
# 1. HSP_CLAUDE_CODE_OAUTH_TOKEN (for VM secrets)
# 2. CLAUDE_CODE_OAUTH_TOKEN (standard)
CLAUDE_CODE_OAUTH_TOKEN="${HSP_CLAUDE_CODE_OAUTH_TOKEN:-${CLAUDE_CODE_OAUTH_TOKEN:-}}"

# Try reading from .env file
if [ -z "$CLAUDE_CODE_OAUTH_TOKEN" ] && [ -f "$REPO_ROOT/.env" ]; then
    CLAUDE_CODE_OAUTH_TOKEN=$(grep -E "^CLAUDE_CODE_OAUTH_TOKEN=" "$REPO_ROOT/.env" 2>/dev/null | cut -d'=' -f2- | tr -d '"' | tr -d "'" || true)
fi

if [ -z "$CLAUDE_CODE_OAUTH_TOKEN" ]; then
    log_warn "      Claude Code OAuth token not found."
    log_warn "      Set CLAUDE_CODE_OAUTH_TOKEN in ~/.homespun/env or environment."
else
    MASKED_CC_TOKEN="${CLAUDE_CODE_OAUTH_TOKEN:0:15}..."
    log_success "      Claude Code OAuth token found: $MASKED_CC_TOKEN"
fi

# Tailscale Auth Key: Check environment variables
# 1. HSP_TAILSCALE_AUTH_KEY (for VM secrets)
# 2. TAILSCALE_AUTH_KEY (standard)
TAILSCALE_AUTH_KEY="${HSP_TAILSCALE_AUTH_KEY:-${TAILSCALE_AUTH_KEY:-}}"

# Try reading from .env file
if [ -z "$TAILSCALE_AUTH_KEY" ] && [ -f "$REPO_ROOT/.env" ]; then
    TAILSCALE_AUTH_KEY=$(grep -E "^TAILSCALE_AUTH_KEY=" "$REPO_ROOT/.env" 2>/dev/null | cut -d'=' -f2- | tr -d '"' | tr -d "'" || true)
fi

if [ -z "$TAILSCALE_AUTH_KEY" ]; then
    log_warn "      Tailscale auth key not found (Tailscale will be disabled)."
    log_warn "      Set TAILSCALE_AUTH_KEY in ~/.homespun/env for VPN access."
else
    MASKED_TS_KEY="${TAILSCALE_AUTH_KEY:0:15}..."
    log_success "      Tailscale auth key found: $MASKED_TS_KEY"
fi

# Step 4: Set up directories
log_info "[4/5] Setting up directories..."

# Use DATA_DIR_PARAM if provided, otherwise default
if [ -n "$DATA_DIR_PARAM" ]; then
    DATA_DIR="$DATA_DIR_PARAM"
else
    DATA_DIR="$HOME/.homespun-container/data"
fi
SSH_DIR="$HOME/.ssh"
CLAUDE_DIR="$HOME/.claude"

if [ ! -d "$DATA_DIR" ]; then
    mkdir -p "$DATA_DIR"
    log_success "      Created data directory: $DATA_DIR"
else
    log_success "      Data directory exists: $DATA_DIR"
fi

chmod 777 "$DATA_DIR" 2>/dev/null || true

# Check SSH directory
SSH_MOUNT=""
if [ -d "$SSH_DIR" ]; then
    SSH_MOUNT="-v $SSH_DIR:/home/homespun/.ssh:ro"
    log_success "      SSH directory found: $SSH_DIR"
else
    log_warn "      SSH directory not found: $SSH_DIR"
fi

# Check Claude directory
CLAUDE_MOUNT=""
if [ -d "$CLAUDE_DIR" ]; then
    CLAUDE_MOUNT="-v $CLAUDE_DIR:/home/homespun/.claude"
    log_success "      Claude config found: $CLAUDE_DIR"
else
    log_warn "      Claude config not found: $CLAUDE_DIR"
fi

# Read external hostname
if [ -z "$EXTERNAL_HOSTNAME" ]; then
    EXTERNAL_HOSTNAME="${HSP_EXTERNAL_HOSTNAME:-}"
fi

# Try reading external hostname from .env file if not set
if [ -z "$EXTERNAL_HOSTNAME" ] && [ -f "$REPO_ROOT/.env" ]; then
    EXTERNAL_HOSTNAME=$(grep -E "^HSP_EXTERNAL_HOSTNAME=" "$REPO_ROOT/.env" 2>/dev/null | cut -d'=' -f2- | tr -d '"' | tr -d "'" || true)
fi

# Step 5: Start containers
log_info "[5/5] Starting containers..."
echo

# Export host user UID/GID so container runs as the same user
HOST_UID="$(id -u)"
HOST_GID="$(id -g)"

log_info "======================================"
log_info "  Container Configuration"
log_info "======================================"
echo "  Container:   $CONTAINER_NAME"
echo "  Image:       $IMAGE_NAME"
echo "  User:        $HOST_UID:$HOST_GID (host user)"
echo "  Port:        8080"
echo "  URL:         http://localhost:8080"
echo "  Data mount:  $DATA_DIR"
if [ -n "$SSH_MOUNT" ]; then
    echo "  SSH mount:   $SSH_DIR (read-only)"
fi
if [ -n "$CLAUDE_MOUNT" ]; then
    echo "  Claude auth: $CLAUDE_DIR"
fi
if [ -n "$TAILSCALE_AUTH_KEY" ]; then
    echo "  Tailscale:   Enabled (will connect on startup)"
fi
if [ -n "$EXTERNAL_HOSTNAME" ]; then
    echo "  Agent URLs:  https://$EXTERNAL_HOSTNAME:<port>"
fi
if [ "$USE_LOCAL" = false ]; then
    echo "  Watchtower:  Enabled (auto-updates every 5 min)"
else
    echo "  Watchtower:  Disabled (local development mode)"
fi
log_info "======================================"
echo

# Stop existing containers first
docker stop "$CONTAINER_NAME" 2>/dev/null || true
docker rm "$CONTAINER_NAME" 2>/dev/null || true

# Build docker run command
DOCKER_CMD="docker run"
if [ "$DETACHED" = true ]; then
    DOCKER_CMD="$DOCKER_CMD -d"
fi
DOCKER_CMD="$DOCKER_CMD --name $CONTAINER_NAME"
DOCKER_CMD="$DOCKER_CMD --user $HOST_UID:$HOST_GID"
DOCKER_CMD="$DOCKER_CMD -p 8080:8080"
DOCKER_CMD="$DOCKER_CMD -v $DATA_DIR:/data"
DOCKER_CMD="$DOCKER_CMD $SSH_MOUNT"
DOCKER_CMD="$DOCKER_CMD $CLAUDE_MOUNT"
DOCKER_CMD="$DOCKER_CMD -e HOME=/home/homespun"
DOCKER_CMD="$DOCKER_CMD -e ASPNETCORE_ENVIRONMENT=Production"
DOCKER_CMD="$DOCKER_CMD -e HSP_HOST_DATA_PATH=$DATA_DIR"

if [ -n "$GITHUB_TOKEN" ]; then
    DOCKER_CMD="$DOCKER_CMD -e GITHUB_TOKEN=$GITHUB_TOKEN"
fi

if [ -n "$CLAUDE_CODE_OAUTH_TOKEN" ]; then
    DOCKER_CMD="$DOCKER_CMD -e CLAUDE_CODE_OAUTH_TOKEN=$CLAUDE_CODE_OAUTH_TOKEN"
fi

if [ -n "$TAILSCALE_AUTH_KEY" ]; then
    DOCKER_CMD="$DOCKER_CMD -e TAILSCALE_AUTH_KEY=$TAILSCALE_AUTH_KEY"
fi

if [ -n "$EXTERNAL_HOSTNAME" ]; then
    DOCKER_CMD="$DOCKER_CMD -e HSP_EXTERNAL_HOSTNAME=$EXTERNAL_HOSTNAME"
fi

DOCKER_CMD="$DOCKER_CMD --restart unless-stopped"
DOCKER_CMD="$DOCKER_CMD --health-cmd 'curl -f http://localhost:8080/health || exit 1'"
DOCKER_CMD="$DOCKER_CMD --health-interval 30s"
DOCKER_CMD="$DOCKER_CMD --health-timeout 10s"
DOCKER_CMD="$DOCKER_CMD --health-retries 3"
DOCKER_CMD="$DOCKER_CMD --health-start-period 10s"
DOCKER_CMD="$DOCKER_CMD $IMAGE_NAME"

if [ "$DETACHED" = true ]; then
    log_info "Starting container in detached mode..."
    eval $DOCKER_CMD

    # Start Watchtower for production mode
    if [ "$USE_LOCAL" = false ]; then
        docker stop watchtower 2>/dev/null || true
        docker rm watchtower 2>/dev/null || true
        docker run -d \
            --name watchtower \
            -v /var/run/docker.sock:/var/run/docker.sock \
            -e WATCHTOWER_CLEANUP=true \
            -e WATCHTOWER_POLL_INTERVAL=${WATCHTOWER_POLL_INTERVAL:-300} \
            -e WATCHTOWER_INCLUDE_STOPPED=false \
            -e WATCHTOWER_ROLLING_RESTART=true \
            --restart unless-stopped \
            containrrr/watchtower "$CONTAINER_NAME"
    fi

    echo
    log_success "Container started successfully!"
    echo
    echo "Access URL: http://localhost:8080"
    echo
    echo "Useful commands:"
    echo "  View logs:     $0 --logs"
    echo "  Stop:          $0 --stop"
    echo "  Health check:  curl http://localhost:8080/health"
    echo
else
    log_warn "Starting container in interactive mode..."
    log_warn "Press Ctrl+C to stop."
    echo
    eval $DOCKER_CMD
    echo
    log_warn "Container stopped."
fi
