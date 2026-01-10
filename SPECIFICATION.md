# Homespun Specification

## 1. Introduction

Homespun is an application for managing software development using Git, GitHub, and agentic AI tools. It treats
development as a tree where each node represents a feature or fix that corresponds to a GitHub pull request.

## 2. System Overview

### 2.1 Core Concepts

#### Feature Tree

- Each node in the tree represents a discrete unit of work (feature, fix, refactor)
- Nodes align with the concept of GitHub pull requests
- The tree supports:
    - **Past nodes**: Closed/merged pull requests (historical context)
    - **Present nodes**: Open pull requests (active development)
    - **Future nodes**: Planned features (not yet created as PRs)

#### Projects

- A project represents a Git repository being worked on
- Multiple projects can be active simultaneously
- Each project has its own tree of features

### 2.2 Feature Status

Features are color-coded by status:

| Status              | Color   | Description                              |
|---------------------|---------|------------------------------------------|
| Merged              | Blue    | Complete and merged into target branch   |
| Cancelled           | Red     | Closed without merging                   |
| In Development      | Yellow  | Active development in progress           |
| Ready for Review    | Green   | PR created, awaiting human review        |
| Has Review Comments | Orange  | PR has review comments needing attention |
| Approved            | Cyan    | PR approved and ready to merge           |
| Future              | Grey    | Planned but not yet started              |

## 3. Technical Architecture

### 3.1 Technology Stack

| Component      | Technology                              |
|----------------|-----------------------------------------|
| Runtime        | .NET 8+                                 |
| Web Framework  | Blazor Server (SSR)                     |
| Data Storage   | JSON File Storage                       |
| Network Access | Tailscale (optional, for remote access) |
| AI Agent       | Claude Code CLI                         |

### 3.2 System Components

