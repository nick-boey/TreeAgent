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

**TDD is mandatory for all planning and implementation.** When developing new features or fixing bugs:

1. **Write tests first** - Before implementing any code, write failing tests that define the expected behavior
2. **Red-Green-Refactor** - Follow the TDD cycle for every change:
   - Red: Write a failing test
   - Green: Write minimal code to make the test pass
   - Refactor: Clean up the code while keeping tests green
3. **Plan with tests in mind** - When planning features, identify test cases as part of the design
4. **Test naming** - Use descriptive test names that explain the scenario and expected outcome (e.g., `GetProjectById_ReturnsNotFound_WhenNotExists`)
5. **Test coverage** - Aim for comprehensive coverage of business logic in services

### Project Structure (Vertical Slice Architecture)

The project follows Vertical Slice Architecture, organizing code by feature rather than technical layer. Each feature contains its own services, data models, and components.

```
src/Homespun/
├── Features/                    # Feature slices (all business logic)
│   ├── Fleece/                  # Fleece issue tracking integration
│   │   └── Services/            # FleeceService, FleeceIssueTransitionService
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

tests/
├── Homespun.Tests/              # Unit tests (NUnit + bUnit + Moq)
│   ├── Features/                # Tests organized by feature (mirrors src structure)
│   │   ├── ClaudeCode/          # ClaudeCode service and hub tests
│   │   ├── Git/                 # Git worktree tests
│   │   ├── GitHub/              # GitHub service tests
│   │   └── PullRequests/        # PR model and workflow tests
│   ├── Components/              # bUnit tests for Blazor components
│   └── Helpers/                 # Shared test utilities and fixtures
├── Homespun.Api.Tests/          # API integration tests (WebApplicationFactory)
└── Homespun.E2E.Tests/          # End-to-end tests (Playwright)
```

### Feature Slices

- **Fleece**: Integration with Fleece issue tracking - JSONL-based storage in `.fleece/` directory, uses Fleece.Core types directly
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
# Run all tests
dotnet test

# Run specific test project
dotnet test tests/Homespun.Tests
dotnet test tests/Homespun.Api.Tests
dotnet test tests/Homespun.E2E.Tests

# Run with verbose output
dotnet test --verbosity normal
```

## Testing Infrastructure

The project uses a comprehensive three-tier testing strategy:

### Unit Tests (Homespun.Tests)

**Framework:** NUnit + bUnit + Moq

Unit tests cover service logic and Blazor components in isolation.

**Service tests** use Moq for dependency mocking:
```csharp
[TestFixture]
public class GitHubServiceTests
{
    private Mock<IGitHubClientWrapper> _mockClient = null!;
    private GitHubService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _mockClient = new Mock<IGitHubClientWrapper>();
        _sut = new GitHubService(_mockClient.Object);
    }

    [Test]
    public async Task GetPullRequests_ReturnsEmpty_WhenNoPullRequests()
    {
        // Arrange
        _mockClient.Setup(x => x.GetPullRequestsAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new List<PullRequest>());

        // Act
        var result = await _sut.GetPullRequestsAsync("owner", "repo");

        // Assert
        Assert.That(result, Is.Empty);
    }
}
```

**Component tests** use bUnit with a shared `BunitTestContext` base class:
```csharp
[TestFixture]
public class ModelSelectorTests : BunitTestContext
{
    [Test]
    public void ModelSelector_DefaultsToOpus()
    {
        var cut = Render<ModelSelector>();
        var select = cut.Find("select");
        Assert.That(select.GetAttribute("value"), Is.EqualTo("opus"));
    }

    [Test]
    public void ModelSelector_InvokesCallbackOnChange()
    {
        string? selectedModel = null;
        var cut = Render<ModelSelector>(parameters =>
            parameters.Add(p => p.SelectedModelChanged,
                EventCallback.Factory.Create<string>(this, value => selectedModel = value)));

        cut.Find("select").Change("haiku");

        Assert.That(selectedModel, Is.EqualTo("haiku"));
    }
}
```

### API Integration Tests (Homespun.Api.Tests)

**Framework:** NUnit + WebApplicationFactory

API tests verify HTTP endpoints using an in-memory test server with `HomespunWebApplicationFactory`:

```csharp
[TestFixture]
public class ProjectsApiTests
{
    private HomespunWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    [SetUp]
    public void SetUp()
    {
        _factory = new HomespunWebApplicationFactory();
        _client = _factory.CreateClient();
    }

