# Homespun

Homespun is a .NET application for managing software development workflows using Git, GitHub, and agentic AI tools like Claude Code. It models development as a tree where each node represents a feature or fix aligned with GitHub pull requests.

## Overview

Homespun provides a visual interface for planning and executing software development through AI agents. It manages:

- **Feature Tree**: Visualize past, present, and future pull requests as a tree structure
- **Multiple Projects**: Work on multiple repositories simultaneously
- **Git Worktrees**: Automatically manage worktrees for each feature branch
- **Claude Code Agents**: Spawn and manage headless Claude Code instances
- **Message Persistence**: Store all agent communications in JSON storage

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
  - Orange: Has review comments
  - Cyan: Approved
  - Grey: Future/planned
- Automated review polling from GitHub
- Agent status panel with real-time updates

## Technology Stack

- **.NET 8+**: Core runtime
- **Blazor Server**: Web frontend with SSR
- **JSON File Storage**: Persistence
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
git clone https://github.com/your-org/Homespun.git
cd Homespun
dotnet restore
dotnet build
```

### Configuration

Set the following environment variables before running:

| Variable | Description | Default |
|----------|-------------|---------|
| `HOMESPUN_DATA_PATH` | Path to data file | `~/.homespun/homespun-data.json` |
| `GITHUB_TOKEN` | GitHub personal access token for PR operations | (required for GitHub sync) |
| `HOMESPUN_WORKTREE_ROOT` | Base directory for worktrees | (uses project path) |
| `CLAUDE_CODE_PATH` | Path to Claude Code CLI executable | (uses PATH) |

Example:
```bash
export GITHUB_TOKEN="ghp_your_token_here"
export HOMESPUN_DATA_PATH="/data/.homespun/homespun-data.json"
```

### Running

```bash
dotnet run --project src/Homespun
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

Homespun supports customizable system prompts with template variables:

| Variable | Description |
|----------|-------------|
| `{{ProjectName}}` | Name of the current project |
| `{{FeatureTitle}}` | Title of the feature being worked on |
| `{{FeatureDescription}}` | Description of the feature |
| `{{BranchName}}` | Git branch name |
| `{{WorktreePath}}` | Path to the worktree directory |

Create prompt templates in the Prompt Templates page to reuse across features and projects.

### Pull Request Workflow

Homespun organizes development as a continuous chain of pull requests across three time stages:

#### Time Dimension

Each pull request has a calculated time value `t` based on its position in the workflow. This value is not stored but computed dynamically:

- **Past (`t <= 0`)**: Merged/closed PRs. The value is calculated from merge order - most recent merge has `t = 0`, older PRs have negative values (`t = -1`, `t = -2`, etc.).
- **Present (`t = 1`)**: All currently open PRs have `t = 1`. Multiple PRs can exist in parallel at this stage.
- **Future (`t > 1`)**: Planned changes stored in `ROADMAP.json`. The value is calculated from the change's depth in the tree structure.

#### PR Status Workflow

```mermaid
stateDiagram-v2
    [*] --> InProgress : Agent opens PR
    InProgress --> ReadyForReview : Agent completes work
    ReadyForReview --> InProgress : User adds code comments
    ReadyForReview --> ChecksFailing : CI checks fail
    ChecksFailing --> InProgress : Agent fixes issues
    ReadyForReview --> ReadyForMerging : User approves (no comments)
    ReadyForMerging --> [*] : User merges PR
    InProgress --> Conflict : Rebase fails
    Conflict --> InProgress : User/agent resolves
```

| Status | Color | Description |
|--------|-------|-------------|
| In Development | Yellow | Agent is actively working on the PR |
| Ready for Review | Green | Agent completed, awaiting user review |
| Has Review Comments | Orange | PR has review comments needing attention |
| Approved | Cyan | PR approved and ready to merge |
| Merged | Blue | PR has been merged (past) |
| Closed | Red | PR was closed without merging (past) |

