# Development Instructions

This file contains instructions for Claude Code when working on this project.

## Overview

TreeAgent is a Blazor web application for managing development features and AI agents. It provides:
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

### Project Structure

```
src/TreeAgent.Web/
├── Components/           # Blazor components
│   ├── Layout/          # Layout components
│   ├── Pages/           # Page components (routed)
│   └── Shared/          # Shared/reusable components
├── Data/
│   └── Entities/        # EF Core entity classes
├── HealthChecks/        # Health check implementations
├── Hubs/                # SignalR hubs
├── Migrations/          # EF Core migrations
├── Services/            # Business logic services
└── Program.cs           # Application entry point

tests/TreeAgent.Web.Tests/
├── Services/            # Service unit tests
└── ...
```

### Running the Application

```bash
cd src/TreeAgent.Web
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
- Database file: `treeagent.db` (gitignored)

### Creating Migrations

```bash
cd src/TreeAgent.Web
dotnet ef migrations add <MigrationName>
```

## Key Services

- **ProjectService**: CRUD operations for projects
- **FeatureService**: Feature management with tree structure and worktree integration
- **AgentService**: Agent lifecycle management with Claude Code process orchestration
- **GitHubService**: GitHub PR synchronization using Octokit
- **GitWorktreeService**: Git worktree operations
- **SystemPromptService**: Template processing for agent system prompts

## Configuration

Environment variables:
- `TREEAGENT_DB_PATH`: Path to SQLite database (default: `treeagent.db`)
- `GITHUB_TOKEN`: GitHub personal access token for PR operations

## Health Checks

The application exposes a health check endpoint at `/health` that monitors:
- Database connectivity
- Process manager status

## Real-time Updates

SignalR is used for real-time updates:
- Agent message streaming
- Agent status changes
- Connect to `/hubs/agent` for agent-related updates
