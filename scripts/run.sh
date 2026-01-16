#!/bin/bash
set -e

# ============================================================================
# Homespun Docker Compose Runner
# ============================================================================
#
# This script runs Homespun using Docker Compose with optional Watchtower
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
#   --tailscale                 Enable Tailscale sidecar for tailnet access
#   --tailscale-auth-key KEY    Set Tailscale auth key (implies --tailscale)
#   --tailscale-hostname NAME   Set Tailscale hostname (default: homespun)
#   --external-hostname HOST    Set external hostname for agent URLs (e.g., homespun.tail1234.ts.net)
#
# Environment Variables:
#   HSP_GITHUB_TOKEN            GitHub token (preferred for VM secrets)
#   HSP_TAILSCALE_AUTH_KEY      Tailscale auth key (preferred for VM secrets)
#   HSP_EXTERNAL_HOSTNAME       External hostname for agent URLs
#   GITHUB_TOKEN                GitHub token (fallback)
#   TAILSCALE_AUTH_KEY          Tailscale auth key (fallback)
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
USE_TAILSCALE=false
TAILSCALE_AUTH_KEY=""
TAILSCALE_HOSTNAME="homespun"
EXTERNAL_HOSTNAME=""
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
    head -25 "$0" | tail -20
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
        --tailscale) USE_TAILSCALE=true ;;
        --tailscale-auth-key) TAILSCALE_AUTH_KEY="$2"; USE_TAILSCALE=true; shift ;;
        --tailscale-hostname) TAILSCALE_HOSTNAME="$2"; shift ;;
        --external-hostname) EXTERNAL_HOSTNAME="$2"; shift ;;
        -h|--help) show_help ;;
        *) log_error "Unknown parameter: $1"; show_help ;;
    esac
    shift
done

# Change to repository root for docker-compose
cd "$REPO_ROOT"

echo
log_info "=== Homespun Docker Compose Runner ==="
echo

# Handle stop action
if [ "$ACTION" = "stop" ]; then
    log_info "Stopping containers..."
    docker compose --profile production --profile tailscale --profile standalone down 2>/dev/null || docker compose down
    log_success "Containers stopped."
    exit 0
fi

# Handle logs action
if [ "$ACTION" = "logs" ]; then
    log_info "Following container logs (Ctrl+C to exit)..."
    docker compose logs -f homespun
    exit 0
fi

# Step 1: Validate Docker is running
log_info "[1/5] Checking Docker..."
if ! docker version >/dev/null 2>&1; then
    log_error "Docker is not running. Please start Docker and try again."
    exit 1
fi
if ! docker compose version >/dev/null 2>&1; then
    log_error "Docker Compose is not available. Please install Docker Compose."
    exit 1
fi
log_success "      Docker and Docker Compose are available."

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

# Step 3: Read GitHub token
log_info "[3/5] Reading GitHub token..."

# Check environment variables in order of preference:
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
    log_warn "      Set HSP_GITHUB_TOKEN or GITHUB_TOKEN environment variable."
    log_warn "      See .env.example for template."
else
    MASKED_TOKEN="${GITHUB_TOKEN:0:10}..."
    log_success "      GitHub token found: $MASKED_TOKEN"
fi

# Read Tailscale auth key from environment if not passed as argument
if [ -z "$TAILSCALE_AUTH_KEY" ]; then
    # Check HSP_TAILSCALE_AUTH_KEY first (for VM secrets), then TAILSCALE_AUTH_KEY
    TAILSCALE_AUTH_KEY="${HSP_TAILSCALE_AUTH_KEY:-${TAILSCALE_AUTH_KEY:-}}"
fi

# Try reading Tailscale auth key from .env file if not set
if [ -z "$TAILSCALE_AUTH_KEY" ] && [ -f "$REPO_ROOT/.env" ]; then
    TAILSCALE_AUTH_KEY=$(grep -E "^TAILSCALE_AUTH_KEY=" "$REPO_ROOT/.env" 2>/dev/null | cut -d'=' -f2- | tr -d '"' | tr -d "'" || true)
fi

# If Tailscale auth key is set, enable Tailscale mode
if [ -n "$TAILSCALE_AUTH_KEY" ]; then
    USE_TAILSCALE=true
    MASKED_TS_KEY="${TAILSCALE_AUTH_KEY:0:15}..."
    log_success "      Tailscale auth key found: $MASKED_TS_KEY"
fi

# Step 4: Set up directories
log_info "[4/5] Setting up directories..."

DATA_DIR="$HOME/.homespun-container/data"
SSH_DIR="$HOME/.ssh"
CLAUDE_CREDENTIALS_FILE="$HOME/.claude/.credentials.json"

if [ ! -d "$DATA_DIR" ]; then
    mkdir -p "$DATA_DIR"
    log_success "      Created data directory: $DATA_DIR"
else
    log_success "      Data directory exists: $DATA_DIR"
fi

chmod 777 "$DATA_DIR" 2>/dev/null || true

# Check SSH directory
if [ ! -d "$SSH_DIR" ]; then
    log_warn "      SSH directory not found: $SSH_DIR"
    SSH_DIR=""
fi