#### Automatic Rebasing

When a PR is merged, all other open PRs (`t = 1`) are automatically rebased onto the new main branch HEAD. This keeps all parallel branches up-to-date and ensures clean merges.

#### Branch Naming Convention

Branches follow the pattern: `{group}/{type}/{id}`
- `group`: Project or component (e.g., `core`, `web`, `services`)
- `type`: Change type (`feature`, `bug`, `refactor`, `docs`, `test`, `chore`)
- `id`: Short identifier describing the change

Examples: `core/feature/pr-time-dimension`, `web/bug/fix-status-colors`

#### Future Changes (ROADMAP.json)

Planned changes are stored in `ROADMAP.json` on the default branch:
- Each change includes `id`, `group`, `type`, `title`, and `instructions`
- Changes are organized as a recursive tree structure with nested children
- When an agent starts work on a future change, it becomes a current PR

#### Plan Update PRs

When a PR contains *only* modifications to `ROADMAP.json` (planning changes without code), it is treated as a special `plan-update` group PR. These PRs:
- Contain only modifications to the roadmap file (no code changes)
- Must be merged before other PRs
- Ensure the single source of truth for planning is always consistent

Note: When a future change is promoted to a current PR, the `ROADMAP.json` is also updated to remove that change. This is a normal PR, not a plan-update PR, since it includes code changes.

#### GitHub Sync

- **Past PRs**: Imported from closed/merged PRs with correct time ordering
- **Current PRs**: Synced with open PRs, status reflects review/check state
- **Create PRs**: Push branches and create PRs directly from Homespun

## API Endpoints

### Health Check

```
GET /health
```

Returns application health status including data store accessibility and process manager state.

## Real-time Updates

Homespun uses SignalR for real-time updates. Connect to `/hubs/agent` for:
- Agent message streaming
- Agent status changes
- Feature status changes

## Deployment

Homespun can be deployed to Ubuntu virtual machines or Docker containers. All deployment methods support secure access via Tailscale.

For detailed deployment instructions, see the [Installation Guide](docs/installation.md).

### Quick start

**Docker (Windows):**
```powershell
docker build -t homespun:local .
.\install\container\run.ps1
```

**Docker (Linux/macOS):**
```bash
# Make scripts executable (first time only)
chmod +x ./install/container/run.sh ./install/container/test.sh

# Build and run interactively (for testing)
./install/container/test.sh

# Or build manually and run with more options
docker build -t homespun:local .
./install/container/run.sh --local -it
```

**Ubuntu VM:**
```bash
sudo ./install/vm/install.sh
sudo ./install/vm/run.sh
```

## Documentation

- [Installation Guide](docs/installation.md) - Deployment options (VM, Docker)
- [SPECIFICATION.md](SPECIFICATION.md) - Technical specification
- [ROADMAP.md](ROADMAP.md) - Development roadmap

## Development

### Running Tests

```bash
dotnet test
```

## Known Issues

### OpenCode Web UI Shows Wrong Branch for Worktrees

When running OpenCode in a Git worktree, the web UI may display "main" (or your default branch) instead of the actual worktree branch. This is a bug in OpenCode's VCS module where it runs `git rev-parse --abbrev-ref HEAD` in the main repository directory instead of the worktree directory.

**Workaround**: The terminal within the OpenCode web UI correctly operates in the worktree, so you can verify the actual branch using `git branch` in the terminal.

**Root Cause**: In OpenCode's `packages/opencode/src/project/vcs.ts`, line 35 uses `.cwd(Instance.worktree)` which points to the main repository path (from `git rev-parse --git-common-dir`), not the worktree directory (`Instance.directory`).

**Status**: A similar fix was applied to OpenCode's TUI in commit `16f9edc1a` but the web app still has this issue. The fix requires changing `.cwd(Instance.worktree)` to `.cwd(Instance.directory)` in the `currentBranch()` function.

## License

MIT