    [Test]
    public async Task GetProjects_ReturnsEmptyList_WhenNoProjects()
    {
        var response = await _client.GetAsync("/api/projects");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var projects = await response.Content.ReadFromJsonAsync<List<Project>>();
        Assert.That(projects, Is.Empty);
    }

    [Test]
    public async Task GetProjectById_ReturnsProject_WhenExists()
    {
        // Seed test data
        _factory.TestDataStore.SeedProject(new Project { Id = "test-id", Name = "Test" });

        var response = await _client.GetAsync("/api/projects/test-id");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }
}
```

### End-to-End Tests (Homespun.E2E.Tests)

**Framework:** NUnit + Playwright

E2E tests run against the full application stack using Playwright for browser automation:

```csharp
[TestFixture]
public class CriticalJourneysTests : PageTest
{
    private string BaseUrl => HomespunFixture.BaseUrl;

    [Test]
    public async Task HomePage_LoadsSuccessfully()
    {
        await Page.GotoAsync(BaseUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Expect(Page).ToHaveTitleAsync(new Regex("Homespun"));
    }

    [Test]
    public async Task Navigation_WorksBetweenPages()
    {
        await Page.GotoAsync(BaseUrl);
        var projectsLink = Page.Locator("a[href*='projects']").First;
        await projectsLink.ClickAsync();
        Assert.That(Page.Url, Does.Contain("projects"));
    }
}
```

**E2E Configuration:**
- `HomespunFixture` automatically starts the application for tests
- Set `E2E_BASE_URL` to test against an external server
- Set `E2E_CONFIGURATION` to specify build configuration (Release/Debug)

### Data Storage

- JSON file storage
- Data file: `homespun-data.json` (stored in `.homespun` directory)

### Creating Migrations

*Not applicable for JSON storage.*

## Key Services

### Fleece (Features/Fleece/)
- **FleeceService**: Project-aware wrapper around Fleece.Core IIssueService for CRUD operations on issues
- **FleeceIssueTransitionService**: Issue status workflow transitions

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
- **IssuePrLinkingService**: Links Fleece issues to GitHub PRs

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

## UI Development with Mock Mode

### Overview

The mock mode provides a development environment with:
- Pre-seeded demo data (projects, features, issues)
- No external dependencies (GitHub, Claude API)
- Isolated from production data

### Starting Mock Mode

```bash
# Using script
./scripts/mock.sh       # Linux/Mac
./scripts/mock.ps1      # Windows

# Or directly
dotnet run --project src/Homespun --launch-profile mock
```

**Default URLs (from launchSettings.json):**
- HTTPS: https://localhost:5094
- HTTP: http://localhost:5095

**Important:** In containerized or CI environments, the `HTTP_PORTS`/`HTTPS_PORTS` environment variables may override the launch profile URLs. Check the console output for the actual listening URL:
```
Now listening on: http://localhost:5093
```

When using Playwright MCP tools in such environments, use the HTTP URL shown in the console output rather than the HTTPS URL from the launch profile.

### Visual UI Development with Playwright

Use the `/ui-dev` skill for browser-assisted UI development:

```
/ui-dev
```

This provides:
- Playwright MCP tools for screenshots and interaction
- Guidance for the visual iteration workflow
- Mock server management

### Playwright MCP Tools

Key tools for UI inspection:
- `browser_navigate` - Navigate to URLs
- `browser_take_screenshot` - Capture visual state
- `browser_snapshot` - Get accessibility tree
- `browser_click` / `browser_type` - Interact with elements
- `browser_console_messages` - Check for JS errors

### Workflow Example: Visual UI Iteration

1. **Start the mock server:**
   ```bash
   cd src/Homespun
   dotnet build
   HOMESPUN_MOCK_MODE=true dotnet run --no-build &
   ```

2. **Wait for server startup and verify:**
   ```bash
   sleep 10
   curl -s http://localhost:5093/health  # Check if server is healthy
   ```

3. **Navigate using Playwright MCP:**
   ```
   browser_navigate to http://localhost:5093/projects/demo-project
   ```

   **Note:** Use `http://` not `https://` when running in environments where HTTPS is not available or certificates are not set up.

4. **Take screenshots to verify visual changes:**
   ```
   browser_take_screenshot with filename "my-feature.png"
   ```

5. **Make CSS/component changes, then refresh the page to see updates**

6. **Stop the server when done:**
   ```bash
   pkill -f "dotnet run"
   ```

### Environment Variables

- `HOMESPUN_MOCK_MODE=true`: Activates mock services
- `ASPNETCORE_ENVIRONMENT=Development`: Enables dev tooling
