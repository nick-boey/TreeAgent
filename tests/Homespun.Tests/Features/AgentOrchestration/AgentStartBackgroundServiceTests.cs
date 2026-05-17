using Fleece.Core.Models;
using Homespun.Features.AgentOrchestration.Services;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Git;
using Homespun.Features.Notifications;
using Homespun.Shared.Models.Fleece;
using Homespun.Shared.Models.Sessions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using AgentStartRequest = Homespun.Features.AgentOrchestration.Services.AgentStartRequest;

namespace Homespun.Tests.Features.AgentOrchestration;

[TestFixture]
public class AgentStartBackgroundServiceTests
{
    private Mock<IServiceProvider> _mockServiceProvider = null!;
    private Mock<IServiceScope> _mockServiceScope = null!;
    private Mock<IServiceScopeFactory> _mockServiceScopeFactory = null!;
    private Mock<IGitCloneService> _mockCloneService = null!;
    private Mock<IClaudeSessionService> _mockSessionService = null!;
    private Mock<ISkillDiscoveryService> _mockSkillDiscovery = null!;
    private Mock<IFleeceIssuesSyncService> _mockIssuesSyncService = null!;
    private Mock<IBaseBranchResolver> _mockBaseBranchResolver = null!;
    private Mock<IHubContext<NotificationHub>> _mockHubContext = null!;
    private Mock<IHubClients> _mockHubClients = null!;
    private Mock<IClientProxy> _mockClientProxy = null!;
    private Mock<IAgentStartupTracker> _mockStartupTracker = null!;
    private Mock<ILogger<AgentStartBackgroundService>> _mockLogger = null!;
    private AgentStartBackgroundService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _mockServiceProvider = new Mock<IServiceProvider>();
        _mockServiceScope = new Mock<IServiceScope>();
        _mockServiceScopeFactory = new Mock<IServiceScopeFactory>();
        _mockCloneService = new Mock<IGitCloneService>();
        _mockSessionService = new Mock<IClaudeSessionService>();
        _mockSkillDiscovery = new Mock<ISkillDiscoveryService>();
        _mockIssuesSyncService = new Mock<IFleeceIssuesSyncService>();
        _mockBaseBranchResolver = new Mock<IBaseBranchResolver>();
        _mockHubContext = new Mock<IHubContext<NotificationHub>>();
        _mockHubClients = new Mock<IHubClients>();
        _mockClientProxy = new Mock<IClientProxy>();
        _mockStartupTracker = new Mock<IAgentStartupTracker>();
        _mockLogger = new Mock<ILogger<AgentStartBackgroundService>>();

        // Setup service scope
        var scopedServiceProvider = new Mock<IServiceProvider>();
        scopedServiceProvider.Setup(x => x.GetService(typeof(IGitCloneService)))
            .Returns(_mockCloneService.Object);
        scopedServiceProvider.Setup(x => x.GetService(typeof(IClaudeSessionService)))
            .Returns(_mockSessionService.Object);
        scopedServiceProvider.Setup(x => x.GetService(typeof(ISkillDiscoveryService)))
            .Returns(_mockSkillDiscovery.Object);
        scopedServiceProvider.Setup(x => x.GetService(typeof(IFleeceIssuesSyncService)))
            .Returns(_mockIssuesSyncService.Object);
        scopedServiceProvider.Setup(x => x.GetService(typeof(IBaseBranchResolver)))
            .Returns(_mockBaseBranchResolver.Object);
        scopedServiceProvider.Setup(x => x.GetService(typeof(IHubContext<NotificationHub>)))
            .Returns(_mockHubContext.Object);

        _mockServiceScope.Setup(x => x.ServiceProvider).Returns(scopedServiceProvider.Object);
        _mockServiceScopeFactory.Setup(x => x.CreateScope()).Returns(_mockServiceScope.Object);
        _mockServiceProvider.Setup(x => x.GetService(typeof(IServiceScopeFactory)))
            .Returns(_mockServiceScopeFactory.Object);

