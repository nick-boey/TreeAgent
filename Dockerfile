# Homespun Dockerfile
# Multi-stage build for .NET 10 Blazor Server application
# Includes: git, gh CLI, and Fleece issue tracking tools
#
# Environment Variables (passed at runtime via scripts/run.sh):
#   GITHUB_TOKEN              - GitHub personal access token for PR operations
#   CLAUDE_CODE_OAUTH_TOKEN   - Claude Code OAuth token for authentication
#   TAILSCALE_AUTH_KEY        - Tailscale auth key for VPN access (optional)

# =============================================================================
# Stage 1: Build
# =============================================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files first for better layer caching
COPY Homespun.sln ./
COPY src/Homespun/Homespun.csproj src/Homespun/
COPY tests/Homespun.Tests/Homespun.Tests.csproj tests/Homespun.Tests/
COPY tests/Homespun.Api.Tests/Homespun.Api.Tests.csproj tests/Homespun.Api.Tests/
COPY tests/Homespun.E2E.Tests/Homespun.E2E.Tests.csproj tests/Homespun.E2E.Tests/

# Restore dependencies
RUN dotnet restore

# Cache-busting build argument
ARG CACHEBUST=1
ARG VERSION=1.0.0
ARG BUILD_CONFIGURATION=Release

# Copy everything else
COPY . .

# Build and publish
# Note: Cannot use --no-restore here because Blazor framework files
# (blazor.web.js, etc.) are in an implicit package that's only resolved during publish
RUN dotnet publish src/Homespun/Homespun.csproj \
    -c $BUILD_CONFIGURATION \
    /p:Version=$VERSION \
    -o /app/publish

# =============================================================================
# Stage 2: Runtime
# =============================================================================
# Use SDK image instead of aspnet runtime to support Claude Code agents
# that need to run dotnet build, test, and other SDK commands via Bash tool
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS runtime
WORKDIR /app

# Install dependencies: git, gh CLI, Node.js (for npm packages)
RUN apt-get update && apt-get install -y --no-install-recommends \
    git \
    curl \
    ca-certificates \
    gnupg \
    && rm -rf /var/lib/apt/lists/*

# Install GitHub CLI
RUN curl -fsSL https://cli.github.com/packages/githubcli-archive-keyring.gpg | dd of=/usr/share/keyrings/githubcli-archive-keyring.gpg \
    && chmod go+r /usr/share/keyrings/githubcli-archive-keyring.gpg \
    && echo "deb [arch=$(dpkg --print-architecture) signed-by=/usr/share/keyrings/githubcli-archive-keyring.gpg] https://cli.github.com/packages stable main" | tee /etc/apt/sources.list.d/github-cli.list > /dev/null \
    && apt-get update \
    && apt-get install -y gh \
    && rm -rf /var/lib/apt/lists/*

# Install Node.js (LTS) for npm packages
RUN curl -fsSL https://deb.nodesource.com/setup_lts.x | bash - \
    && apt-get install -y nodejs \
    && rm -rf /var/lib/apt/lists/*

# Install build dependencies for native npm packages (node-pty requires compilation)
RUN apt-get update && apt-get install -y --no-install-recommends \
    build-essential \
    python3-setuptools \
    && rm -rf /var/lib/apt/lists/*

# Install OpenCode, Claude Code, and Claude Code UI (cloudcli) globally
RUN npm install -g opencode-ai@latest @anthropic-ai/claude-code @siteboon/claude-code-ui

# Install Playwright MCP and Chromium browser with all dependencies
# --with-deps automatically installs system libraries (libatk, libcups, libdrm, etc.)
RUN npm install -g @playwright/mcp@latest \
    && npx playwright install chromium --with-deps

# Install Fleece CLI for issue tracking
# Install as root, then make tools accessible to all users
RUN dotnet tool install Fleece.Cli -g \
    && chmod 755 /root \
    && chmod -R 755 /root/.dotnet

# Clean up build dependencies to reduce image size
RUN apt-get update && apt-get remove -y build-essential && apt-get autoremove -y \
    && rm -rf /var/lib/apt/lists/*

# Install Tailscale for VPN access
RUN curl -fsSL https://pkgs.tailscale.com/stable/debian/bookworm.noarmor.gpg | tee /usr/share/keyrings/tailscale-archive-keyring.gpg >/dev/null \
    && curl -fsSL https://pkgs.tailscale.com/stable/debian/bookworm.tailscale-keyring.list | tee /etc/apt/sources.list.d/tailscale.list \
    && apt-get update \
    && apt-get install -y tailscale \
    && rm -rf /var/lib/apt/lists/*

# Create non-root user for security
RUN useradd --create-home --shell /bin/bash homespun

# Create data directory
RUN mkdir -p /data \
    && chown -R homespun:homespun /data

# Make home directory accessible to any runtime user
# This is needed because docker-compose may override the runtime user (HOST_UID/HOST_GID)
# for proper file ownership on mounted volumes, but HOME still points to /home/homespun
# Also create .claude directory structure for Claude Code runtime data (todos, debug, sessions)
RUN chmod 777 /home/homespun \
    && mkdir -p /home/homespun/.local/share /home/homespun/.config /home/homespun/.cache \
    && mkdir -p /home/homespun/.claude/todos /home/homespun/.claude/debug /home/homespun/.claude/projects /home/homespun/.claude/statsig \
    && chmod -R 777 /home/homespun/.local /home/homespun/.config /home/homespun/.cache /home/homespun/.claude

# Configure Playwright MCP for Claude Code agents (headless mode for container)
RUN echo '{"mcpServers":{"playwright":{"command":"npx","args":["@playwright/mcp@latest","--headless"]}}}' \
    > /home/homespun/.claude/settings.json

# Configure git to trust mounted directories (avoids "dubious ownership" errors)
RUN git config --global --add safe.directory '*'

# Copy published application
COPY --from=build /app/publish .

# Copy start script
COPY src/Homespun/start.sh .
RUN chmod +x start.sh

# Set ownership
RUN chown -R homespun:homespun /app

# Switch to non-root user
USER homespun

# Configure environment
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080
ENV HOMESPUN_DATA_PATH=/data/homespun-data.json
ENV DOTNET_PRINT_TELEMETRY_MESSAGE=false
ENV PATH="${PATH}:/root/.dotnet/tools"
ENV SignalR__InternalBaseUrl=http://localhost:8080

# Expose port
EXPOSE 8080

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

# Set entrypoint
ENTRYPOINT ["./start.sh"]
