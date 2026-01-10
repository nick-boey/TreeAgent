# Homespun Docker Container

This directory contains documentation and scripts for building and running Homespun in a container.

**Note:** The `Dockerfile` and `.dockerignore` files are located at the repository root, not in this directory.

## Prerequisites

- Docker installed on your system
- Docker Compose (optional, for easier configuration)
- PowerShell 7.0+ (for the automated run script on Windows)

## Quick Start (Windows)

### Step 1: Build the container

From the repository root:

```powershell
docker build -t homespun:local .
```

### Step 2: Run with the automated script

```powershell
.\install\container\run.ps1
```

This script will:
- ✅ Validate Docker is running and the image exists
- ✅ Read your GitHub token from .NET user secrets
- ✅ Create `~/.homespun-container/data` for persistent storage
- ✅ Mount your SSH keys for git operations
- ✅ Run the container in interactive mode on port 8080

The application will be available at `http://localhost:8080`

Press `Ctrl+C` to stop the container.

## Running on Azure Linux VM (with Tailscale)

To run Homespun on a cloud VM and access it securely via Tailscale:

1.  **Generate a Tailscale Auth Key:**
    - Go to [Tailscale Admin Console](https://login.tailscale.com/admin/settings/keys)
    - Generate an ephemeral key (reusable if you plan to spin up multiple instances, or single-use)
    - Ensure the key has tags if you use ACLs (e.g., `tag:homespun`)

2.  **Run with Tailscale:**

    **Using the PowerShell script (if PowerShell is installed):**
    ```powershell
    ./install/container/run.ps1 -TailscaleAuthKey "tskey-auth-..." -TailscaleHostname "homespun-azure"
    ```

    **Using the Bash script (Linux/macOS):**
    ```bash
    ./install/container/run.sh --tailscale-auth-key "tskey-auth-..." --tailscale-hostname "homespun-azure"
    ```

    **Using Docker directly:**
    ```bash
    docker run --rm -it \
      --name homespun-azure \
      -p 8080:8080 \
      -v ~/.homespun-container/data:/data \
      -e GITHUB_TOKEN=your_token \
      -e TAILSCALE_AUTH_KEY=tskey-auth-... \
      -e TAILSCALE_HOSTNAME=homespun-azure \
      homespun:local
    ```

3.  **Access the Application:**
    - The application will join your Tailscale network.
    - It uses `tailscale serve` to expose the application on port 80 of its Tailscale IP.
    - Access it from another device on your tailnet via: `http://homespun-azure` (or the MagicDNS name).

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
  --name homespun-local \
  -p 8080:8080 \
  -v ~/.homespun-container/data:/data \
  -v ~/.ssh:/home/homespun/.ssh:ro \
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

## Docker Compose (Alternative)

Create a `docker-compose.yml` file in the project root:

```yaml
version: '3.8'

services:
  homespun:
    build:
      context: .
      dockerfile: Dockerfile
    ports:
      - "8080:8080"
    volumes:
      - homespun-data:/data
      - ./repos:/repos
    environment:
      - GITHUB_TOKEN=${GITHUB_TOKEN}
      - ASPNETCORE_ENVIRONMENT=Production
    restart: unless-stopped

volumes:
  homespun-data:
```

Then run:

```bash
docker-compose up -d
```

## Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `ASPNETCORE_URLS` | URL bindings | `http://+:8080` |
| `ASPNETCORE_ENVIRONMENT` | Environment (Development/Production) | `Production` |
| `HOMESPUN_DATA_PATH` | Path to data file | `/data/.homespun/homespun-data.json` |
| `GITHUB_TOKEN` | GitHub personal access token | (none) |
| `TAILSCALE_AUTH_KEY` | Tailscale auth key (optional) | (none) |
| `TAILSCALE_HOSTNAME` | Hostname for Tailscale | `homespun-container` |

## Volumes and Filesystem

### Container Paths

| Container Path | Purpose |
|----------------|---------|
| `/data` | Persistent data storage (database, configuration) |
| `/app` | Application binaries (Homespun.dll) |
| `/home/homespun` | Home directory for the homespun user |
| `/home/homespun/.ssh` | SSH keys (mounted from host, read-only) |

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

Ensure the mounted directories have proper permissions. The container runs as user `homespun` (UID/GID created during build).

### Cannot connect to GitHub

Verify your GitHub token is set:
```bash
docker exec homespun-local env | grep GITHUB_TOKEN
```

If using the PowerShell script, ensure your .NET user secrets are configured:
```powershell
dotnet user-secrets list --project src/Homespun
```

To set the GitHub token:
```powershell
dotnet user-secrets set "GitHub:Token" "your_token_here" --project src/Homespun
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

- The container runs as a non-root user (`homespun`) for security
- Node.js and beads (bd) are pre-installed for git workflow management
- OpenCode is pre-installed for AI agent orchestration
- The GitHub CLI (gh) is available for PR operations
- Data Protection keys are persisted to prevent antiforgery token errors across container restarts
- Health checks run every 30 seconds on the `/health` endpoint
