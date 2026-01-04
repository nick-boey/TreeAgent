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
│   ├── Agents/                  # Claude Code agent management
│   │   ├── Components/Pages/    # Agent UI pages
│   │   ├── Data/                # Agent, AgentStatus, Message entities
│   │   ├── Hubs/                # SignalR hub for real-time updates
│   │   └── Services/            # AgentService, ClaudeCodeProcessManager, etc.
│   ├── Commands/                # Shell command execution
│   ├── Git/                     # Git worktree operations
│   ├── GitHub/                  # GitHub API integration (Octokit)
│   ├── Projects/                # Project management
│   ├── PullRequests/            # PR workflow and data entities
│   │   └── Data/                # Feature, Project, HomespunDbContext
│   │       └── Entities/        # EF Core entities
│   └── Roadmap/                 # Roadmap parsing and management
├── Components/                  # Shared Blazor components
│   ├── Layout/                  # Layout components
│   ├── Pages/                   # Page components
│   └── Shared/                  # Reusable components
├── HealthChecks/                # Health check implementations
├── Migrations/                  # EF Core migrations
└── Program.cs                   # Application entry point

tests/Homespun.Tests/
├── Features/                    # Tests organized by feature (mirrors src structure)
│   ├── Agents/                  # Agent unit tests, fixtures, and integration tests
│   ├── Git/                     # Git worktree tests
│   ├── GitHub/                  # GitHub service tests
│   ├── PullRequests/            # PR model and workflow tests
│   └── Roadmap/                 # Roadmap parser and service tests
└── Helpers/                     # Shared test utilities and fixtures
```

### Feature Slices

- **Agents**: Claude Code agent orchestration, process management, message streaming, system prompts
- **Commands**: Shell command execution abstraction
- **Git**: Git worktree creation, management, and rebase operations
- **GitHub**: GitHub PR synchronization and API operations using Octokit
- **Projects**: Project CRUD operations
- **PullRequests**: PR workflow, feature management, and core data entities (Feature, Project, DbContext)
- **Roadmap**: Roadmap file parsing and change tracking

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

### Database

- SQLite database with EF Core
- Migrations applied automatically on startup
- Database file: `homespun.db` (gitignored)

### Creating Migrations

```bash
cd src/Homespun
dotnet ef migrations add <MigrationName>
```

## Key Services

### Agents (Features/Agents/)
- **AgentService**: Agent lifecycle management with Claude Code process orchestration
- **AgentWorkflowService**: Orchestrates agent workflow from feature creation to completion
- **ClaudeCodeProcessManager**: Process pool management for Claude Code instances
- **ClaudeCodeProcess**: Individual Claude Code process wrapper
- **ClaudeCodePathResolver**: Platform-aware Claude Code executable discovery
- **MessageParser**: JSON message parser for Claude Code output
- **SystemPromptService**: Template processing for agent system prompts
- **AgentHub**: SignalR hub for real-time agent updates

### Commands (Features/Commands/)
- **CommandRunner**: Shell command execution with output capture

### Git (Features/Git/)
- **GitWorktreeService**: Git worktree creation, deletion, and rebase operations

### GitHub (Features/GitHub/)
- **GitHubService**: GitHub PR synchronization using Octokit
- **GitHubClientWrapper**: Octokit abstraction for testability

### Projects (Features/Projects/)
- **ProjectService**: CRUD operations for projects

### PullRequests (Features/PullRequests/)
- **FeatureService**: Feature management with tree structure and worktree integration
- **PullRequestWorkflowService**: PR creation and management workflow
- **HomespunDbContext**: EF Core database context

### Roadmap (Features/Roadmap/)
- **RoadmapService**: Roadmap file loading and future change calculations
- **RoadmapParser**: YAML roadmap file parsing

## Configuration

Environment variables:
- `HOMESPUN_DB_PATH`: Path to SQLite database (default: `homespun.db`)
- `GITHUB_TOKEN`: GitHub personal access token for PR operations
- `HOMESPUN_VERBOSE_SQL`: Set to `true` to enable detailed EF Core SQL logging

## Health Checks

The application exposes a health check endpoint at `/health` that monitors:
- Database connectivity
- Process manager status

## Real-time Updates

SignalR is used for real-time updates:
- Agent message streaming
- Agent status changes
- Connect to `/hubs/agent` for agent-related updates
