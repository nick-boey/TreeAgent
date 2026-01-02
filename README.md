# TreeAgent

TreeAgent is a .NET application for managing software development workflows using Git, GitHub, and agentic AI tools like Claude Code. It models development as a tree where each node represents a feature or fix aligned with GitHub pull requests.

## Overview

TreeAgent provides a visual interface for planning and executing software development through AI agents. It manages:

- **Feature Tree**: Visualize past, present, and future pull requests as a tree structure
- **Multiple Projects**: Work on multiple repositories simultaneously
- **Git Worktrees**: Automatically manage worktrees for each feature branch
- **Claude Code Agents**: Spawn and manage headless Claude Code instances
- **Message Persistence**: Store all agent communications in SQLite

## Features

- Tree-based visualization of feature development
- Sync with GitHub pull requests (open, closed, merged)
- Plan future features before development begins
- Drill down into agent message streams
- Real-time updates via SignalR
- Custom system prompts with template variables
- Color-coded feature status:
  - Blue: Merged
  - Red: Cancelled
  - Yellow: In development
  - Green: Ready for review
  - Grey: Future/planned

## Technology Stack

- **.NET 8+**: Core runtime
- **Blazor Server**: Web frontend with SSR
- **SQLite + EF Core**: Persistence
- **SignalR**: Real-time updates
- **Claude Code CLI**: AI agent
- **Tailscale**: Optional secure remote access

## Getting Started

### Prerequisites

- .NET 8 SDK or later
- Git
- Claude Code CLI installed and configured
- GitHub personal access token (for PR synchronization)

### Installation

```bash
git clone https://github.com/your-org/TreeAgent.git
cd TreeAgent
dotnet restore
dotnet build
```

### Configuration

Set the following environment variables before running:

| Variable | Description | Default |
|----------|-------------|---------|
| `TREEAGENT_DB_PATH` | Path to SQLite database | `treeagent.db` |
| `GITHUB_TOKEN` | GitHub personal access token for PR operations | (required for GitHub sync) |
| `TREEAGENT_WORKTREE_ROOT` | Base directory for worktrees | (uses project path) |
| `CLAUDE_CODE_PATH` | Path to Claude Code CLI executable | (uses PATH) |

Example:
```bash
export GITHUB_TOKEN="ghp_your_token_here"
export TREEAGENT_DB_PATH="/data/treeagent.db"
```

### Running

```bash
dotnet run --project src/TreeAgent.Web
```

The application will be available at `https://localhost:5001` (or the configured port).

## Usage Guide

### Managing Projects

1. **Create a Project**: Navigate to the Projects page and click "Create Project"
2. **Configure Repository**: Enter the local Git repository path and optionally configure GitHub integration (owner/repo)
3. **Set Default Branch**: Specify the main branch name (e.g., `main` or `master`)

### Working with Features

1. **View Feature Tree**: Click on a project to see its feature tree
2. **Create a Feature**: Click "Add Feature" to create a new planned feature
   - Enter title, description, and branch name
   - Optionally set a parent feature to create hierarchical relationships
3. **Sync with GitHub**: Use the "Sync" button to import existing pull requests
4. **Start Development**: Click "Start" on a feature to:
   - Create a Git worktree automatically
   - Spawn a Claude Code agent
   - Begin development in isolation

### Managing Agents

1. **View Agents**: Navigate to the Agents dashboard to see all active agents
2. **Monitor Messages**: Click on an agent to view its message stream in real-time
3. **Stop Agents**: Use the "Stop" button to gracefully terminate an agent
4. **Custom Prompts**: Configure system prompts per feature or use project-level defaults

### System Prompts

TreeAgent supports customizable system prompts with template variables:

| Variable | Description |
|----------|-------------|
| `{{ProjectName}}` | Name of the current project |
| `{{FeatureTitle}}` | Title of the feature being worked on |
| `{{FeatureDescription}}` | Description of the feature |
| `{{BranchName}}` | Git branch name |
| `{{WorktreePath}}` | Path to the worktree directory |

Create prompt templates in the Prompt Templates page to reuse across features and projects.

### GitHub Integration

- **Sync PRs**: Import existing pull requests as features
- **Create PRs**: Push branches and create PRs directly from TreeAgent
- **Status Updates**: Feature status automatically reflects PR state (open, merged, closed)

## API Endpoints

### Health Check

```
GET /health
```

Returns application health status including database connectivity and process manager state.

## Real-time Updates

TreeAgent uses SignalR for real-time updates. Connect to `/hubs/agent` for:
- Agent message streaming
- Agent status changes
- Feature status changes

## Documentation

- [SPECIFICATION.md](SPECIFICATION.md) - Technical specification
- [ROADMAP.md](ROADMAP.md) - Development roadmap

## Development

### Running Tests

```bash
dotnet test
```

### Creating Database Migrations

```bash
cd src/TreeAgent.Web
dotnet ef migrations add <MigrationName>
```

Migrations are applied automatically on startup.

## License

MIT
