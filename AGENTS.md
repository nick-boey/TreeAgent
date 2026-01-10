# Development Instructions

This file contains instructions for working on this project.

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

### UI Text Conventions

Use **sentence case** for all UI labels, headings, and button text throughout the application. Do not use Title Case.

**Examples:**
- "Branch name preview" (correct) vs "Branch Name Preview" (incorrect)
- "Additional labels" (correct) vs "Additional Labels" (incorrect)
- "Parent issue" (correct) vs "Parent Issue" (incorrect)

**Exceptions:**
- Proper nouns (e.g., "GitHub", "Claude")
- Acronyms (e.g., "ID", "API")

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
├── Features/                    # Tests organized by feature
│   ├── Agents/
│   │   ├── Services/            # Agent service unit tests
│   │   └── Integration/         # Agent integration tests
│   └── PullRequests/
│       └── Services/            # GitHub service tests
├── Integration/
│   └── Fixtures/                # Test fixtures
├── Models/                      # Model unit tests
└── Services/                    # Service tests (legacy location)
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

### Verifying DI Configuration

After adding or modifying service registrations, always run the application to verify dependency injection is configured correctly:

```bash
cd src/Homespun
dotnet run
```

Common DI issues to watch for:
- **Scoped service from Singleton**: A Singleton service cannot inject a Scoped service directly. Either make the dependent service Singleton (if stateless) or use `IServiceScopeFactory` to create scopes.
- **Missing registrations**: Ensure all interfaces have corresponding implementations registered in `Program.cs`.

The application validates DI at startup and will fail fast with a clear error message if there are configuration issues.

### Data Storage

- JSON file storage
- Data file: `homespun-data.json` (stored in `.homespun` directory)

### Creating Migrations

*Not applicable for JSON storage.*

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
- **ReviewPollingService**: Background service polling GitHub for PR reviews (60s interval)
- **PullRequestReviewInfo**: Review data models (ReviewSummary with approval/comment counts)

### Projects (Features/Projects/)
- **ProjectService**: CRUD operations for projects

### PullRequests (Features/PullRequests/)
- **FeatureService**: Feature management with tree structure and worktree integration
- **PullRequestWorkflowService**: PR creation and management workflow
- **HomespunDbContext**: EF Core database context

### Roadmap (Features/Roadmap/)
- **RoadmapService**: Roadmap file loading and future change calculations
- **RoadmapParser**: YAML roadmap file parsing
- **FutureChangeTransitionService**: Status transitions for roadmap changes (Pending → InProgress → AwaitingPR → Complete)

### OpenCode (Features/OpenCode/)
- **AgentWorkflowService**: End-to-end agent workflow orchestration
- **AgentCompletionMonitor**: Monitors agent completion and triggers status transitions
- **OpenCodeClient**: HTTP client for OpenCode server API
- **OpenCodeServerManager**: Manages OpenCode server lifecycle
- **OpenCodeConfigGenerator**: Generates OpenCode configuration files
- **AgentHub**: SignalR hub for real-time agent updates

## Configuration

### GitHub Authentication

The application looks for a GitHub personal access token in the following order:

1. **User secrets** (recommended for development): `GitHub:Token`
2. **Configuration/environment variable**: `GITHUB_TOKEN`
3. **Direct environment variable**: `GITHUB_TOKEN`

To set up user secrets for local development:

```bash
cd src/Homespun
dotnet user-secrets set "GitHub:Token" "ghp_your_token_here"
```

### Environment Variables

- `HOMESPUN_DATA_PATH`: Path to data file (default: `~/.homespun/homespun-data.json`)
- `GITHUB_TOKEN`: GitHub personal access token for PR operations (alternative to user secrets)

## Health Checks

The application exposes a health check endpoint at `/health` that monitors:
- Data store accessibility
- Process manager status

## Real-time Updates

SignalR is used for real-time updates:
- Agent message streaming
- Agent status changes
- Connect to `/hubs/agent` for agent-related updates

## Landing the Plane (Session Completion)

**When ending a work session**, you MUST complete ALL steps below. Work is NOT complete until `git push` succeeds.

**MANDATORY WORKFLOW:**

1. **File issues for remaining work** - Create issues for anything that needs follow-up
2. **Run quality gates** (if code changed) - Tests, linters, builds
3. **Update issue status** - Close finished work, update in-progress items
4. **PUSH TO REMOTE** - This is MANDATORY:
   ```bash
   git pull --rebase
   bd sync
   git push
   git status  # MUST show "up to date with origin"
   ```
5. **Clean up** - Clear stashes, prune remote branches
6. **Verify** - All changes committed AND pushed
7. **Hand off** - Provide context for next session

**CRITICAL RULES:**
- Work is NOT complete until `git push` succeeds
- NEVER stop before pushing - that leaves work stranded locally
- NEVER say "ready to push when you are" - YOU must push
- If push fails, resolve and retry until it succeeds
