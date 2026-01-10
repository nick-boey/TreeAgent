#!/bin/bash
set -e

# ============================================================================
# Homespun Docker Container Runner (Bash)
# ============================================================================

# Default values
TAILSCALE_AUTH_KEY=""
TAILSCALE_HOSTNAME="homespun-container"
USER_SECRETS_ID="2cfc6c57-72da-4b56-944b-08f2c1df76f6"
IMAGE_NAME="homespun:local"
CONTAINER_NAME="homespun-local"

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

# Parse arguments
while [[ "$#" -gt 0 ]]; do
    case $1 in
        --tailscale-auth-key) TAILSCALE_AUTH_KEY="$2"; shift ;;
        --tailscale-hostname) TAILSCALE_HOSTNAME="$2"; shift ;;
        *) echo "Unknown parameter passed: $1"; exit 1 ;;
    esac
    shift
done

echo
log_info "=== Homespun Docker Container Runner ==="
echo

# Step 1: Validate Docker is running
log_info "[1/6] Checking Docker..."
if ! docker version >/dev/null 2>&1; then
    log_error "Docker is not running. Please start Docker and try again."
    exit 1
fi
log_success "      Docker is running."

# Step 2: Check if image exists
log_info "[2/6] Checking for homespun:local image..."
if ! docker images --format "{{.Repository}}:{{.Tag}}" | grep -q "^${IMAGE_NAME}$"; then
    log_error "Docker image '${IMAGE_NAME}' not found."
    echo
    echo "Please build the image first:"
    echo "    docker build -t ${IMAGE_NAME} ."
    echo
    echo "Then run this script again."
    exit 1
fi
log_success "      Image found."

# Step 3: Read GitHub token
log_info "[3/6] Reading GitHub token..."
GITHUB_TOKEN=""

# Try reading from .NET user secrets JSON directly if available
SECRETS_PATH="$HOME/.microsoft/usersecrets/$USER_SECRETS_ID/secrets.json"
if [ -f "$SECRETS_PATH" ]; then
    # Try to extract token using grep/sed to avoid jq dependency if possible, or python
    if command -v python3 &>/dev/null; then
        GITHUB_TOKEN=$(python3 -c "import json, sys; print(json.load(open('$SECRETS_PATH')).get('GitHub:Token', ''))" 2>/dev/null)
    elif command -v jq &>/dev/null; then
         GITHUB_TOKEN=$(jq -r '."GitHub:Token" // empty' "$SECRETS_PATH")
    fi
fi

# Fallback: Check environment variable if not found in secrets
if [ -z "$GITHUB_TOKEN" ]; then
    GITHUB_TOKEN="${GITHUB_TOKEN:-}"
fi

if [ -z "$GITHUB_TOKEN" ]; then
    log_warn "      GitHub token not found in user secrets or environment."
    log_warn "      Container will run without GitHub integration."
else
    MASKED_TOKEN="${GITHUB_TOKEN:0:10}..."
    log_success "      GitHub token found: $MASKED_TOKEN"
fi

# Step 4: Set up paths
log_info "[4/6] Setting up directories..."

DATA_DIR="$HOME/.homespun-container/data"
SSH_DIR="$HOME/.ssh"

# Create data directory
if [ ! -d "$DATA_DIR" ]; then
    mkdir -p "$DATA_DIR"
    log_success "      Created data directory: $DATA_DIR"
else
    log_success "      Data directory exists: $DATA_DIR"
fi

# Check SSH directory
MOUNT_SSH=false
if [ ! -d "$SSH_DIR" ]; then
    log_warn "      SSH directory not found: $SSH_DIR"
    log_warn "      Git operations requiring SSH may not work."
else
    log_success "      SSH directory found: $SSH_DIR"
    MOUNT_SSH=true
fi

# Step 5: Stop existing container
log_info "[5/6] Checking for existing container..."
if docker ps -a --format '{{.Names}}' | grep -q "^${CONTAINER_NAME}$"; then
    log_warn "Stopping existing container '${CONTAINER_NAME}'..."
    docker stop "$CONTAINER_NAME" >/dev/null 2>&1 || true
    docker rm "$CONTAINER_NAME" >/dev/null 2>&1 || true
    log_success "Existing container removed."
fi

# Step 6: Run container
log_info "[6/6] Starting container..."
echo
log_info "======================================"
log_info "  Container Configuration"
log_info "======================================"
echo "  Name:        $CONTAINER_NAME"
echo "  Port:        8080"
echo "  URL:         http://localhost:8080"
echo "  Environment: Production"
echo "  Data mount:  $DATA_DIR"
if [ "$MOUNT_SSH" = true ]; then
    echo "  SSH mount:   $SSH_DIR (read-only)"
fi
if [ -n "$TAILSCALE_AUTH_KEY" ]; then
    echo "  Tailscale:   Enabled ($TAILSCALE_HOSTNAME)"
fi
log_info "======================================"
echo
log_warn "Starting container in interactive mode..."
log_warn "Press Ctrl+C to stop the container."
echo

# Build docker run command
DOCKER_CMD=(docker run --rm -it)
DOCKER_CMD+=("--name" "$CONTAINER_NAME")
DOCKER_CMD+=("-p" "8080:8080")
DOCKER_CMD+=("-v" "$DATA_DIR:/data")

if [ "$MOUNT_SSH" = true ]; then
    DOCKER_CMD+=("-v" "$SSH_DIR:/home/homespun/.ssh:ro")
fi

if [ -n "$GITHUB_TOKEN" ]; then
    DOCKER_CMD+=("-e" "GITHUB_TOKEN=$GITHUB_TOKEN")
fi

if [ -n "$TAILSCALE_AUTH_KEY" ]; then
    DOCKER_CMD+=("-e" "TAILSCALE_AUTH_KEY=$TAILSCALE_AUTH_KEY")
    DOCKER_CMD+=("-e" "TAILSCALE_HOSTNAME=$TAILSCALE_HOSTNAME")
fi

DOCKER_CMD+=("$IMAGE_NAME")

# Execute
"${DOCKER_CMD[@]}"

echo
log_warn "Container stopped."
