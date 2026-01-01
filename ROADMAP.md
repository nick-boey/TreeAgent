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

## Milestone 2: Claude Code Integration

Integrate with Claude Code CLI to spawn and manage agent instances.

### Tasks

- [ ] Review happy-cli reference implementation for patterns
  - [ ] Clone to .tmp/happy-cli if needed
  - [ ] Document relevant patterns
- [ ] Implement ClaudeCodeProcessManager
  - [ ] Start Claude Code in headless/JSON mode
  - [ ] Parse JSON output stream
  - [ ] Handle process lifecycle (start, stop, crash)
- [ ] Create message parsing service
  - [ ] Parse structured responses
  - [ ] Store messages to SQLite
- [ ] Build agent status monitoring
  - [ ] Track running processes
  - [ ] Detect and report errors

### Deliverables

- Ability to spawn Claude Code instance
- Message capture and storage
- Agent status tracking

---

## Milestone 3: Git Worktree Management

Implement worktree creation and lifecycle management.

### Tasks

- [ ] Implement GitWorktreeService
  - [ ] Create worktree for feature branch
  - [ ] List existing worktrees
  - [ ] Remove worktree (with cleanup)
- [ ] Integrate with feature lifecycle
  - [ ] Auto-create worktree when feature starts
  - [ ] Prune worktree when feature completes
- [ ] Handle edge cases
  - [ ] Worktree already exists
  - [ ] Branch conflicts
  - [ ] Dirty worktree cleanup

### Deliverables

- Automatic worktree management
- Worktree status in UI
- Clean worktree lifecycle

---

## Milestone 4: GitHub Synchronization

Sync features with GitHub pull requests.

### Tasks

- [ ] Implement GitHubService using Octokit
  - [ ] Authenticate with GitHub token
  - [ ] Fetch open pull requests
  - [ ] Fetch closed/merged pull requests
- [ ] Sync PR data to features
  - [ ] Import existing PRs as features
  - [ ] Update feature status from PR state
  - [ ] Handle PR merges and closes
- [ ] Create PR from feature
  - [ ] Push branch to remote
  - [ ] Create PR via API
  - [ ] Link PR number to feature

### Deliverables

- Two-way sync with GitHub PRs
- Feature status reflects PR state
- Create PRs from planned features

---

## Milestone 5: Feature Tree Visualization

Build the tree visualization for the feature roadmap.

### Tasks

- [ ] Design tree data structure
  - [ ] Parent-child relationships
  - [ ] Ordering within siblings
- [ ] Implement tree rendering component
  - [ ] Hierarchical list or simple tree view
  - [ ] Color coding by status
  - [ ] Node selection
- [ ] Feature detail panel
  - [ ] View/edit feature metadata
  - [ ] View linked agent and messages
  - [ ] Quick actions (start, cancel)

### Deliverables

- Visual tree of features
- Status color coding
- Feature detail view

---

## Milestone 6: Agent UI and Message Inspector

Build detailed agent monitoring and message inspection.

### Tasks

- [ ] Agent dashboard
  - [ ] List all active agents
  - [ ] Show agent status
  - [ ] Quick actions (stop, restart)
- [ ] Message inspector component
  - [ ] Real-time message stream
  - [ ] Message filtering and search
  - [ ] Syntax highlighting for code
- [ ] Implement SignalR for real-time updates
  - [ ] Push new messages to UI
  - [ ] Agent status changes
  - [ ] Feature status changes

### Deliverables

- Real-time agent monitoring
- Message drill-down capability
- Live updates without refresh

---

## Milestone 7: Custom System Prompts

Enable customization of agent behavior.

### Tasks

- [ ] System prompt editor
  - [ ] Per-feature system prompt
  - [ ] Project-level default prompts
  - [ ] Template variables (project name, feature title, etc.)
- [ ] Context injection
  - [ ] Inject feature tree context
  - [ ] Include related feature information
- [ ] Prompt library
  - [ ] Save and reuse prompts
  - [ ] Share prompts across projects

### Deliverables

- Customizable agent instructions
- Context-aware prompting
- Prompt management

---

## Milestone 8: Polish and Production Readiness

Prepare for production deployment.

### Tasks

- [ ] Error handling and recovery
  - [ ] Graceful degradation
  - [ ] Retry mechanisms
  - [ ] User-friendly error messages
- [ ] Logging and diagnostics
  - [ ] Structured logging
  - [ ] Health checks
- [ ] Documentation
  - [ ] User guide
  - [ ] Deployment guide
- [ ] Testing
  - [ ] Unit tests for services
  - [ ] Integration tests for agents

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
