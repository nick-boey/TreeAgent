# Homespun Docker Container

This directory contains Docker Compose configuration and scripts for running Homespun in a container with automatic updates via Watchtower.

**Note:** The `Dockerfile` and `.dockerignore` files are located at the repository root, not in this directory.

## Prerequisites

- Docker installed on your system
- Docker Compose V2 (included with Docker Desktop, or install separately)
- PowerShell 7.0+ (for the Windows script only)

## Quick Start (Linux/VM - Recommended)

The easiest way to run Homespun on a Linux VM with automatic updates:

### Step 1: Configure credentials

For one-time or temporary use, export environment variables:

```bash
export GITHUB_TOKEN="ghp_your_token_here"
```

For persistent VM deployments, add credentials to `/etc/environment` so they persist across reboots and are available to all users:

```bash
# Edit /etc/environment (requires sudo)
sudo nano /etc/environment

# Add these lines:
HSP_GITHUB_TOKEN="ghp_your_token_here"
HSP_TAILSCALE_AUTH="tskey-auth-your_key_here"  # Optional, for Tailscale access
```

Then reload the environment:

```bash
source /etc/environment
```

**Credential priority:** The script checks for credentials in this order:
1. Command line arguments
2. `HSP_GITHUB_TOKEN` / `HSP_TAILSCALE_AUTH` environment variables
3. `GITHUB_TOKEN` / `TAILSCALE_AUTH_KEY` environment variables
4. `.env` file in the install/container directory

### Step 2: Run with the script

```bash
./install/container/run.sh
```

This will:
- Pull the latest pre-built image from GHCR (`ghcr.io/nick-boey/homespun:latest`)
- Start Homespun in detached mode on port 8080
- Start Watchtower to automatically update when new releases are published
- Create `~/.homespun-container/data` for persistent storage

The application will be available at `http://localhost:8080`

### Common commands

```bash
# View logs
./install/container/run.sh --logs

# Stop containers
./install/container/run.sh --stop

# Pull latest image and restart
./install/container/run.sh --pull

# Run interactively (foreground)
./install/container/run.sh -it
```

## Quick Start (Windows)

For Windows development, use the PowerShell script with a locally built image:

```powershell
# Build the image
docker build -t homespun:local .

# Run interactively
.\install\container\run.ps1
```

## Development Mode

For local development without Watchtower, use the `--local` flag:

```bash
# Make scripts executable (first time only)
chmod +x ./install/container/run.sh ./install/container/test.sh

# Build the local image first
docker build -t homespun:local .

# Run with local image (no Watchtower)
./install/container/run.sh --local

# Run interactively for debugging
./install/container/run.sh --local -it
```

### Quick Test Script

For quick build-and-run testing, use the test script which combines both steps:

```bash
# Build and run interactively in one command
./install/container/test.sh

# Run without rebuilding (if image already built)
./install/container/test.sh --no-build
```

The test script:
- Builds the container as `homespun:local`
- Runs in interactive mode (foreground)
- Disables Watchtower (local development mode)
- Uses `HSP_GITHUB_TOKEN` and `HSP_TAILSCALE_AUTH` environment variables

## Pre-built Container Images

Pre-built container images are automatically published to GitHub Container Registry (GHCR) when new releases are created:

```bash
docker pull ghcr.io/nick-boey/homespun:latest
```

Available tags:
- `latest` - Most recent release
- `x.y.z` - Specific version (e.g., `1.0.0`)
- `x.y` - Latest patch of a minor version (e.g., `1.0`)

## Tailscale Integration

To run Homespun on a VM and access it securely via Tailscale:

