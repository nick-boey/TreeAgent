# Development Instructions

This file contains instructions for Claude Code when working on this project.

## Overview

Homespun is a Blazor web application for managing development features and AI agents. It provides:
- Project and feature management with hierarchical tree visualization
- Git worktree integration for isolated feature development
- GitHub PR synchronization
- Claude Code agent orchestration with real-time message streaming
- Customizable system prompts with template variables

## Development Practices

### Test Driven Development (TDD)

Use Test Driven Development practices where possible:

1. **Write tests first** - Before implementing a feature, write failing tests that define the expected behavior
2. **Red-Green-Refactor** - Follow the TDD cycle:
   - Red: Write a failing test
   - Green: Write minimal code to make the test pass
   - Refactor: Clean up the code while keeping tests green
3. **Test naming** - Use descriptive test names that explain the scenario and expected outcome
4. **Test coverage** - Aim for comprehensive coverage of business logic in services

### Project Structure (Vertical Slice Architecture)

The project follows Vertical Slice Architecture, organizing code by feature rather than technical layer. Each feature contains its own services, data models, and components.

```
src/Homespun/
├── Features/                    # Feature slices (all business logic)
│   ├── Beads/                   # Beads issue tracking integration
│   │   ├── Data/                # BeadsIssue, BeadsIssueMetadata entities
│   │   └── Services/            # BeadsDatabaseService, BeadsQueueService
│   ├── ClaudeCode/              # Claude Code SDK session management
│   │   ├── Components/Pages/    # Session.razor chat UI
│   │   ├── Data/                # ClaudeSession, ClaudeMessage, SessionMode
│   │   ├── Hubs/                # ClaudeCodeHub for real-time streaming
│   │   └── Services/            # ClaudeSessionService, SessionOptionsFactory
│   ├── Commands/                # Shell command execution
│   ├── Git/                     # Git worktree operations
│   ├── GitHub/                  # GitHub API integration (Octokit)
│   ├── Projects/                # Project management
│   ├── PullRequests/            # PR workflow and data entities
│   │   └── Data/                # Feature, Project, HomespunDbContext
│   │       └── Entities/        # EF Core entities
│   └── Notifications/           # Toast notifications via SignalR
├── Components/                  # Shared Blazor components
│   ├── Layout/                  # Layout components
│   ├── Pages/                   # Page components
│   └── Shared/                  # Reusable components
├── HealthChecks/                # Health check implementations
└── Program.cs                   # Application entry point

tests/Homespun.Tests/
├── Features/                    # Tests organized by feature (mirrors src structure)
│   ├── Beads/                   # Beads service tests
│   ├── ClaudeCode/              # ClaudeCode service and hub tests
│   ├── Git/                     # Git worktree tests
│   ├── GitHub/                  # GitHub service tests
│   └── PullRequests/            # PR model and workflow tests
└── Helpers/                     # Shared test utilities and fixtures
```

### Feature Slices

- **Beads**: Integration with beads issue tracking system - direct SQLite database access for fast reads, queued writes
- **ClaudeCode**: Claude Code SDK session management using ClaudeAgentSdk NuGet package - supports Plan (read-only) and Build (full access) modes
- **Commands**: Shell command execution abstraction
- **Git**: Git worktree creation, management, and rebase operations
- **GitHub**: GitHub PR synchronization and API operations using Octokit
- **Notifications**: Real-time toast notifications via SignalR
- **Projects**: Project CRUD operations
- **PullRequests**: PR workflow, feature management, and core data entities (Feature, Project, PullRequest)

### Running the Application

```bash
cd src/Homespun
dotnet run
```

The application will be available at `https://localhost:5001` (or the configured port).

### Running Tests

```bash
dotnet test
```

### Data Storage

- JSON file storage
- Data file: `homespun-data.json` (stored in `.homespun` directory)

### Creating Migrations

*Not applicable for JSON storage.*

## Key Services

### Beads (Features/Beads/)
- **BeadsDatabaseService**: Direct SQLite access for fast issue reads and writes
- **BeadsQueueService**: Queued async database operations
- **BeadsService**: CLI-based beads integration (fallback)
- **BeadsIssueTransitionService**: Issue status transitions

### ClaudeCode (Features/ClaudeCode/)
- **ClaudeSessionService**: Session lifecycle management using ClaudeAgentSdk
- **ClaudeSessionStore**: In-memory storage for active sessions
- **SessionOptionsFactory**: Creates ClaudeAgentOptions based on session mode (Plan/Build)
- **AgentStartupTracker**: Tracks session startup state for UI feedback
- **ClaudeCodeHub**: SignalR hub for real-time message streaming

### Commands (Features/Commands/)
- **CommandRunner**: Shell command execution with output capture

### Git (Features/Git/)
- **GitWorktreeService**: Git worktree creation, deletion, and rebase operations

### GitHub (Features/GitHub/)
- **GitHubService**: GitHub PR synchronization using Octokit
- **GitHubClientWrapper**: Octokit abstraction for testability
- **IssuePrLinkingService**: Links beads issues to GitHub PRs

### Notifications (Features/Notifications/)
- **NotificationService**: Toast notification management
- **NotificationHub**: SignalR hub for notification delivery

### Projects (Features/Projects/)
- **ProjectService**: CRUD operations for projects

### PullRequests (Features/PullRequests/)
- **PullRequestDataService**: PR CRUD operations
- **PullRequestWorkflowService**: PR creation and management workflow

## Configuration

### Environment Variables

- `HOMESPUN_DATA_PATH`: Path to data file (default: `~/.homespun/homespun-data.json`)
- `GITHUB_TOKEN`: GitHub personal access token for PR operations
- `CLAUDE_CODE_OAUTH_TOKEN`: Claude Code OAuth token for API authentication
- `TAILSCALE_AUTH_KEY`: Tailscale auth key for VPN access (optional)

### Docker Deployment

The recommended way to provide credentials for Docker deployment is to create a credentials file at `~/.homespun/env`:

```bash
export GITHUB_TOKEN=ghp_your_token_here
export CLAUDE_CODE_OAUTH_TOKEN=your_oauth_token_here
export TAILSCALE_AUTH_KEY=tskey-auth-your_key_here  # Optional, for VPN access
```

The `scripts/run.sh` script will automatically source this file when starting the container.

Alternative methods (checked in order):
1. `~/.homespun/env` file (recommended)
2. `HSP_*` prefixed environment variables (for VM secrets)
3. Standard environment variables (`GITHUB_TOKEN`, `CLAUDE_CODE_OAUTH_TOKEN`, `TAILSCALE_AUTH_KEY`)
4. `.env` file in the repository root

## Health Checks

The application exposes a health check endpoint at `/health` that monitors:
- Data store accessibility
- Process manager status

## Real-time Updates

SignalR is used for real-time updates:
- `/hubs/claudecode` - Claude Code session message streaming, content blocks, session status
- `/hubs/notifications` - Toast notifications for system events
