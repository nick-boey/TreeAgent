# TreeAgent Development Roadmap

This document outlines the development roadmap for TreeAgent, organized into milestones that can be developed incrementally.

## Milestone 1: Foundation (Complete)

Establish the core project structure and basic infrastructure.

### Tasks

- [x] Create .NET solution with single web project
  - [x] TreeAgent.Web (Blazor SSR, services, data)
  - [x] Components/ for Blazor components
  - [x] Data/ for EF Core entities and DbContext
  - [x] Services/ for business logic
- [x] Set up SQLite database with EF Core
  - [x] Projects table
  - [x] Features table
  - [x] Agents table
  - [x] Messages table
- [x] Implement basic project CRUD operations
- [x] Create minimal Blazor layout and navigation

### Deliverables

- Running Blazor application
- Database migrations working
- Project create/list/edit functionality

---

## Milestone 2: Claude Code Integration (Complete)

Integrate with Claude Code CLI to spawn and manage agent instances.

### Tasks

- [x] Review happy-cli reference implementation for patterns
  - [x] Clone to .tmp/happy-cli if needed
  - [x] Document relevant patterns
- [x] Implement ClaudeCodeProcessManager
  - [x] Start Claude Code in headless/JSON mode
  - [x] Parse JSON output stream
  - [x] Handle process lifecycle (start, stop, crash)
- [x] Create message parsing service
  - [x] Parse structured responses
  - [x] Store messages to SQLite
- [x] Build agent status monitoring
  - [x] Track running processes
  - [x] Detect and report errors

### Deliverables

- Ability to spawn Claude Code instance
- Message capture and storage
- Agent status tracking

---

## Milestone 3: Git Worktree Management (Complete)

Implement worktree creation and lifecycle management.

### Tasks

- [x] Implement GitWorktreeService
  - [x] Create worktree for feature branch
  - [x] List existing worktrees
  - [x] Remove worktree (with cleanup)
- [x] Integrate with feature lifecycle
  - [x] Auto-create worktree when feature starts
  - [x] Prune worktree when feature completes
- [x] Handle edge cases
  - [x] Worktree already exists
  - [x] Branch conflicts
  - [x] Dirty worktree cleanup

### Deliverables

- Automatic worktree management
- Worktree status in UI
- Clean worktree lifecycle

---

## Milestone 4: GitHub Synchronization (Complete)

Sync features with GitHub pull requests.

### Tasks

- [x] Implement GitHubService using Octokit
  - [x] Authenticate with GitHub token
  - [x] Fetch open pull requests
  - [x] Fetch closed/merged pull requests
- [x] Sync PR data to features
  - [x] Import existing PRs as features
  - [x] Update feature status from PR state
  - [x] Handle PR merges and closes
- [x] Create PR from feature
  - [x] Push branch to remote
  - [x] Create PR via API
  - [x] Link PR number to feature

### Deliverables

- Two-way sync with GitHub PRs
- Feature status reflects PR state
- Create PRs from planned features

---

## Milestone 5: Feature Tree Visualization (Complete)

Build the tree visualization for the feature roadmap.

### Tasks

- [x] Design tree data structure
  - [x] Parent-child relationships
  - [x] Ordering within siblings
- [x] Implement tree rendering component
  - [x] Hierarchical list or simple tree view
  - [x] Color coding by status
  - [x] Node selection
- [x] Feature detail panel
  - [x] View/edit feature metadata
  - [x] View linked agent and messages
  - [x] Quick actions (start, cancel)

### Deliverables

- Visual tree of features
- Status color coding
- Feature detail view

---

## Milestone 6: Agent UI and Message Inspector (Complete)

Build detailed agent monitoring and message inspection.

### Tasks

- [x] Agent dashboard
  - [x] List all active agents
  - [x] Show agent status
  - [x] Quick actions (stop, restart)
- [x] Message inspector component
  - [x] Real-time message stream
  - [x] Message filtering and search
  - [x] Syntax highlighting for code
- [x] Implement SignalR for real-time updates
  - [x] Push new messages to UI
  - [x] Agent status changes
  - [x] Feature status changes

### Deliverables

- Real-time agent monitoring
- Message drill-down capability
- Live updates without refresh

---

## Milestone 7: Custom System Prompts (Complete)

Enable customization of agent behavior.

### Tasks

- [x] System prompt editor
  - [x] Per-feature system prompt
  - [x] Project-level default prompts
  - [x] Template variables (project name, feature title, etc.)
- [x] Context injection
  - [x] Inject feature tree context
  - [x] Include related feature information
- [x] Prompt library
  - [x] Save and reuse prompts
  - [x] Share prompts across projects

### Deliverables

- Customizable agent instructions
- Context-aware prompting
- Prompt management

---

## Milestone 8: Polish and Production Readiness (Complete)

Prepare for production deployment.

### Tasks

- [x] Error handling and recovery
  - [x] Graceful degradation
  - [x] Retry mechanisms
  - [x] User-friendly error messages
- [x] Logging and diagnostics
  - [x] Structured logging
  - [x] Health checks
- [x] Documentation
  - [x] User guide
  - [x] Deployment guide
- [x] Testing
  - [x] Unit tests for services
  - [x] Integration tests for agents

### Deliverables

- Production-ready application
- Documentation
- Test coverage

---

## Future Considerations

Features for potential future development:

- **Interactive Tree Editing**: Drag-and-drop reordering and reparenting
- **Agent Collaboration**: Multiple agents working on related features
- **Auto-merge Pipeline**: Automated PR merging with conflict resolution
- **Code Review Integration**: AI-assisted code review workflow
- **Metrics Dashboard**: Development velocity and agent performance metrics
- **Protocol Layer (ACP/AG-UI/MCP)**: Standardized protocols for agent interoperability
- **Multi-user Support**: Team features with role-based access
