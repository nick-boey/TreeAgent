#!/bin/bash
set -e

# ============================================================================
# Homespun Test Script
# ============================================================================
#
# Builds and runs the container locally for testing.
# Uses interactive mode without Watchtower.
#
# Usage:
#   ./test.sh              # Build and run interactively
#   ./test.sh --no-build   # Run without rebuilding
#   ./test.sh --debug      # Build in Debug configuration
#
# Environment Variables:
#   HSP_GITHUB_TOKEN       GitHub token
#   HSP_TAILSCALE_AUTH     Tailscale auth key

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

# Colors
CYAN='\033[0;36m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'

log_info() { echo -e "${CYAN}$1${NC}"; }
log_success() { echo -e "${GREEN}$1${NC}"; }
log_warn() { echo -e "${YELLOW}$1${NC}"; }
log_error() { echo -e "${RED}$1${NC}"; }

# Parse arguments
SKIP_BUILD=false
BUILD_CONFIG="Release"
while [[ "$#" -gt 0 ]]; do
    case $1 in
        --no-build) SKIP_BUILD=true ;;
        --debug) BUILD_CONFIG="Debug" ;;
        -h|--help)
            echo "Usage: $0 [--no-build] [--debug]"
            echo ""
            echo "Options:"
            echo "  --no-build    Skip building the container"
            echo "  --debug       Build in Debug configuration"
            exit 0
            ;;
        *) log_error "Unknown parameter: $1"; exit 1 ;;
    esac
    shift
done

echo
log_info "=== Homespun Test Runner ==="
echo

# Check environment variables
if [ -z "$HSP_GITHUB_TOKEN" ]; then
    log_warn "HSP_GITHUB_TOKEN not set"
fi

if [ -z "$HSP_TAILSCALE_AUTH" ]; then
    log_warn "HSP_TAILSCALE_AUTH not set (Tailscale will not be configured)"
fi

# Build the container
if [ "$SKIP_BUILD" = false ]; then
    log_info "Building container ($BUILD_CONFIG configuration)..."
    echo
    docker build -t homespun:local --build-arg BUILD_CONFIGURATION="$BUILD_CONFIG" "$PROJECT_ROOT"
    echo
    log_success "Build complete!"
    echo
fi

# Run the container
log_info "Starting container in interactive mode..."
echo

cd "$SCRIPT_DIR"
./run.sh --local -it
