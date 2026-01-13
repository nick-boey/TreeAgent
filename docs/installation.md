# Homespun installation guide

This guide covers deploying Homespun to Ubuntu virtual machines and Docker containers.

## Table of contents

- [Prerequisites](#prerequisites)
- [VM deployment](#vm-deployment)
- [Container deployment](#container-deployment)
- [Post-deployment configuration](#post-deployment-configuration)
- [Troubleshooting](#troubleshooting)

## Prerequisites

### Common requirements

- **GitHub personal access token**: Required for PR synchronization. Create one at [GitHub Settings > Developer settings > Personal access tokens](https://github.com/settings/tokens) with `repo` scope.
- **Tailscale**: For secure remote access over your private network. Install from [tailscale.com/download](https://tailscale.com/download).

### VM-specific requirements

- Ubuntu 20.04 LTS or later
- .NET 10.0 runtime
- `sudo` access

### Container-specific requirements

- Docker 20.10 or later
- Docker Compose (optional, for easier management)

## VM deployment

Deploy Homespun as a systemd service on Ubuntu with nginx as a reverse proxy. The application binds to your Tailscale interface for secure access.

### Step 1: Install prerequisites

```bash
# Install .NET 10.0 runtime
wget https://packages.microsoft.com/config/ubuntu/$(lsb_release -rs)/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
rm packages-microsoft-prod.deb
sudo apt-get update
sudo apt-get install -y dotnet-runtime-10.0

# Install Tailscale
curl -fsSL https://tailscale.com/install.sh | sh
sudo tailscale up
```

### Step 2: Build and publish the application

On your development machine:

```bash
cd Homespun
dotnet publish src/Homespun -c Release -o ./publish
```

Copy the `publish` folder to your VM:

```bash
scp -r ./publish user@your-vm:/tmp/homespun-publish
```

### Step 3: Run the installation script

On your VM:

```bash
# Copy the install scripts
sudo mkdir -p /opt/homespun
sudo cp -r /tmp/homespun-publish/* /opt/homespun/

# Copy and run the install script
cd /path/to/Homespun/install/vm
sudo ./install.sh
```

The install script will:
- Create a `homespun` system user
- Configure a systemd service
- Set up nginx as a reverse proxy
- Bind to your Tailscale IP automatically

### Step 4: Configure and start

Set your GitHub token:

```bash
sudo systemctl edit homespun
```

Add the following (between the comment lines):

```ini
[Service]
Environment=GITHUB_TOKEN=ghp_your_token_here
```

Start the service:

```bash
sudo ./run.sh
```

### Step 5: Access Homespun

Get your Tailscale IP:

```bash
tailscale ip -4
```

Access Homespun at `http://<tailscale-ip>` from any device on your Tailscale network.

### Installation options

The install script accepts several options:

| Option | Description | Default |
|--------|-------------|---------|
| `--app-dir DIR` | Application directory | `/opt/homespun` |
| `--data-dir DIR` | Data directory | `/var/lib/homespun` |
| `--user USER` | Service user | `homespun` |
| `--port PORT` | Internal Kestrel port | `5000` |

Example:

```bash
sudo ./install.sh --app-dir /home/deploy/homespun --port 8080
```

## Container deployment

Run Homespun as a Docker container with persistent storage.

### Step 1: Build the image

From the repository root:

```bash
docker build -t homespun:local .
```

### Step 2: Run the container

**Windows (Automated - Recommended):**

Use the PowerShell script which automatically configures everything:

```powershell
.\scripts\run.ps1
```

This script will:
- Read GitHub token from .NET user secrets
- Mount `~/.homespun-container/data` for persistent storage
- Mount SSH keys for git operations
- Run in interactive mode on port 8080

**Linux/macOS (Manual):**

```bash
docker run --rm -it \
  --name homespun-local \
  -p 8080:8080 \
  -v ~/.homespun-container/data:/data \
  -v ~/.ssh:/home/homespun/.ssh:ro \
  -e GITHUB_TOKEN=ghp_your_token_here \
  -e ASPNETCORE_ENVIRONMENT=Development \
  homespun:local
```

**Production (Detached mode):**

```bash
docker run -d \
  --name homespun \
  -p 8080:8080 \
  -v homespun-data:/data \
  -e GITHUB_TOKEN=ghp_your_token_here \
  -e ASPNETCORE_ENVIRONMENT=Production \
  --restart unless-stopped \
  homespun:local
```

**Production with pre-built image from GHCR:**

Pre-built images are published to GitHub Container Registry on each release:

```bash
docker run -d \
  --name homespun \
  -p 8080:8080 \
  -v homespun-data:/data \
  -e GITHUB_TOKEN=ghp_your_token_here \
  -e ASPNETCORE_ENVIRONMENT=Production \
  --restart unless-stopped \
  ghcr.io/nick-boey/homespun:latest
```

### Step 3: Verify the deployment

**Interactive mode (Windows script):**
- The application will start and display logs
- Open http://localhost:8080 in your browser
- Press Ctrl+C to stop

**Detached mode:**

```bash
# Check container status
docker ps

# View logs
docker logs -f homespun

# Test health endpoint
curl http://localhost:8080/health
```

### Docker Compose (recommended)

Create a `docker-compose.yml`:

```yaml
version: '3.8'

services:
  homespun:
    build:
      context: .
      dockerfile: Dockerfile
    container_name: homespun
    ports:
      - "8080:8080"
    volumes:
      - homespun-data:/data
    environment:
      - GITHUB_TOKEN=${GITHUB_TOKEN}
      - ASPNETCORE_ENVIRONMENT=Production
    restart: unless-stopped
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/health"]
      interval: 30s
      timeout: 10s
      retries: 3

volumes:
  homespun-data:
```

Run with:

```bash
GITHUB_TOKEN=ghp_your_token docker-compose up -d
```

### Automatic updates with Watchtower

For production deployments using the pre-built GHCR image, use [Watchtower](https://containrrr.dev/watchtower/) to automatically update when new releases are published:

```yaml
version: '3.8'

services:
  homespun:
    image: ghcr.io/nick-boey/homespun:latest
    container_name: homespun
    ports:
      - "8080:8080"
    volumes:
      - homespun-data:/data
    environment:
      - GITHUB_TOKEN=${GITHUB_TOKEN}
      - ASPNETCORE_ENVIRONMENT=Production
    restart: unless-stopped

  watchtower:
    image: containrrr/watchtower
    container_name: watchtower
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock
    environment:
      - WATCHTOWER_CLEANUP=true
      - WATCHTOWER_POLL_INTERVAL=300
    restart: unless-stopped
    command: homespun

volumes:
  homespun-data:
```

See the [main README](../README.md#deployment) for detailed Watchtower configuration options.

### Tailscale integration for containers

To make your container accessible via Tailscale:

**Option A: Host network mode (simplest)**

```bash
docker run -d \
  --name homespun \
  --network host \
  -v homespun-data:/data \
  -e GITHUB_TOKEN=ghp_your_token_here \
  -e ASPNETCORE_URLS=http://$(tailscale ip -4):8080 \
  homespun:latest
```

**Option B: Tailscale sidecar**

Use a Tailscale container as a sidecar. See [Tailscale Docker documentation](https://tailscale.com/kb/1282/docker).

## Post-deployment configuration

### Environment variables

| Variable | Description | Required |
|----------|-------------|----------|
| `GITHUB_TOKEN` | GitHub personal access token | Yes |
| `HOMESPUN_DATA_PATH` | Path to data file | No (default: `~/.homespun/homespun-data.json`) |
| `ASPNETCORE_ENVIRONMENT` | Environment name | No (default: `Production`) |
| `ASPNETCORE_URLS` | URLs to bind to | No (default: `http://+:8080`) |

### Health checks

All deployments expose a health check endpoint:

```bash
curl http://<host>/health
```

Response: `Healthy` with HTTP 200 indicates the application is running correctly.

### Persistent data

Homespun stores data in the `.homespun` folder. Ensure this is persisted:

| Deployment | Persistence method |
|------------|-------------------|
| VM | `/var/lib/homespun/.homespun` (default) |
| Container | Volume mount to `/data` |

## Troubleshooting

### VM deployment

**Service fails to start:**

```bash
# Check service status
sudo systemctl status homespun

# View detailed logs
sudo journalctl -u homespun -n 100 --no-pager

# Common issues:
# - Missing .NET runtime: Install dotnet-runtime-10.0
# - Tailscale not connected: Run 'tailscale status'
# - Permission issues: Check /var/lib/homespun ownership
```

**Nginx errors:**

```bash
# Test configuration
sudo nginx -t

# View nginx logs
sudo tail -f /var/log/nginx/homespun_error.log
```

### Container deployment

**Container exits immediately:**

```bash
# View logs
docker logs homespun

# Run interactively to debug
docker run -it --rm homespun:latest /bin/bash
```

**Health check fails:**

```bash
# Check if app is listening
docker exec homespun curl localhost:8080/health
```

### General issues

**SignalR/WebSocket connection failures:**

- Ensure your reverse proxy (nginx) is configured for WebSocket upgrade
- Check that `/hubs/` paths have extended timeouts

**GitHub sync not working:**

- Verify `GITHUB_TOKEN` is set correctly
- Ensure the token has `repo` scope
- Check token hasn't expired

**Data not persisting:**

- Verify volume mounts are correct
- Check directory permissions
- Ensure `HOMESPUN_DATA_PATH` points to a mounted volume
