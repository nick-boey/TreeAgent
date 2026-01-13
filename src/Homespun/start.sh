#!/bin/bash
set -e

# Start Tailscale if configured
if [ ! -z "$TAILSCALE_AUTH_KEY" ]; then
    echo "Starting Tailscale..."

    # Use persistent storage for Tailscale state
    # This ensures the device ID remains consistent across container restarts
    TS_STATE_DIR="/data/tailscale"
    mkdir -p "$TS_STATE_DIR"
    mkdir -p /tmp/tailscale

    # Start tailscaled in background with userspace networking
    # Using /tmp for socket
    tailscaled \
        --tun=userspace-networking \
        --socket=/tmp/tailscale/tailscaled.sock \
        --state="$TS_STATE_DIR/tailscaled.state" \
        &

    # Wait for tailscaled socket
    echo "Waiting for tailscaled..."
    TIMEOUT=10
    COUNT=0
    while [ ! -S /tmp/tailscale/tailscaled.sock ]; do
        sleep 0.5
        COUNT=$((COUNT+1))
        if [ $COUNT -ge $((TIMEOUT*2)) ]; then
            echo "Timed out waiting for tailscaled socket"
            break
        fi
    done

    # Authenticate
    # Use provided hostname or default
    TS_HOSTNAME=${TAILSCALE_HOSTNAME:-homespun-prod}

    echo "Authenticating with Tailscale as $TS_HOSTNAME..."
    tailscale --socket=/tmp/tailscale/tailscaled.sock up \
        --authkey="${TAILSCALE_AUTH_KEY}" \
        --hostname="${TS_HOSTNAME}" \
        --ssh \
        --accept-routes

    # Serve the application on the Tailscale network (Port 80 -> Localhost 8080)
    # This enables access via http://hostname on the tailnet
    echo "Configuring Tailscale Serve..."
    tailscale --socket=/tmp/tailscale/tailscaled.sock serve --bg --http=80 http://localhost:8080

    echo "Tailscale started."
fi

echo "Starting Homespun..."
exec dotnet Homespun.dll
