#!/bin/bash
# ============================================================================
# Homespun Development Runner
# ============================================================================
#
# This script runs Homespun in a development environment with isolated storage.
# It wraps run.sh with dev-specific defaults.
#
# Usage:
#   ./dev.sh                    # Start dev container (interactive mode)
#   ./dev.sh --stop             # Stop dev container and delete data
#   ./dev.sh --logs             # View dev container logs
#   ./dev.sh -d                 # Run in detached mode
#
# Data Directory: ~/.homespun-dev-container
# Container Name: homespun-dev
#
# All other arguments are passed through to run.sh

set -e

# Get script directory
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Dev-specific settings
DEV_DATA_DIR="$HOME/.homespun-dev-container"
DEV_CONTAINER_NAME="homespun-dev"

# Colors
CYAN='\033[0;36m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

log_info() { echo -e "${CYAN}$1${NC}"; }
log_success() { echo -e "${GREEN}$1${NC}"; }
log_warn() { echo -e "${YELLOW}$1${NC}"; }

# Check if --stop is in arguments
STOP_MODE=false
for arg in "$@"; do
    if [ "$arg" = "--stop" ]; then
        STOP_MODE=true
        break
    fi
done

if [ "$STOP_MODE" = true ]; then
    # Stop mode: stop the container and delete the data directory
    log_info "=== Homespun Dev Stop ==="
    echo

    # Call run.sh with stop and container name
    "$SCRIPT_DIR/run.sh" --stop --container-name "$DEV_CONTAINER_NAME"

    # Delete the dev data directory
    if [ -d "$DEV_DATA_DIR" ]; then
        log_warn "Deleting dev data directory: $DEV_DATA_DIR"
        rm -rf "$DEV_DATA_DIR"
        log_success "Dev data directory deleted."
    else
        log_info "Dev data directory does not exist: $DEV_DATA_DIR"
    fi

    exit 0
fi

# Check if --logs is in arguments
LOGS_MODE=false
for arg in "$@"; do
    if [ "$arg" = "--logs" ]; then
        LOGS_MODE=true
        break
    fi
done

if [ "$LOGS_MODE" = true ]; then
    # Logs mode: pass through to run.sh with container name
    exec "$SCRIPT_DIR/run.sh" --logs --container-name "$DEV_CONTAINER_NAME"
fi

# Check if -d or --detach is in arguments (user wants detached mode)
DETACH_MODE=false
for arg in "$@"; do
    if [ "$arg" = "-d" ] || [ "$arg" = "--detach" ]; then
        DETACH_MODE=true
        break
    fi
done

# Build arguments, filtering out stop/logs if they somehow got here
FILTERED_ARGS=()
for arg in "$@"; do
    case "$arg" in
        --stop|--logs)
            # Already handled above
            ;;
        *)
            FILTERED_ARGS+=("$arg")
            ;;
    esac
done

# Default to interactive mode unless detach is specified
if [ "$DETACH_MODE" = false ]; then
    FILTERED_ARGS+=("-it")
fi

log_info "=== Homespun Dev Runner ==="
echo
log_info "Data directory: $DEV_DATA_DIR"
log_info "Container name: $DEV_CONTAINER_NAME"
echo

# Run with dev settings: --local + dev data dir + dev container name
exec "$SCRIPT_DIR/run.sh" \
    --local \
    --data-dir "$DEV_DATA_DIR" \
    --container-name "$DEV_CONTAINER_NAME" \
    "${FILTERED_ARGS[@]}"
