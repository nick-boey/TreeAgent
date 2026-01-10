#!/bin/bash
#
# Homespun Service Runner
#
# This script enables and starts the Homespun systemd service.
# Run this after install.sh to start the application.
#
# Usage: sudo ./run.sh
#

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

log_info() {
    echo -e "${GREEN}[INFO]${NC} $1"
}

log_warn() {
    echo -e "${YELLOW}[WARN]${NC} $1"
}

log_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Check if running as root
if [[ $EUID -ne 0 ]]; then
    log_error "This script must be run as root (use sudo)"
    exit 1
fi

# Check if service file exists
if [[ ! -f /etc/systemd/system/homespun.service ]]; then
    log_error "Homespun service not found. Run install.sh first."
    exit 1
fi

# Reload systemd daemon to pick up any changes
log_info "Reloading systemd daemon..."
systemctl daemon-reload

# Restart nginx
log_info "Restarting nginx..."
systemctl restart nginx
systemctl enable nginx

# Enable and start the Homespun service
log_info "Enabling Homespun service at boot..."
systemctl enable homespun

log_info "Starting Homespun service..."
systemctl start homespun

# Wait a moment for the service to start
sleep 3

# Check service status
if systemctl is-active --quiet homespun; then
    log_info "Homespun service is running!"
else
    log_error "Homespun service failed to start. Checking logs..."
    journalctl -u homespun -n 20 --no-pager
    exit 1
fi

# Get Tailscale IP
TAILSCALE_IP=$(tailscale ip -4 2>/dev/null || echo "unknown")

echo ""
echo "========================================"
echo "  Homespun is now running!"
echo "========================================"
echo ""
echo "  Access the application at:"
echo "  http://$TAILSCALE_IP"
echo ""
echo "  Useful commands:"
echo "  - View logs:     sudo journalctl -u homespun -f"
echo "  - Stop service:  sudo systemctl stop homespun"
echo "  - Restart:       sudo systemctl restart homespun"
echo "  - Check status:  sudo systemctl status homespun"
echo "  - Health check:  curl http://$TAILSCALE_IP/health"
echo ""

# Show brief status
systemctl status homespun --no-pager -l | head -15
