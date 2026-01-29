#!/bin/bash
# ============================================================================
# Homespun Mock Mode
# ============================================================================
#
# Starts Homespun in mock mode with seeded demo data.
# This mode uses mock services and doesn't require external dependencies
# (GitHub API, Claude API, etc.)
#
# Usage:
#   ./mock.sh              # Start in mock mode
#
# The application runs at: https://localhost:5094

set -e

# Get script directory
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"

# Colors
CYAN='\033[0;36m'
NC='\033[0m' # No Color

log_info() { echo -e "${CYAN}$1${NC}"; }

log_info "=== Homespun Mock Mode ==="
log_info "Starting with mock services and demo data..."
echo

exec dotnet run --project "$PROJECT_DIR/src/Homespun" --launch-profile mock