        // Setup hub context
        _mockHubContext.Setup(x => x.Clients).Returns(_mockHubClients.Object);
        _mockHubClients.Setup(x => x.All).Returns(_mockClientProxy.Object);
        _mockHubClients.Setup(x => x.Group(It.IsAny<string>())).Returns(_mockClientProxy.Object);

        // Default setup for base branch resolver - returns default branch
        _mockBaseBranchResolver.Setup(x => x.ResolveBaseBranchAsync(
                It.IsAny<AgentStartRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentStartRequest req, CancellationToken ct) =>
                new BaseBranchResolutionResult(req.BaseBranch ?? req.ProjectDefaultBranch, null));

        // Skill discovery returns null unless a specific test overrides it
        _mockSkillDiscovery.Setup(x => x.GetSkillAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SkillDescriptor?)null);

        _service = new AgentStartBackgroundService(
            _mockServiceProvider.Object,
            _mockStartupTracker.Object,
            _mockLogger.Object);
    }

    private AgentStartRequest CreateTestRequest(string issueId = "issue123", string projectId = "proj123")
    {
        var ts = DateTimeOffset.UtcNow;
        return new AgentStartRequest
        {
            IssueId = issueId,
            ProjectId = projectId,
            ProjectLocalPath = "/path/to/project",
            ProjectDefaultBranch = "main",
            Issue = new Issue
            {
                Id = issueId,
                Title = "Test Issue",
                Status = IssueStatus.Progress,
                Type = IssueType.Task,
                CreatedAt = ts,
                LastUpdate = ts
            },
            SkillName = null,
            BaseBranch = null,
            Model = "sonnet",
            BranchName = "task/test-issue+issue123",
            Mode = SessionMode.Build
        };
    }

    #region QueueAgentStartAsync Tests

    [Test]
    public async Task QueueAgentStartAsync_CreatesCloneWhenNotExists_AndStartsSession()
    {
        // Arrange
        var request = CreateTestRequest();
        var clonePath = "/path/to/clone";
        var session = new ClaudeSession
        {
            Id = "session123",
            EntityId = request.IssueId,
            ProjectId = request.ProjectId,
            WorkingDirectory = clonePath,
            Model = "sonnet",
            Mode = SessionMode.Build,
            Status = ClaudeSessionStatus.Running
        };

        _mockCloneService.Setup(x => x.GetClonePathForBranchAsync(
                request.ProjectLocalPath, request.BranchName))
            .ReturnsAsync((string?)null);

        _mockIssuesSyncService.Setup(x => x.PullFleeceOnlyAsync(
                request.ProjectLocalPath, "main", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FleecePullResult(Success: true, ErrorMessage: null, IssuesMerged: 0, WasBehindRemote: false, CommitsPulled: 0));

        _mockCloneService.Setup(x => x.CreateCloneAsync(
                request.ProjectLocalPath, request.BranchName, true, "main"))
            .ReturnsAsync(clonePath);

        _mockSessionService.Setup(x => x.StartSessionAsync(
                request.IssueId, request.ProjectId, clonePath,
                SessionMode.Build, "sonnet", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        // Act
        await _service.QueueAgentStartAsync(request);
        await Task.Delay(200); // Wait for background task

        // Assert
        _mockCloneService.Verify(x => x.CreateCloneAsync(
            request.ProjectLocalPath, request.BranchName, true, "main"), Times.Once);

        _mockSessionService.Verify(x => x.StartSessionAsync(
            request.IssueId, request.ProjectId, clonePath,
            SessionMode.Build, "sonnet", null, It.IsAny<CancellationToken>()), Times.Once);

        _mockStartupTracker.Verify(x => x.MarkAsStarted(request.IssueId), Times.Once);

        // Verify AgentStarting notification was sent (twice: once to All, once to Group)
        _mockClientProxy.Verify(x => x.SendCoreAsync(
            "AgentStarting",
            It.Is<object?[]>(args =>
                args[0]!.Equals(request.IssueId) &&
                args[1]!.Equals(request.ProjectId) &&
                args[2]!.Equals(request.BranchName)),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Test]
    public async Task QueueAgentStartAsync_UsesExistingClone_WhenCloneExists()
    {
        // Arrange
        var request = CreateTestRequest() with { Mode = SessionMode.Plan };
        var existingClonePath = "/path/to/existing/clone";
        var session = new ClaudeSession
        {
            Id = "session123",
            EntityId = request.IssueId,
            ProjectId = request.ProjectId,
            WorkingDirectory = existingClonePath,
            Model = "sonnet",
            Mode = SessionMode.Plan,
            Status = ClaudeSessionStatus.Running
        };

        _mockCloneService.Setup(x => x.GetClonePathForBranchAsync(
                request.ProjectLocalPath, request.BranchName))
            .ReturnsAsync(existingClonePath);

        _mockSessionService.Setup(x => x.StartSessionAsync(
                request.IssueId, request.ProjectId, existingClonePath,
                SessionMode.Plan, "sonnet", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        // Act
        await _service.QueueAgentStartAsync(request);
        await Task.Delay(200);

        // Assert
        _mockCloneService.Verify(x => x.CreateCloneAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
            It.IsAny<string>()), Times.Never);

        _mockSessionService.Verify(x => x.StartSessionAsync(
            request.IssueId, request.ProjectId, existingClonePath,
            SessionMode.Plan, "sonnet", null, It.IsAny<CancellationToken>()), Times.Once);

        _mockStartupTracker.Verify(x => x.MarkAsStarted(request.IssueId), Times.Once);
    }

    [Test]
    public async Task QueueAgentStartAsync_BroadcastsFailure_WhenCloneCreationFails()
    {
        // Arrange
        var request = CreateTestRequest();

        _mockCloneService.Setup(x => x.GetClonePathForBranchAsync(
                request.ProjectLocalPath, request.BranchName))
            .ReturnsAsync((string?)null);

        _mockIssuesSyncService.Setup(x => x.PullFleeceOnlyAsync(
                request.ProjectLocalPath, "main", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FleecePullResult(Success: true, ErrorMessage: null, IssuesMerged: 0, WasBehindRemote: false, CommitsPulled: 0));

        _mockCloneService.Setup(x => x.CreateCloneAsync(
                request.ProjectLocalPath, request.BranchName, true, "main"))
            .ReturnsAsync((string?)null); // Clone creation fails

        // Act
        await _service.QueueAgentStartAsync(request);
        await Task.Delay(200);

        // Assert
        _mockStartupTracker.Verify(x => x.MarkAsFailed(
            request.IssueId, It.IsAny<string>()), Times.Once);

        // Verify AgentStartFailed notification was sent (twice: once to All, once to Group)
        _mockClientProxy.Verify(x => x.SendCoreAsync(
            "AgentStartFailed",
            It.Is<object?[]>(args =>
                args[0]!.Equals(request.IssueId) &&
                args[1]!.Equals(request.ProjectId)),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Test]
    public async Task QueueAgentStartAsync_BroadcastsFailure_WhenSessionCreationFails()
    {
        // Arrange
        var request = CreateTestRequest();
        var clonePath = "/path/to/clone";

        _mockCloneService.Setup(x => x.GetClonePathForBranchAsync(
                request.ProjectLocalPath, request.BranchName))
            .ReturnsAsync(clonePath);

        _mockSessionService.Setup(x => x.StartSessionAsync(
                request.IssueId, request.ProjectId, clonePath,
                SessionMode.Build, "sonnet", null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Session creation failed"));

        // Act
        await _service.QueueAgentStartAsync(request);
        await Task.Delay(200);

        // Assert
        _mockStartupTracker.Verify(x => x.MarkAsFailed(
            request.IssueId, "Session creation failed"), Times.Once);

        // Verify AgentStartFailed notification was sent (twice: once to All, once to Group)
        _mockClientProxy.Verify(x => x.SendCoreAsync(
            "AgentStartFailed",
            It.Is<object?[]>(args =>
                args[0]!.Equals(request.IssueId) &&
                args[1]!.Equals(request.ProjectId) &&
                args[2]!.Equals("Session creation failed")),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Test]
    public async Task QueueAgentStartAsync_SkipsDuplicate_WhenAlreadyStarting()
    {
        // Arrange
        var request = CreateTestRequest() with { Mode = SessionMode.Plan };
        var clonePath = "/path/to/clone";
        var session = new ClaudeSession
        {
            Id = "session123",
            EntityId = request.IssueId,
            ProjectId = request.ProjectId,
            WorkingDirectory = clonePath,
            Model = "sonnet",
            Mode = SessionMode.Plan,
            Status = ClaudeSessionStatus.Running
        };

        _mockCloneService.Setup(x => x.GetClonePathForBranchAsync(
                request.ProjectLocalPath, request.BranchName))
            .Returns(async () =>
            {
                await Task.Delay(200); // Simulate slow operation
                return clonePath;
            });

        _mockSessionService.Setup(x => x.StartSessionAsync(
                request.IssueId, request.ProjectId, clonePath,
                SessionMode.Plan, "sonnet", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        // Act - Queue twice in quick succession
        await _service.QueueAgentStartAsync(request);
        await _service.QueueAgentStartAsync(request); // Should be ignored as duplicate

        await Task.Delay(400); // Wait for first task to complete

        // Assert - Only one session creation
        _mockSessionService.Verify(x => x.StartSessionAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<SessionMode>(), It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task QueueAgentStartAsync_DefaultsToPlanMode_WhenNoSkillOrModeProvided()
    {
        // Arrange
        var request = CreateTestRequest() with { Mode = null, SkillName = null };
        var clonePath = "/path/to/clone";
        var session = new ClaudeSession
        {
            Id = "session123",
            EntityId = request.IssueId,
            ProjectId = request.ProjectId,
            WorkingDirectory = clonePath,
            Model = "sonnet",
            Mode = SessionMode.Plan,
            Status = ClaudeSessionStatus.Running
        };

        _mockCloneService.Setup(x => x.GetClonePathForBranchAsync(
                request.ProjectLocalPath, request.BranchName))
            .ReturnsAsync(clonePath);

        _mockSessionService.Setup(x => x.StartSessionAsync(
                request.IssueId, request.ProjectId, clonePath,
                SessionMode.Plan, "sonnet", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        // Act
        await _service.QueueAgentStartAsync(request);
        await Task.Delay(200);

        // Assert - Session should be created with Plan mode
        _mockSessionService.Verify(x => x.StartSessionAsync(
            request.IssueId, request.ProjectId, clonePath,
            SessionMode.Plan, "sonnet", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task QueueAgentStartAsync_UsesSpecifiedBaseBranch_WhenProvided()
    {
        // Arrange
        var request = CreateTestRequest() with { BaseBranch = "develop", Mode = SessionMode.Plan };
        var clonePath = "/path/to/clone";
        var session = new ClaudeSession
        {
            Id = "session123",
            EntityId = request.IssueId,
            ProjectId = request.ProjectId,
            WorkingDirectory = clonePath,
            Model = "sonnet",
            Mode = SessionMode.Plan,
            Status = ClaudeSessionStatus.Running
        };

        _mockCloneService.Setup(x => x.GetClonePathForBranchAsync(
                request.ProjectLocalPath, request.BranchName))
            .ReturnsAsync((string?)null);

        _mockIssuesSyncService.Setup(x => x.PullFleeceOnlyAsync(
                request.ProjectLocalPath, "develop", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FleecePullResult(Success: true, ErrorMessage: null, IssuesMerged: 0, WasBehindRemote: false, CommitsPulled: 0));

        _mockCloneService.Setup(x => x.CreateCloneAsync(
                request.ProjectLocalPath, request.BranchName, true, "develop"))
            .ReturnsAsync(clonePath);

        _mockSessionService.Setup(x => x.StartSessionAsync(
                request.IssueId, request.ProjectId, clonePath,
                SessionMode.Plan, "sonnet", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        // Act
        await _service.QueueAgentStartAsync(request);
        await Task.Delay(200);

        // Assert - Clone should be created from "develop" branch
        _mockCloneService.Verify(x => x.CreateCloneAsync(
            request.ProjectLocalPath, request.BranchName, true, "develop"), Times.Once);
    }

    [Test]
    public async Task QueueAgentStartAsync_BroadcastsFailure_WhenBlockedByOpenIssues()
    {
        // Arrange
        var request = CreateTestRequest();

        // Setup resolver to return an error indicating blocking issues
        _mockBaseBranchResolver.Setup(x => x.ResolveBaseBranchAsync(
                It.IsAny<AgentStartRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BaseBranchResolutionResult(null, "Cannot start agent: blocked by open issues: child123, sibling456"));

        // Act
        await _service.QueueAgentStartAsync(request);
        await Task.Delay(200);

        // Assert - Should mark as failed with blocking error
        _mockStartupTracker.Verify(x => x.MarkAsFailed(
            request.IssueId, It.Is<string>(s => s.Contains("blocked by open issues"))), Times.Once);

        // Verify AgentStartFailed notification was sent (twice: once to All, once to Group)
        _mockClientProxy.Verify(x => x.SendCoreAsync(
            "AgentStartFailed",
            It.Is<object?[]>(args =>
                args[0]!.Equals(request.IssueId) &&
                args[1]!.Equals(request.ProjectId) &&
                ((string)args[2]!).Contains("blocked by open issues")),
            It.IsAny<CancellationToken>()), Times.Exactly(2));

        // Verify no clone creation was attempted
        _mockCloneService.Verify(x => x.CreateCloneAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>()), Times.Never);

        // Verify no session creation was attempted
        _mockSessionService.Verify(x => x.StartSessionAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<SessionMode>(), It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task QueueAgentStartAsync_UsesStackedPrBranch_WhenResolverReturnsIt()
    {
        // Arrange
        var request = CreateTestRequest() with { Mode = SessionMode.Plan };
        var clonePath = "/path/to/clone";
        var stackedBranch = "task/prior-issue+prior123";
        var session = new ClaudeSession
        {
            Id = "session123",
            EntityId = request.IssueId,
            ProjectId = request.ProjectId,
            WorkingDirectory = clonePath,
            Model = "sonnet",
            Mode = SessionMode.Plan,
            Status = ClaudeSessionStatus.Running
        };

        // Setup resolver to return a stacked PR branch
        _mockBaseBranchResolver.Setup(x => x.ResolveBaseBranchAsync(
                It.IsAny<AgentStartRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BaseBranchResolutionResult(stackedBranch, null));

        _mockCloneService.Setup(x => x.GetClonePathForBranchAsync(
                request.ProjectLocalPath, request.BranchName))
            .ReturnsAsync((string?)null);

        _mockIssuesSyncService.Setup(x => x.PullFleeceOnlyAsync(
                request.ProjectLocalPath, stackedBranch, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FleecePullResult(Success: true, ErrorMessage: null, IssuesMerged: 0, WasBehindRemote: false, CommitsPulled: 0));

        _mockCloneService.Setup(x => x.CreateCloneAsync(
                request.ProjectLocalPath, request.BranchName, true, stackedBranch))
            .ReturnsAsync(clonePath);

        _mockSessionService.Setup(x => x.StartSessionAsync(
                request.IssueId, request.ProjectId, clonePath,
                SessionMode.Plan, "sonnet", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        // Act
        await _service.QueueAgentStartAsync(request);
        await Task.Delay(200);

        // Assert - Clone should be created from the stacked PR branch
        _mockCloneService.Verify(x => x.CreateCloneAsync(
            request.ProjectLocalPath, request.BranchName, true, stackedBranch), Times.Once);

        _mockStartupTracker.Verify(x => x.MarkAsStarted(request.IssueId), Times.Once);
    }

    [Test]
    public async Task QueueAgentStartAsync_UsesUserInstructions_WhenProvided()
    {
        // Arrange
        var request = CreateTestRequest() with
        {
            UserInstructions = "Custom user instructions for the agent"
        };
        var clonePath = "/path/to/clone";
        var session = new ClaudeSession
        {
            Id = "session123",
            EntityId = request.IssueId,
            ProjectId = request.ProjectId,
            WorkingDirectory = clonePath,
            Model = "sonnet",
            Mode = SessionMode.Build,
            Status = ClaudeSessionStatus.Running
        };

        _mockCloneService.Setup(x => x.GetClonePathForBranchAsync(
                request.ProjectLocalPath, request.BranchName))
            .ReturnsAsync(clonePath);

        _mockSessionService.Setup(x => x.StartSessionAsync(
                request.IssueId, request.ProjectId, clonePath,
                SessionMode.Build, "sonnet", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        // Act
        await _service.QueueAgentStartAsync(request);
        await Task.Delay(200);

        // Assert - Session should use Build mode as requested
        _mockSessionService.Verify(x => x.StartSessionAsync(
            request.IssueId, request.ProjectId, clonePath,
            SessionMode.Build, "sonnet", null, It.IsAny<CancellationToken>()), Times.Once);

        _mockStartupTracker.Verify(x => x.MarkAsStarted(request.IssueId), Times.Once);
    }

    [Test]
    public async Task QueueAgentStartAsync_UsesSkillMode_WhenSkillProvidedAndNoExplicitMode()
    {
        // Arrange
        var request = CreateTestRequest() with
        {
            Mode = null,
            SkillName = "fix-bug"
        };
        var clonePath = "/path/to/clone";
        var session = new ClaudeSession
        {
            Id = "session123",
            EntityId = request.IssueId,
            ProjectId = request.ProjectId,
            WorkingDirectory = clonePath,
            Model = "sonnet",
            Mode = SessionMode.Build,
            Status = ClaudeSessionStatus.Running
        };

        _mockCloneService.Setup(x => x.GetClonePathForBranchAsync(
                request.ProjectLocalPath, request.BranchName))
            .ReturnsAsync(clonePath);

        _mockSkillDiscovery.Setup(x => x.GetSkillAsync(
                clonePath, "fix-bug", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SkillDescriptor
            {
                Name = "fix-bug",
                Category = SkillCategory.Homespun,
                Mode = SessionMode.Build,
                SkillBody = "Fix the bug.",
            });

        _mockSessionService.Setup(x => x.StartSessionAsync(
                request.IssueId, request.ProjectId, clonePath,
                SessionMode.Build, "sonnet", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        // Act
        await _service.QueueAgentStartAsync(request);
        await Task.Delay(200);

        // Assert - Session should use Build mode derived from the skill
        _mockSessionService.Verify(x => x.StartSessionAsync(
            request.IssueId, request.ProjectId, clonePath,
            SessionMode.Build, "sonnet", null, It.IsAny<CancellationToken>()), Times.Once);

        _mockStartupTracker.Verify(x => x.MarkAsStarted(request.IssueId), Times.Once);
    }

    [Test]
    public async Task QueueAgentStartAsync_PassesSystemPromptOverride_WhenProvided()
    {
        // Arrange
        var request = CreateTestRequest() with
        {
            SystemPromptOverride = "Use openspec-integration schema: custom-schema"
        };
        var clonePath = "/path/to/clone";
        var session = new ClaudeSession
        {
            Id = "session123",
            EntityId = request.IssueId,
            ProjectId = request.ProjectId,
            WorkingDirectory = clonePath,
            Model = "sonnet",
            Mode = SessionMode.Build,
            Status = ClaudeSessionStatus.Running
        };

        _mockCloneService.Setup(x => x.GetClonePathForBranchAsync(
                request.ProjectLocalPath, request.BranchName))
            .ReturnsAsync(clonePath);

        _mockSessionService.Setup(x => x.StartSessionAsync(
                request.IssueId, request.ProjectId, clonePath,
                SessionMode.Build, "sonnet",
                "Use openspec-integration schema: custom-schema",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        // Act
        await _service.QueueAgentStartAsync(request);
        await Task.Delay(200);

        // Assert
        _mockSessionService.Verify(x => x.StartSessionAsync(
            request.IssueId, request.ProjectId, clonePath,
            SessionMode.Build, "sonnet",
            "Use openspec-integration schema: custom-schema",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region ComposeInitialMessage Tests (3.4, 3.5, 3.6 coverage)

    [Test]
    public void ComposeInitialMessage_ReturnsNull_WhenNoSkillAndNoUserInstructions()
    {
        var msg = AgentStartBackgroundService.ComposeInitialMessage(
            skill: null, args: null, userInstructions: null);

        Assert.That(msg, Is.Null);
    }

    [Test]
    public void ComposeInitialMessage_ReturnsNull_WhenAllInputsBlank()
    {
        var msg = AgentStartBackgroundService.ComposeInitialMessage(
            skill: new SkillDescriptor { SkillBody = "   " },
            args: new Dictionary<string, string> { ["x"] = "  " },
            userInstructions: "");

        Assert.That(msg, Is.Null);
    }

    [Test]
    public void ComposeInitialMessage_ReturnsUserInstructionsOnly_WhenNoSkill()
    {
        var msg = AgentStartBackgroundService.ComposeInitialMessage(
            skill: null,
            args: null,
            userInstructions: "Please fix it");

        Assert.That(msg, Is.EqualTo("Please fix it"));
    }

    [Test]
    public void ComposeInitialMessage_ComposesSkillBodyWithArgs_ForHomespunSkill()
    {
        var skill = new SkillDescriptor
        {
            Name = "fix-bug",
            Category = SkillCategory.Homespun,
            SkillBody = "Fix the bug described in the issue.",
        };
        var args = new Dictionary<string, string> { ["issue-id"] = "abc123" };

        var msg = AgentStartBackgroundService.ComposeInitialMessage(
            skill, args, userInstructions: null);

        Assert.That(msg, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(msg, Does.StartWith("Fix the bug described in the issue."));
            Assert.That(msg, Does.Contain("## Args"));
            Assert.That(msg, Does.Contain("issue-id: abc123"));
        });
    }

    [Test]
    public void ComposeInitialMessage_IncludesChangeNameArg_ForOpenSpecSkill()
    {
        var skill = new SkillDescriptor
        {
            Name = "openspec-apply-change",
            Category = SkillCategory.OpenSpec,
            SkillBody = "Implement tasks from an OpenSpec change.",
        };
        var args = new Dictionary<string, string> { ["change-name"] = "skills-catalogue" };

        var msg = AgentStartBackgroundService.ComposeInitialMessage(
            skill, args, userInstructions: null);

        Assert.That(msg, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(msg, Does.StartWith("Implement tasks from an OpenSpec change."));
            Assert.That(msg, Does.Contain("change-name: skills-catalogue"));
        });
    }

    [Test]
    public void ComposeInitialMessage_AppendsUserInstructions_AfterSkillBody()
    {
        var skill = new SkillDescriptor
        {
            Name = "fix-bug",
            Category = SkillCategory.Homespun,
            SkillBody = "Fix the bug.",
        };

        var msg = AgentStartBackgroundService.ComposeInitialMessage(
            skill, args: null, userInstructions: "Extra context from user");

        Assert.That(msg, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(msg, Does.StartWith("Fix the bug."));
            Assert.That(msg, Does.EndWith("Extra context from user"));
        });
    }

    [Test]
    public void ComposeInitialMessage_SkipsArgsWithBlankValues()
    {
        var skill = new SkillDescriptor
        {
            Name = "my-skill",
            SkillBody = "Body",
        };
        var args = new Dictionary<string, string>
        {
            ["a"] = "1",
            ["b"] = "",
            ["c"] = "   ",
            ["d"] = "4",
        };

        var msg = AgentStartBackgroundService.ComposeInitialMessage(
            skill, args, userInstructions: null);

        Assert.That(msg, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(msg, Does.Contain("a: 1"));
            Assert.That(msg, Does.Not.Contain("b:"));
            Assert.That(msg, Does.Not.Contain("c:"));
            Assert.That(msg, Does.Contain("d: 4"));
        });
    }

    [Test]
    public void ComposeInitialMessage_ReturnsSkillBodyOnly_WhenNoArgsAndNoUserInstructions()
    {
        var skill = new SkillDescriptor
        {
            Name = "my-skill",
            SkillBody = "Just the body.",
        };

        var msg = AgentStartBackgroundService.ComposeInitialMessage(
            skill, args: null, userInstructions: null);

        Assert.That(msg, Is.EqualTo("Just the body."));
    }

    #endregion
}