# Check Claude Code credentials file (for OAuth authentication)
if [ ! -f "$CLAUDE_CREDENTIALS_FILE" ]; then
    log_warn "      Claude credentials not found: $CLAUDE_CREDENTIALS_FILE"
    log_warn "      Run 'claude login' on host to authenticate Claude Code."
    CLAUDE_CREDENTIALS_FILE=""
else
    log_success "      Claude credentials found: $CLAUDE_CREDENTIALS_FILE"
fi

# Step 5: Start containers
log_info "[5/5] Starting containers..."
echo

# Read external hostname from environment if not passed as argument
if [ -z "$EXTERNAL_HOSTNAME" ]; then
    EXTERNAL_HOSTNAME="${HSP_EXTERNAL_HOSTNAME:-}"
fi

# Try reading external hostname from .env file if not set
if [ -z "$EXTERNAL_HOSTNAME" ] && [ -f "$REPO_ROOT/.env" ]; then
    EXTERNAL_HOSTNAME=$(grep -E "^HSP_EXTERNAL_HOSTNAME=" "$REPO_ROOT/.env" 2>/dev/null | cut -d'=' -f2- | tr -d '"' | tr -d "'" || true)
fi

if [ -n "$EXTERNAL_HOSTNAME" ]; then
    log_success "      External hostname: $EXTERNAL_HOSTNAME"
fi

# Export environment variables for docker-compose
export HOMESPUN_IMAGE="$IMAGE_NAME"
export DATA_DIR="$DATA_DIR"
export SSH_DIR="${SSH_DIR:-/dev/null}"
export CLAUDE_CREDENTIALS_FILE="${CLAUDE_CREDENTIALS_FILE:-/dev/null}"
export GITHUB_TOKEN="$GITHUB_TOKEN"
export TAILSCALE_AUTH_KEY="$TAILSCALE_AUTH_KEY"
export TAILSCALE_HOSTNAME="$TAILSCALE_HOSTNAME"
export HSP_EXTERNAL_HOSTNAME="$EXTERNAL_HOSTNAME"

# Export host user UID/GID so container runs as the same user
# This ensures files created in mounted volumes are accessible to the host user
export HOST_UID="$(id -u)"
export HOST_GID="$(id -g)"

# Check if Tailscale is available on the host and get the IP
TAILSCALE_IP=""
TAILSCALE_URL=""
if command -v tailscale &>/dev/null; then
    TAILSCALE_IP=$(tailscale ip -4 2>/dev/null || true)
    if [ -n "$TAILSCALE_IP" ]; then
        TAILSCALE_URL="http://${TAILSCALE_IP}:8080"
    fi
fi

# Determine compose profiles
COMPOSE_PROFILES=""
if [ "$USE_LOCAL" = false ]; then
    COMPOSE_PROFILES="--profile production"
fi
if [ "$USE_TAILSCALE" = true ]; then
    COMPOSE_PROFILES="$COMPOSE_PROFILES --profile tailscale"
else
    COMPOSE_PROFILES="$COMPOSE_PROFILES --profile standalone"
fi

log_info "======================================"
log_info "  Container Configuration"
log_info "======================================"
echo "  Image:       $IMAGE_NAME"
echo "  User:        $HOST_UID:$HOST_GID (host user)"
echo "  Port:        8080"
echo "  URL:         http://localhost:8080"
if [ -n "$TAILSCALE_URL" ]; then
    echo "  Tailnet URL: $TAILSCALE_URL"
fi
echo "  Data mount:  $DATA_DIR"
if [ -n "$SSH_DIR" ] && [ "$SSH_DIR" != "/dev/null" ]; then
    echo "  SSH mount:   $SSH_DIR (read-only)"
fi
if [ -n "$CLAUDE_CREDENTIALS_FILE" ] && [ "$CLAUDE_CREDENTIALS_FILE" != "/dev/null" ]; then
    echo "  Claude auth: $CLAUDE_CREDENTIALS_FILE (read-only)"
fi
if [ "$USE_TAILSCALE" = true ]; then
    echo "  Tailscale:   Enabled via sidecar ($TAILSCALE_HOSTNAME)"
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

if [ "$DETACHED" = true ]; then
    log_info "Starting containers in detached mode..."
    docker compose $COMPOSE_PROFILES up -d
    echo
    log_success "Containers started successfully!"
    echo
    echo "Access URLs:"
    if [ "$USE_TAILSCALE" = true ]; then
        echo "  Tailnet:     https://${TAILSCALE_HOSTNAME}.<your-tailnet>.ts.net"
        echo "  OpenCode:    https://${TAILSCALE_HOSTNAME}.<your-tailnet>.ts.net:4096-4105"
    else
        echo "  Local:       http://localhost:8080"
        if [ -n "$TAILSCALE_URL" ]; then
            echo "  Tailnet:     $TAILSCALE_URL"
        fi
    fi
    echo
    echo "Useful commands:"
    echo "  View logs:     $0 --logs"
    echo "  Stop:          $0 --stop"
    echo "  Health check:  curl http://localhost:8080/health"
    echo
else
    log_warn "Starting containers in interactive mode..."
    log_warn "Press Ctrl+C to stop."
    echo
    docker compose $COMPOSE_PROFILES up
    echo
    log_warn "Containers stopped."
fi
