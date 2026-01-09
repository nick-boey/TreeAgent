# Homespun Dockerfile
# Multi-stage build for .NET 10 Blazor Server application
# Includes: git, gh CLI, and beads (bd) tools

# =============================================================================
# Stage 1: Build
# =============================================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files first for better layer caching
COPY Homespun.sln ./
COPY src/Homespun/Homespun.csproj src/Homespun/
COPY tests/Homespun.Tests/Homespun.Tests.csproj tests/Homespun.Tests/

# Restore dependencies
RUN dotnet restore

# Copy everything else
COPY . .

# Build and publish
RUN dotnet publish src/Homespun/Homespun.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# =============================================================================
# Stage 2: Runtime
# =============================================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Install dependencies: git, gh CLI, Node.js (for beads)
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

# Install Node.js (LTS) for beads
RUN curl -fsSL https://deb.nodesource.com/setup_lts.x | bash - \
    && apt-get install -y nodejs \
    && rm -rf /var/lib/apt/lists/*

# Install beads (bd) globally
RUN npm install -g @beads/bd

# Create non-root user for security
RUN useradd --create-home --shell /bin/bash homespun

# Create data directory
RUN mkdir -p /data/.homespun && chown -R homespun:homespun /data

# Copy published application
COPY --from=build /app/publish .

# Set ownership
RUN chown -R homespun:homespun /app

# Switch to non-root user
USER homespun

# Configure environment
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080
ENV HOMESPUN_DATA_PATH=/data/.homespun/homespun-data.json
ENV DOTNET_PRINT_TELEMETRY_MESSAGE=false

# Expose port
EXPOSE 8080

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

# Set entrypoint
ENTRYPOINT ["dotnet", "Homespun.dll"]