```
┌─────────────────────────────────────────────────────────────┐
│                      Blazor SSR Frontend                     │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────┐  │
│  │  Tree View  │  │ Agent View  │  │  Message Inspector  │  │
│  └─────────────┘  └─────────────┘  └─────────────────────┘  │
│  ┌─────────────────────────┐  ┌────────────────────────────┐ │
│  │   Prompt Templates     │  │     Feature Editor          │ │
│  └─────────────────────────┘  └────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
                              │
┌─────────────────────────────────────────────────────────────┐
│                    SignalR Hub (Real-time)                   │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │     Agent Hub: messages, status changes, updates        │ │
│  └─────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
                              │
┌─────────────────────────────────────────────────────────────┐
│                       Services                               │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────┐  │
│  │   Project   │  │  Worktree   │  │      GitHub         │  │
│  │   Service   │  │   Service   │  │      Service        │  │
│  └─────────────┘  └─────────────┘  └─────────────────────┘  │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────┐  │
│  │   Feature   │  │   Agent     │  │   System Prompt     │  │
│  │   Service   │  │   Service   │  │      Service        │  │
│  └─────────────┘  └─────────────┘  └─────────────────────┘  │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │              Claude Code Process Manager                 │ │
│  └─────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
                              │
┌─────────────────────────────────────────────────────────────┐
│                       Data Layer                             │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │                   JSON Data Store                        │ │
│  │  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌────────────┐ │ │
│  │  │ Projects │ │ Features │ │ Messages │ │   Agents   │ │ │
│  │  └──────────┘ └──────────┘ └──────────┘ └────────────┘ │ │
│  │  ┌──────────────────────────────────────────────────┐  │ │
│  │  │              Prompt Templates                     │  │ │
│  │  └──────────────────────────────────────────────────┘  │ │
│  └─────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

### 3.3 Project Structure

Single-project architecture for simplicity:

```
Homespun/
├── src/
│   └── Homespun/
│       ├── Components/       # Blazor components
│       │   ├── Layout/      # Layout components (MainLayout, NavMenu)
│       │   ├── Pages/       # Routed page components
│       │   └── Shared/      # Reusable components (FeatureTree, FeatureNode)
│       ├── Data/            # EF Core DbContext and entities
│       │   └── Entities/    # Entity classes (Project, Feature, Agent, Message)
│       ├── HealthChecks/    # Health check implementations
│       ├── Hubs/            # SignalR hubs for real-time updates
│       ├── Services/        # Business logic services
│       └── Program.cs
├── tests/
│   └── Homespun.Tests/ # Unit and integration tests
└── Homespun.sln
```

### 3.4 Core Services

| Service                    | Responsibility                                                    |
|----------------------------|-------------------------------------------------------------------|
| `ProjectService`           | CRUD operations for projects                                      |
| `FeatureService`           | Feature management with tree structure and worktree integration   |
| `AgentService`             | Agent lifecycle management with Claude Code process orchestration |
| `GitHubService`            | GitHub PR synchronization using Octokit                           |
| `GitWorktreeService`       | Git worktree operations                                           |
| `SystemPromptService`      | Template processing for agent system prompts                      |
| `ClaudeCodeProcessManager` | Process lifecycle for Claude Code CLI instances                   |
| `MessageParser`            | Parse JSON output from Claude Code agents                         |
| `FutureChangeTransitionService` | Status transitions for roadmap changes (Pending → InProgress → AwaitingPR → Complete) |
| `ReviewPollingService`     | Background service polling GitHub for PR reviews                  |
| `AgentWorkflowService`     | End-to-end agent workflow orchestration                          |

## 4. Functional Requirements

### 4.1 Project Management

- **FR-PM-01**: Create, edit, and delete projects
- **FR-PM-02**: Associate a project with a local Git repository path
- **FR-PM-03**: Configure GitHub repository connection (owner/repo)
- **FR-PM-04**: Set default branch for project

### 4.2 Feature Tree Management

- **FR-FT-01**: Display features as a tree structure
- **FR-FT-02**: Sync with GitHub to import existing pull requests
- **FR-FT-03**: Create new future feature nodes
- **FR-FT-04**: Define parent-child relationships between features
- **FR-FT-05**: Edit feature metadata (title, description, branch name)

### 4.3 Git Worktree Management

- **FR-WT-01**: Create worktree for each active feature
- **FR-WT-02**: Prune worktrees for completed/cancelled features
- **FR-WT-03**: Manage worktree directory structure
- **FR-WT-04**: Handle branch creation and checkout

### 4.4 Claude Code Agent Management

- **FR-AG-01**: Spawn headless Claude Code instances
- **FR-AG-02**: Assign agents to specific worktrees
- **FR-AG-03**: Provide custom system instructions per agent
- **FR-AG-04**: Monitor agent status (running, idle, error)
- **FR-AG-05**: Terminate agents gracefully
- **FR-AG-06**: Parse and store JSON output from agents

### 4.5 Message Management

- **FR-MSG-01**: Store all messages sent to agents
- **FR-MSG-02**: Store all responses from agents
- **FR-MSG-03**: Display message history per agent
- **FR-MSG-04**: Search and filter messages

### 4.6 User Interface

- **FR-UI-01**: Tree view of features
- **FR-UI-02**: Agent status dashboard
- **FR-UI-03**: Message inspector for individual agents
- **FR-UI-04**: Feature editor for planning
- **FR-UI-05**: Real-time updates via SignalR

### 4.7 System Prompts

- **FR-SP-01**: Per-feature custom system prompts
- **FR-SP-02**: Project-level default system prompts
- **FR-SP-03**: Template variable substitution (project name, feature title, etc.)
- **FR-SP-04**: Prompt template library for reuse across projects
- **FR-SP-05**: Context injection with feature tree information

## 5. Non-Functional Requirements

### 5.1 Performance

- **NFR-P-01**: Support at least 10 concurrent agents
- **NFR-P-02**: UI updates within 100ms of state change

### 5.2 Security

- **NFR-S-01**: Tailscale can restrict access to private network
- **NFR-S-02**: No sensitive credentials stored in data file
- **NFR-S-03**: Environment-based configuration for secrets

### 5.3 Reliability

- **NFR-R-01**: Graceful handling of agent crashes
- **NFR-R-02**: Automatic reconnection to agents

## 6. Data Schema

The application uses a JSON file store. The schema consists of the following entities:

### 6.1 Project

- **Id**: Unique identifier
- **Name**: Project name
- **LocalPath**: Local filesystem path
- **GitHubOwner**: GitHub repository owner (optional)
- **GitHubRepo**: GitHub repository name (optional)
- **DefaultBranch**: Default branch name (e.g., 'main')
- **CreatedAt**: Timestamp
- **UpdatedAt**: Timestamp

### 6.2 Feature (Pull Request)

- **Id**: Unique identifier
- **ProjectId**: Reference to parent Project
- **ParentId**: Reference to parent Feature (for tree structure)
- **Title**: Feature title
- **Description**: Feature description
- **BranchName**: Git branch name
- **Status**: Current status (e.g., InDevelopment, ReadyForReview)
- **GitHubPRNumber**: Associated GitHub PR number
- **WorktreePath**: Path to the feature's git worktree
- **CreatedAt**: Timestamp
- **UpdatedAt**: Timestamp

### 6.3 Agent

- **Id**: Unique identifier
- **FeatureId**: Reference to associated Feature
- **ProcessId**: OS process ID for the agent process
- **Status**: Agent status (e.g., Running, Stopped, Error)
- **SystemPrompt**: Custom system prompt used
- **CreatedAt**: Timestamp
- **UpdatedAt**: Timestamp

### 6.4 Message

- **Id**: Unique identifier
- **AgentId**: Reference to associated Agent
- **Role**: Sender role (e.g., User, Assistant)
- **Content**: Message content
- **Timestamp**: Timestamp
- **Metadata**: Additional metadata

### 6.5 PromptTemplate

- **Id**: Unique identifier
- **Name**: Template name
- **Content**: Template content
- **Description**: Template description
- **IsDefault**: Whether this is the default template
- **CreatedAt**: Timestamp
- **UpdatedAt**: Timestamp

## 7. External Dependencies

### 7.1 Claude Code CLI

The application depends on Claude Code CLI for agent functionality:

- JSON output mode for structured responses
- Headless operation capability
- Process-based lifecycle management

### 7.2 Reference Implementation

The [happy-cli](https://github.com/slopus/happy-cli) project provides reference patterns for Claude Code process
management. Clone for reference if needed:

```bash
git clone https://github.com/slopus/happy-cli .tmp/happy-cli
```

## 8. Deployment

### 8.1 Target Environment

- Cloud VM or local machine
- Optionally accessible via Tailscale private network
- Linux or Windows
- .NET 8 runtime required

### 8.2 Configuration

Environment variables:

- `HOMESPUN_DATA_PATH`: Path to data file (default: `homespun-data.json`)
- `HOMESPUN_WORKTREE_ROOT`: Base directory for worktrees
- `GITHUB_TOKEN`: GitHub API access token
- `CLAUDE_CODE_PATH`: Path to Claude Code CLI executable

### 8.3 Health Checks

The application exposes a health check endpoint at `/health` that monitors:

- Data store accessibility
- Process manager status

### 8.4 Real-time Communication

SignalR hub available at `/hubs/agent` for:

- Agent message streaming
- Agent status change notifications
- Feature status updates

## 9. Template Variables

System prompts support the following template variables:

| Variable                 | Description                                  |
|--------------------------|----------------------------------------------|
| `{{ProjectName}}`        | Name of the current project                  |
| `{{FeatureTitle}}`       | Title of the feature being worked on         |
| `{{FeatureDescription}}` | Description of the feature                   |
| `{{BranchName}}`         | Git branch name for the feature              |
| `{{WorktreePath}}`       | Absolute path to the worktree directory      |
| `{{ParentFeature}}`      | Title of the parent feature (if any)         |
| `{{ChildFeatures}}`      | Comma-separated list of child feature titles |

## 10. Agent Workflow

### 10.1 Workflow Overview

The agent workflow automates the development lifecycle from roadmap planning to PR merge:

```
┌─────────────────────────────────────────────────────────────────────────┐
│                        Agent Workflow Lifecycle                          │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  ROADMAP.json          Agent Started           PR Created              │
│  ┌──────────┐         ┌────────────┐         ┌────────────┐            │
│  │ Pending  │────────>│ InProgress │────────>│ AwaitingPR │            │
│  └──────────┘         └────────────┘         └────────────┘            │
│       │                     │                      │                     │
│       │                     │ (failure)            │ (merged)           │
│       │<────────────────────┘                      v                     │
│                                              ┌────────────┐            │
│                                              │  Complete  │            │
│                                              └────────────┘            │
└─────────────────────────────────────────────────────────────────────────┘
```

### 10.2 Future Change Status

Roadmap changes (`ROADMAP.json`) have the following status flow:

| Status      | Description                                      |
|-------------|--------------------------------------------------|
| Pending     | Change is planned but not started                |
| InProgress  | Agent is actively working on the change          |
| AwaitingPR  | PR has been created, awaiting review/merge       |
| Complete    | PR has been merged                               |

### 10.3 Open PR Status

Open PRs are tracked with detailed review status:

| Status           | Description                                        |
|------------------|---------------------------------------------------|
| InDevelopment    | Work in progress, agent may be active              |
| ReadyForReview   | Agent completed, PR ready for human review         |
| HasReviewComments| PR has review comments needing attention           |
| Approved         | PR approved and ready to merge                     |

### 10.4 Workflow Services

- **AgentWorkflowService**: Orchestrates the end-to-end workflow:
  - Starts OpenCode agent for a feature
  - Monitors agent completion
  - Handles success/failure transitions
  
- **FutureChangeTransitionService**: Manages roadmap status:
  - Updates `ROADMAP.json` when status changes
  - Broadcasts status changes via SignalR
  - Handles agent failure recovery

- **ReviewPollingService**: Background polling for reviews:
  - Polls GitHub every 60 seconds (configurable)
  - Updates PR status based on review state
  - Broadcasts review updates via SignalR

### 10.5 SignalR Events

Agent workflow events broadcast via SignalR:

| Event                      | Payload                                  | Description                    |
|---------------------------|------------------------------------------|--------------------------------|
| `FutureChangeStatusChanged` | projectId, changeId, newStatus         | Roadmap change status updated  |
| `PullRequestReviewsUpdated` | projectId, prId, prNumber, reviews     | PR review state changed        |
| `AgentStarted`             | pullRequestId, agentStatus              | Agent started for a PR         |
| `AgentStopped`             | pullRequestId                           | Agent stopped                  |
| `AgentEvent`               | pullRequestId, event                    | Agent activity event           |

## 11. Later (Future Enhancements)

The following features are planned for future implementation:

### 11.1 GitHub Webhooks

Replace polling with real-time webhook notifications:
- **PR Review Events**: Instant notification when reviews are submitted
- **PR Merge Events**: Trigger rebase of dependent branches
- **Check Events**: CI/CD status updates

### 11.2 Automatic Review Response

Configure agents to automatically respond to review comments:
- Parse review comment content
- Generate responses using AI
- Create review responses or code fixes

### 11.3 Multi-Agent Coordination

Support multiple agents working on related features:
- Dependency-aware task assignment
- Conflict detection and resolution
- Coordinated rebasing