1.  **Generate a Tailscale Auth Key:**
    - Go to [Tailscale Admin Console](https://login.tailscale.com/admin/settings/keys)
    - Generate a **reusable** auth key (recommended - allows container restarts without generating new keys)
    - Ensure the key has tags if you use ACLs (e.g., `tag:homespun`)

2.  **Run with Tailscale:**

    ```bash
    export GITHUB_TOKEN="ghp_..."
    ./install/container/run.sh --tailscale-auth-key "tskey-auth-..." --tailscale-hostname "homespun-vm"
    ```

    Or add to your `.env` file:
    ```bash
    TAILSCALE_AUTH_KEY=tskey-auth-your_key_here
    TAILSCALE_HOSTNAME=homespun-vm
    ```

3.  **Access the Application:**
    - The application will join your Tailscale network
    - It uses `tailscale serve` to expose the application on port 80 of its Tailscale IP
    - Access it from another device on your tailnet via: `http://homespun-vm` (or the MagicDNS name)

## Manual Container Operations

### Building the Container

To build the container image locally, run the following command from the **repository root directory**:

```bash
docker build -t homespun:local .
```

This will:
- Use the multi-stage build process to compile the application
- Install required dependencies (git, gh CLI, beads, OpenCode)
- Create a production-ready container image

**Note:** The build process installs OpenCode for AI agent functionality. This may take a few minutes on first build.

## Running the Container Manually

### Basic Run

```bash
docker run -p 8080:8080 homespun:local
```

The application will be available at `http://localhost:8080`

### Run with Bind Mount (Recommended for Development)

To persist data to a local directory you can easily access:

**Windows (PowerShell):**
```powershell
docker run --rm -it `
  --name homespun-local `
  -p 8080:8080 `
  -v "$HOME/.homespun-container/data:/data" `
  -v "$HOME/.ssh:/home/homespun/.ssh:ro" `
  -e GITHUB_TOKEN=your_token_here `
  -e ASPNETCORE_ENVIRONMENT=Development `
  homespun:local
```

**Linux/macOS:**
```bash
docker run --rm -it \
  --user "$(id -u):$(id -g)" \
  --name homespun-local \
  -p 8080:8080 \
  -v ~/.homespun-container/data:/data \
  -v ~/.homespun-container/data:/home/containeruser \
  -v ~/.ssh:/home/containeruser/.ssh:ro \
  -e HOME=/home/containeruser \
  -e GITHUB_TOKEN=your_token_here \
  -e ASPNETCORE_ENVIRONMENT=Development \
  homespun:local
```

**About Bind Mounts vs Docker Volumes:**
- **Bind mounts** (`C:\Users\you\.homespun-container\data`) - Easy to access and inspect from Windows
- **Docker volumes** (`homespun-data`) - Stored inside WSL2 VM, harder to access but better performance

For local development, bind mounts are recommended as you can easily view and edit files.

### Run with Docker Volume

To use a Docker-managed volume instead:

```bash
docker run -p 8080:8080 \
  -v homespun-data:/data \
  -e GITHUB_TOKEN=your_github_token_here \
  -e ASPNETCORE_ENVIRONMENT=Development \
  homespun:local
```

**Note:** Docker volumes on Windows are stored in the WSL2 VM at `\\wsl$\docker-desktop-data\data\docker\volumes\`. They're harder to access but may have better performance.

## Testing the Container

### Verify Build

Check that the image was built successfully:

```bash
docker images | grep homespun
```

### Test Health Check

Once the container is running, verify the health check endpoint:

```bash
curl http://localhost:8080/health
```

### Interactive Shell

To explore the container or debug issues:

```bash
docker run -it --entrypoint /bin/bash homespun:local
```

## Docker Compose Configuration

This directory includes a `docker-compose.yml` that configures:
- **Homespun**: The main application with health checks
- **Watchtower**: Automatic updates when new releases are published (production profile only)

The `run.sh` script handles all the configuration automatically. For advanced customization, you can modify `docker-compose.yml` directly.

### Watchtower behavior

When running in production mode (default), Watchtower will:
- Check for new images every 5 minutes (300 seconds)
- Automatically pull and restart the container when a new release is published
- Clean up old images to save disk space
- Only monitor the `homespun` container (not other containers on the system)

### Customizing update schedule

Edit `docker-compose.yml` to change `WATCHTOWER_POLL_INTERVAL` (in seconds):

```yaml
environment:
  - WATCHTOWER_POLL_INTERVAL=3600  # Check hourly
```

Or use a cron schedule:

```yaml
environment:
  - WATCHTOWER_SCHEDULE=0 0 4 * * *  # Check at 4 AM daily
```

## Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `ASPNETCORE_URLS` | URL bindings | `http://+:8080` |
| `ASPNETCORE_ENVIRONMENT` | Environment (Development/Production) | `Production` |
| `HOMESPUN_DATA_PATH` | Path to data file | `/data/homespun-data.json` |
| `GITHUB_TOKEN` | GitHub personal access token | (none) |
| `TAILSCALE_AUTH_KEY` | Tailscale auth key (optional) | (none) |
| `TAILSCALE_HOSTNAME` | Hostname for Tailscale | `homespun-container` |

## Volumes and Filesystem

### Container Paths

| Container Path | Purpose |
|----------------|---------|
| `/data` | Persistent data storage (database, configuration, Tailscale state) |
| `/app` | Application binaries (Homespun.dll) |
| `/home/containeruser` | Home directory (mapped to /data for correct file ownership) |
| `/home/containeruser/.ssh` | SSH keys (mounted from host, read-only) |

**Note:** When using the run scripts, the container runs as your host user (matching UID/GID) so that files created in mounted volumes have correct ownership. This allows you to run `git` commands on cloned repositories from the host without "dubious ownership" errors.

### Local Development (Bind Mounts)

When using the PowerShell script or bind mounts, your data is stored at:

**Windows:** `C:\Users\<username>\.homespun-container\data\`

You can access this directory directly from Windows Explorer to:
- View the JSON data file
- Inspect configuration files
- Backup data
- Debug issues

### Docker Volumes

If using Docker-managed volumes, they're stored inside the WSL2 VM:

**Location:** `\\wsl$\docker-desktop-data\data\docker\volumes\`

**Access via WSL:**
```bash
wsl -d docker-desktop
cd /var/lib/docker/volumes/homespun-data/_data
```

**Copy files out:**
```bash
docker run --rm -v homespun-data:/data -v C:/temp:/backup alpine cp -r /data /backup
```

## Troubleshooting

### Container exits immediately

Check the logs:
```bash
docker logs homespun-local
```

### Permission issues with mounted volumes

The run scripts automatically set proper permissions (`chmod 777`) on the data directory. If you're running Docker directly, ensure the mounted directory is writable by the container user.

**Git "dubious ownership" errors:**
If you see `fatal: detected dubious ownership in repository`, this means the repository was created by a different user. The run scripts solve this by running the container as your host user. If running Docker directly, use `--user "$(id -u):$(id -g)"`.

### Tailscale authentication fails

**"invalid key" error:**
- If you used a one-time auth key, it can only be used once. Generate a new **reusable** key from the [Tailscale Admin Console](https://login.tailscale.com/admin/settings/keys).
- If you previously ran the container with `sudo`, the Tailscale state may be corrupted. Clear it:
  ```bash
  rm -rf ~/.homespun-container/data/tailscale
  ```
- Remove any stale devices from the [Tailscale Machines page](https://login.tailscale.com/admin/machines).

### Cannot connect to GitHub

Verify your GitHub token is set:
```bash
docker exec homespun-local env | grep GITHUB_TOKEN
```

**On Linux/macOS (bash script):**
Set the environment variable before running:
```bash
export GITHUB_TOKEN="ghp_your_token_here"
./install/container/run.sh
```

**On Windows (PowerShell script):**
The script reads from .NET user secrets. Configure with:
```powershell
dotnet user-secrets set "GitHub:Token" "your_token_here" --project src/Homespun
```

To verify your secrets:
```powershell
dotnet user-secrets list --project src/Homespun
```

### Data file issues

**With bind mount:**
Delete the data directory:
```powershell
Remove-Item -Recurse -Force ~/.homespun-container/data
```

**With Docker volume:**
Remove the volume to start fresh:
```bash
docker volume rm homespun-data
```

### PowerShell script fails - "Image not found"

Build the image first:
```powershell
docker build -t homespun:local .
```

### PowerShell script fails - "Docker is not running"

Start Docker Desktop and wait for it to fully initialize, then try again.

### DNS resolution errors during build

Add DNS servers to Docker Desktop settings:
1. Open Docker Desktop → Settings → Docker Engine
2. Add to the JSON configuration:
   ```json
   {
     "dns": ["8.8.8.8", "8.8.4.4"]
   }
   ```
3. Click Apply & Restart

## Building for Production

For production deployments, consider:

1. Using a specific tag instead of `latest`:
   ```bash
   docker build -t homespun:v1.0.0 .
   ```

2. Publishing to a container registry:
   ```bash
   docker tag homespun:v1.0.0 your-registry/homespun:v1.0.0
   docker push your-registry/homespun:v1.0.0
   ```

3. Using secrets management instead of environment variables for sensitive data

## Notes

- When using the run scripts, the container runs as your host user (matching UID/GID) for correct file ownership
- When running Docker directly, specify `--user "$(id -u):$(id -g)"` to avoid permission issues
- Node.js and beads (bd) are pre-installed for git workflow management
- OpenCode is pre-installed for AI agent orchestration
- The GitHub CLI (gh) is available for PR operations
- Tailscale state is persisted in `/data/tailscale` for consistent device identity across restarts
- Data Protection keys are persisted to prevent antiforgery token errors across container restarts
- Health checks run every 30 seconds on the `/health` endpoint
