# GitHub Authentication Fix Implementation Summary

## Issue
**ID:** hsp-3dc
**Title:** Fix GitHub authentication for agents

## Problem
Agents were unable to push code to GitHub or use `gh` CLI commands because:
1. `GITHUB_TOKEN` was not passed to spawned processes
2. Git credential helper was not configured for agent processes
3. No visibility into authentication status in the UI

## Solution
Implemented an **environment-only approach** - inject `GITHUB_TOKEN`, `GH_TOKEN`, and `GIT_ASKPASS` as environment variables into spawned processes. No global state changes to `gh auth` or git config.

## Implementation

### 1. GitHubEnvironmentService
**Files:**
- `src/Homespun/Features/GitHub/IGitHubEnvironmentService.cs` - Interface
- `src/Homespun/Features/GitHub/GitHubEnvironmentService.cs` - Implementation
- `src/Homespun/Features/GitHub/GitHubAuthStatus.cs` - Status record

**Functionality:**
- Resolves `GITHUB_TOKEN` from configuration (user secrets, config, or env var)
- Creates a `GIT_ASKPASS` script that echoes the token for git credential prompts
- Provides `CheckGhAuthStatusAsync()` to check `gh auth status`
- Cross-platform support (Windows .cmd / Unix .sh scripts)

**Environment variables returned:**
- `GITHUB_TOKEN` - The resolved token
- `GH_TOKEN` - Same token (gh CLI uses both)
- `GIT_ASKPASS` - Path to credential helper script
- `GIT_TERMINAL_PROMPT=0` - Disable interactive prompts
- `GIT_AUTHOR_NAME` - Git author name (defaults to "Homespun Bot")
- `GIT_AUTHOR_EMAIL` - Git author email (defaults to "homespun@localhost")
- `GIT_COMMITTER_NAME` - Git committer name (same as author)
- `GIT_COMMITTER_EMAIL` - Git committer email (same as author)

### 2. OpenCodeServerManager Enhancement
**File:** `src/Homespun/Features/OpenCode/Services/OpenCodeServerManager.cs`

- Added `IGitHubEnvironmentService` dependency
- Modified `StartServerProcess()` to inject environment variables into `ProcessStartInfo.Environment`
- Agents now inherit GitHub authentication automatically

### 3. CommandRunner Enhancement
**File:** `src/Homespun/Features/Commands/CommandRunner.cs`

- Added `IGitHubEnvironmentService` dependency
- Modified `RunAsync()` to inject environment variables
- All shell commands (including `bd sync`) now have GitHub auth

### 4. Settings Page
**Files:**
- `src/Homespun/Components/Pages/Settings.razor` - Settings page
- `src/Homespun/Components/Layout/NavMenu.razor` - Added Settings link
- `src/Homespun/Components/Layout/NavMenu.razor.css` - Added gear icon

**Features:**
- Display GitHub authentication status
- Show masked token (`ghp_***...xyz`)
- Show GitHub username (if authenticated via gh CLI)
- Auth method indicator (Token, gh CLI, or Both)
- Refresh button to recheck status
- Configuration instructions

### 5. Service Registration
**File:** `src/Homespun/Program.cs`

```csharp
builder.Services.AddSingleton<IGitHubEnvironmentService, GitHubEnvironmentService>();
```

## Beads Issue Created
**ID:** hsp-sqk
**Title:** Create Settings page with GitHub/git authentication health display
**Priority:** P3

Follow-up issue for Settings page enhancements.

## How It Works

### Agent Workflow
1. Agent is spawned in worktree directory
2. `OpenCodeServerManager` injects `GITHUB_TOKEN`, `GH_TOKEN`, `GIT_ASKPASS` into process environment
3. Agent can now run:
   - `gh pr create` (gh CLI uses `GITHUB_TOKEN`/`GH_TOKEN` automatically)
   - `git push` (uses `GIT_ASKPASS` for credential prompts)
   - `bd sync` (uses git credentials for pushing)

### Beads Sync Flow
1. `CommandRunner` executes `bd sync`
2. Environment variables are injected into the process
3. When git needs credentials, `GIT_ASKPASS` provides the token
4. No manual intervention required

## Token Resolution Priority
1. User secrets: `GitHub:Token`
2. Configuration: `GITHUB_TOKEN`
3. Environment variable: `GITHUB_TOKEN`

## Git Identity Resolution Priority
1. Configuration: `Git:AuthorName`, `Git:AuthorEmail`
2. Environment variable: `GIT_AUTHOR_NAME`, `GIT_AUTHOR_EMAIL`
3. Default: "Homespun Bot" / "homespun@localhost"

## Files Modified/Created

### New Files
- `src/Homespun/Features/GitHub/IGitHubEnvironmentService.cs`
- `src/Homespun/Features/GitHub/GitHubEnvironmentService.cs`
- `src/Homespun/Features/GitHub/GitHubAuthStatus.cs`
- `src/Homespun/Components/Pages/Settings.razor`
- `tests/Homespun.Tests/Features/GitHub/GitHubEnvironmentServiceTests.cs`

### Modified Files
- `src/Homespun/Features/OpenCode/Services/OpenCodeServerManager.cs`
- `src/Homespun/Features/Commands/CommandRunner.cs`
- `src/Homespun/Features/Git/GitWorktreeService.cs` (added NullGitHubEnvironmentService for parameterless constructor)
- `src/Homespun/Components/Layout/NavMenu.razor`
- `src/Homespun/Components/Layout/NavMenu.razor.css`
- `src/Homespun/Program.cs`
- `tests/Homespun.Tests/Features/OpenCode/OpenCodeServerManagerTests.cs`
- `tests/Homespun.Tests/Features/OpenCode/OpenCodeIntegrationTests.cs`
- `tests/Homespun.Tests/Features/OpenCode/OpenCodeWorkingDirectoryIntegrationTests.cs`
- `tests/Homespun.Tests/Features/GitHub/GitHubServiceIntegrationTests.cs`

## Testing

### Manual Testing Steps
1. Set GITHUB_TOKEN:
   ```bash
   # User secrets (recommended)
   dotnet user-secrets set "GitHub:Token" "ghp_..." --project src/Homespun
   # OR environment variable
   export GITHUB_TOKEN="ghp_..."
   ```

2. Start Homespun:
   ```bash
   cd src/Homespun
   dotnet run
   ```

3. Navigate to `/settings` and verify:
   - Status shows "Authenticated"
   - Token is masked
   - Auth method shows "Token" or "Both"

4. Test agent workflow:
   - Create a worktree for a feature
   - Run an agent that creates a PR
   - Agent should successfully run `gh pr create`

5. Test beads sync:
   ```bash
   bd sync
   ```

### Unit Tests
All 278 non-integration tests pass, including new tests for `GitHubEnvironmentService`.

## Benefits

1. **No Global State Changes** - Authentication is scoped to spawned processes only
2. **Cross-Platform** - Works on Windows and Unix
3. **Automatic Authentication** - No manual `gh auth login` required
4. **Visibility** - Settings page shows authentication status
5. **Testable** - Service is mockable for unit tests
