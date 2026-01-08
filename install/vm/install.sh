#!/bin/bash
#
# Homespun VM Installation Script
# 
# This script installs Homespun as a systemd service on Ubuntu and configures
# nginx as a reverse proxy. The application binds to the Tailscale interface.
#
# Usage: sudo ./install.sh [OPTIONS]
#
# Options:
#   --app-dir DIR      Application directory (default: /opt/homespun)
#   --data-dir DIR     Data directory (default: /var/lib/homespun)
#   --user USER        Service user (default: homespun)
#   --port PORT        Internal Kestrel port (default: 5000)
#   --help             Show this help message
#

set -e

# Default configuration
APP_DIR="/opt/homespun"
DATA_DIR="/var/lib/homespun"
SERVICE_USER="homespun"
KESTREL_PORT="5000"

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

show_help() {
    head -20 "$0" | tail -15
    exit 0
}

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --app-dir)
            APP_DIR="$2"
            shift 2
            ;;
        --data-dir)
            DATA_DIR="$2"
            shift 2
            ;;
        --user)
            SERVICE_USER="$2"
            shift 2
            ;;
        --port)
            KESTREL_PORT="$2"
            shift 2
            ;;
        --help)
            show_help
            ;;
        *)
            log_error "Unknown option: $1"
            show_help
            ;;
    esac
done

# Check if running as root
if [[ $EUID -ne 0 ]]; then
    log_error "This script must be run as root (use sudo)"
    exit 1
fi

# Check for Tailscale
if ! command -v tailscale &> /dev/null; then
    log_error "Tailscale is not installed. Please install Tailscale first."
    log_info "Visit: https://tailscale.com/download/linux"
    exit 1
fi

# Get Tailscale IP
TAILSCALE_IP=$(tailscale ip -4 2>/dev/null || true)
if [[ -z "$TAILSCALE_IP" ]]; then
    log_error "Could not detect Tailscale IP. Ensure Tailscale is connected."
    log_info "Run: tailscale status"
    exit 1
fi

log_info "Detected Tailscale IP: $TAILSCALE_IP"

# Check for .NET runtime
if ! command -v dotnet &> /dev/null; then
    log_warn ".NET runtime not found. Please install .NET 10.0 runtime."
    log_info "Visit: https://dotnet.microsoft.com/download/dotnet/10.0"
    log_info "Or run: sudo apt-get update && sudo apt-get install -y dotnet-runtime-10.0"
    exit 1
fi

DOTNET_VERSION=$(dotnet --version 2>/dev/null || echo "unknown")
log_info "Found .NET version: $DOTNET_VERSION"

# Create service user if it doesn't exist
if ! id "$SERVICE_USER" &>/dev/null; then
    log_info "Creating service user: $SERVICE_USER"
    useradd --system --no-create-home --shell /usr/sbin/nologin "$SERVICE_USER"
fi

# Create directories
log_info "Creating directories..."
mkdir -p "$APP_DIR"
mkdir -p "$DATA_DIR/.homespun"

# Set ownership
chown -R "$SERVICE_USER:$SERVICE_USER" "$DATA_DIR"

# Check if application files exist
if [[ ! -f "$APP_DIR/Homespun.dll" ]]; then
    log_warn "Application files not found in $APP_DIR"
    log_info "Please copy the published Homespun application to $APP_DIR"
    log_info "Build with: dotnet publish src/Homespun -c Release -o $APP_DIR"
fi

# Create systemd service file
log_info "Creating systemd service..."
cat > /etc/systemd/system/homespun.service << EOF
[Unit]
Description=Homespun - AI Agent Development Manager
After=network.target tailscaled.service
Wants=tailscaled.service

[Service]
Type=notify
User=$SERVICE_USER
Group=$SERVICE_USER
WorkingDirectory=$APP_DIR
ExecStart=/usr/bin/dotnet $APP_DIR/Homespun.dll
Restart=always
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=homespun
TimeoutStopSec=30

# Environment variables
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://$TAILSCALE_IP:$KESTREL_PORT
Environment=HOMESPUN_DATA_PATH=$DATA_DIR/.homespun/homespun-data.json
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false

# Security hardening
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=strict
ProtectHome=true
ReadWritePaths=$DATA_DIR

[Install]
WantedBy=multi-user.target
EOF

# Install nginx if not present
if ! command -v nginx &> /dev/null; then
    log_info "Installing nginx..."
    apt-get update
    apt-get install -y nginx
fi

# Create nginx configuration
log_info "Configuring nginx reverse proxy..."
cat > /etc/nginx/sites-available/homespun << EOF
# Homespun reverse proxy configuration
# Binds to Tailscale interface for secure access

server {
    listen $TAILSCALE_IP:80;
    server_name _;

    # Logging
    access_log /var/log/nginx/homespun_access.log;
    error_log /var/log/nginx/homespun_error.log;

    location / {
        proxy_pass http://$TAILSCALE_IP:$KESTREL_PORT;
        proxy_http_version 1.1;
        proxy_set_header Upgrade \$http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
        proxy_cache_bypass \$http_upgrade;

        # Timeout settings for long-running connections (SignalR)
        proxy_connect_timeout 60s;
        proxy_send_timeout 60s;
        proxy_read_timeout 60s;
    }

    # SignalR hub endpoints - extended timeouts
    location /hubs/ {
        proxy_pass http://$TAILSCALE_IP:$KESTREL_PORT;
        proxy_http_version 1.1;
        proxy_set_header Upgrade \$http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
        proxy_cache_bypass \$http_upgrade;

        # Extended timeouts for WebSocket connections
        proxy_connect_timeout 7d;
        proxy_send_timeout 7d;
        proxy_read_timeout 7d;
    }

    # Health check endpoint
    location /health {
        proxy_pass http://$TAILSCALE_IP:$KESTREL_PORT/health;
        proxy_http_version 1.1;
        proxy_set_header Host \$host;
    }
}
EOF

# Enable the nginx site
ln -sf /etc/nginx/sites-available/homespun /etc/nginx/sites-enabled/homespun

# Remove default site if it exists
rm -f /etc/nginx/sites-enabled/default

# Test nginx configuration
log_info "Testing nginx configuration..."
nginx -t

# Reload systemd
systemctl daemon-reload

log_info "Installation complete!"
echo ""
echo "========================================"
echo "  Homespun Installation Summary"
echo "========================================"
echo ""
echo "  Application directory: $APP_DIR"
echo "  Data directory:        $DATA_DIR"
echo "  Service user:          $SERVICE_USER"
echo "  Tailscale IP:          $TAILSCALE_IP"
echo "  Internal port:         $KESTREL_PORT"
echo ""
echo "  Next steps:"
echo "  1. Copy published app to $APP_DIR (if not done)"
echo "  2. Set GITHUB_TOKEN in /etc/systemd/system/homespun.service"
echo "  3. Run: sudo ./run.sh"
echo "  4. Access: http://$TAILSCALE_IP"
echo ""
echo "  To set GitHub token:"
echo "  sudo systemctl edit homespun"
echo "  Add: Environment=GITHUB_TOKEN=ghp_your_token"
echo ""
