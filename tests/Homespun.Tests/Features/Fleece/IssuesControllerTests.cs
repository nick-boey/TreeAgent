using Fleece.Core.Models;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Fleece.Controllers;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Git;
using Homespun.Features.Notifications;
using Homespun.Features.OpenSpec.Services;
using Homespun.Features.Projects;
using Homespun.Features.PullRequests.Data;
using Homespun.Features.AgentOrchestration.Services;
using Homespun.Shared.Models.Fleece;
using Homespun.Shared.Models.Issues;
using Homespun.Shared.Models.Projects;
using Homespun.Shared.Models.Sessions;
using Homespun.Shared.Requests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using AgentStartRequest = Homespun.Features.AgentOrchestration.Services.AgentStartRequest;

namespace Homespun.Tests.Features.Fleece;

/// <summary>
/// Unit tests for IssuesController SignalR broadcasting.
/// </summary>
[TestFixture]
public class IssuesControllerTests
{
    private Mock<IProjectFleeceService> _fleeceServiceMock = null!;
    private Mock<IProjectService> _projectServiceMock = null!;
    private Mock<IDataStore> _dataStoreMock = null!;
    private Mock<IHubContext<NotificationHub>> _notificationHubMock = null!;
    private Mock<IIssueBranchResolverService> _branchResolverServiceMock = null!;
    private Mock<IClaudeSessionService> _sessionServiceMock = null!;

    private Mock<IGitCloneService> _cloneServiceMock = null!;
    private Mock<IBranchIdBackgroundService> _branchIdBackgroundServiceMock = null!;
    private Mock<IFleeceChangeApplicationService> _changeApplicationServiceMock = null!;
    private Mock<IFleeceIssuesSyncService> _fleeceIssuesSyncServiceMock = null!;
    private Mock<IAgentStartBackgroundService> _agentStartBackgroundServiceMock = null!;
    private Mock<IAgentStartupTracker> _agentStartupTrackerMock = null!;
    private Mock<IIssueAncestorTraversalService> _ancestorTraversalMock = null!;
    private Mock<IModelCatalogService> _modelCatalogMock = null!;
    private Mock<IPhaseDispatchGuard> _phaseDispatchGuardMock = null!;
    private Mock<ILogger<IssuesController>> _loggerMock = null!;
    private Mock<IHubClients> _clientsMock = null!;
    private Mock<IClientProxy> _allClientsMock = null!;
    private Mock<IClientProxy> _groupClientsMock = null!;
    private IssuesController _controller = null!;

    private static readonly Project TestProject = new()
    {
        Id = "project-123",
        Name = "Test Project",
        LocalPath = "/path/to/project",
        DefaultBranch = "main"
    };

    private static Issue CreateTestIssue(string id, string title, IssueType type = IssueType.Task) => new()
    {
        Id = id,
        Title = title,
        Type = type,
        Status = IssueStatus.Open,
        CreatedAt = DateTimeOffset.UtcNow,
        LastUpdate = DateTimeOffset.UtcNow
    };

    [SetUp]
    public void SetUp()
    {
        _fleeceServiceMock = new Mock<IProjectFleeceService>();
        _projectServiceMock = new Mock<IProjectService>();
        _dataStoreMock = new Mock<IDataStore>();
        _notificationHubMock = new Mock<IHubContext<NotificationHub>>();
        _branchResolverServiceMock = new Mock<IIssueBranchResolverService>();
        _sessionServiceMock = new Mock<IClaudeSessionService>();

        _cloneServiceMock = new Mock<IGitCloneService>();
        _branchIdBackgroundServiceMock = new Mock<IBranchIdBackgroundService>();
        _changeApplicationServiceMock = new Mock<IFleeceChangeApplicationService>();
        _fleeceIssuesSyncServiceMock = new Mock<IFleeceIssuesSyncService>();
        _agentStartBackgroundServiceMock = new Mock<IAgentStartBackgroundService>();
        _agentStartupTrackerMock = new Mock<IAgentStartupTracker>();
        _ancestorTraversalMock = new Mock<IIssueAncestorTraversalService>();
        _modelCatalogMock = new Mock<IModelCatalogService>();
        _modelCatalogMock
            .Setup(m => m.ResolveModelIdAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string? requested, CancellationToken _) => requested ?? "claude-default");
        _phaseDispatchGuardMock = new Mock<IPhaseDispatchGuard>();
        _phaseDispatchGuardMock
            .Setup(g => g.GetBlockingPhasesAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());
        _loggerMock = new Mock<ILogger<IssuesController>>();
        _clientsMock = new Mock<IHubClients>();
        _allClientsMock = new Mock<IClientProxy>();
        _groupClientsMock = new Mock<IClientProxy>();

        _notificationHubMock.Setup(x => x.Clients).Returns(_clientsMock.Object);
        _clientsMock.Setup(x => x.All).Returns(_allClientsMock.Object);
        _clientsMock.Setup(x => x.Group(It.IsAny<string>())).Returns(_groupClientsMock.Object);

        // Default: allow marking as starting
        _agentStartupTrackerMock.Setup(x => x.TryMarkAsStarting(It.IsAny<string>()))
            .Returns(true);

        _controller = new IssuesController(
            _fleeceServiceMock.Object,
            _projectServiceMock.Object,
            _dataStoreMock.Object,
            _notificationHubMock.Object,
            _branchResolverServiceMock.Object,
            _sessionServiceMock.Object,
            _cloneServiceMock.Object,
            _branchIdBackgroundServiceMock.Object,
            _changeApplicationServiceMock.Object,
            _fleeceIssuesSyncServiceMock.Object,
            _agentStartBackgroundServiceMock.Object,
            _agentStartupTrackerMock.Object,
            _ancestorTraversalMock.Object,
            _modelCatalogMock.Object,
            _phaseDispatchGuardMock.Object,
            NullLogger<IssuesController>.Instance);

        // Set up HTTP context for controller. RequestServices is consulted by
        // NotificationHubExtensions.BroadcastIssueTopologyChanged to resolve
        // IProjectTaskGraphSnapshotStore / ITaskGraphSnapshotRefresher, so an
        // empty provider is required even when those services are unregistered.
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                RequestServices = new ServiceCollection().BuildServiceProvider()
            }
        };
    }

    #region Create Tests

    [Test]
    public async Task Create_BroadcastsIssueChanged_WithCreatedChangeType()
    {
        // Arrange
        var issue = CreateTestIssue("ABC123", "Test Issue");

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _fleeceServiceMock
            .Setup(x => x.CreateIssueAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IssueType>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<ExecutionMode?>(),
                It.IsAny<IssueStatus?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(issue);

        var request = new CreateIssueRequest { ProjectId = TestProject.Id, Title = "Test Issue" };

        // Act
        await _controller.Create(request);

        // Assert — the unified IssueChanged event carries the canonical body.
        _groupClientsMock.Verify(
            x => x.SendCoreAsync("IssueChanged",
                It.Is<object?[]>(args =>
                    args.Length == 4 &&
                    (string)args[0]! == TestProject.Id &&
                    (IssueChangeType)args[1]! == IssueChangeType.Created &&
                    (string)args[2]! == issue.Id &&
                    args[3] is IssueResponse),
                default),
            Times.Once);
    }

    [Test]
    public async Task Create_ReturnsCreatedResult()
    {
        // Arrange
        var issue = CreateTestIssue("ABC123", "Test Issue");

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _fleeceServiceMock
            .Setup(x => x.CreateIssueAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IssueType>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<ExecutionMode?>(),
                It.IsAny<IssueStatus?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(issue);

        var request = new CreateIssueRequest { ProjectId = TestProject.Id, Title = "Test Issue" };

        // Act
        var result = await _controller.Create(request);

        // Assert
        Assert.That(result.Result, Is.TypeOf<CreatedAtActionResult>());
    }

    [Test]
    public async Task Create_ProjectNotFound_DoesNotBroadcast()
    {
        // Arrange
        _projectServiceMock
            .Setup(x => x.GetByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((Project?)null);

        var request = new CreateIssueRequest { ProjectId = "nonexistent", Title = "Test Issue" };

        // Act
        await _controller.Create(request);

        // Assert
        _allClientsMock.Verify(
            x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), default),
            Times.Never);
    }

    [Test]
    public async Task Create_PassesUserEmailToFleeceService()
    {
        // Arrange
        var issue = CreateTestIssue("ABC123", "Test Issue");
        const string userEmail = "test@example.com";

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _dataStoreMock
            .Setup(x => x.UserEmail)
            .Returns(userEmail);
        _fleeceServiceMock
            .Setup(x => x.CreateIssueAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IssueType>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<ExecutionMode?>(),
                It.IsAny<IssueStatus?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(issue);

        var request = new CreateIssueRequest { ProjectId = TestProject.Id, Title = "Test Issue" };

        // Act
        await _controller.Create(request);

        // Assert - verify that the user email was passed to CreateIssueAsync
        _fleeceServiceMock.Verify(
            x => x.CreateIssueAsync(
                TestProject.LocalPath,
                request.Title,
                request.Type,
                request.Description,
                request.Priority,
                request.ExecutionMode,
                It.IsAny<IssueStatus?>(),
                userEmail,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task Create_WhenNoUserEmail_PassesNullToFleeceService()
    {
        // Arrange
        var issue = CreateTestIssue("ABC123", "Test Issue");

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _dataStoreMock
            .Setup(x => x.UserEmail)
            .Returns((string?)null);
        _fleeceServiceMock
            .Setup(x => x.CreateIssueAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IssueType>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<ExecutionMode?>(),
                It.IsAny<IssueStatus?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(issue);

        var request = new CreateIssueRequest { ProjectId = TestProject.Id, Title = "Test Issue" };

        // Act
        await _controller.Create(request);

        // Assert - verify that null was passed as the assignedTo parameter
        _fleeceServiceMock.Verify(
            x => x.CreateIssueAsync(
                TestProject.LocalPath,
                request.Title,
                request.Type,
                request.Description,
                request.Priority,
                request.ExecutionMode,
                It.IsAny<IssueStatus?>(),
                (string?)null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region Update Tests

    [Test]
    public async Task Update_BroadcastsIssueChanged_WithUpdatedChangeType()
    {
        // Arrange — every Update flows through the unified IssueChanged event,
        // regardless of whether the field is structure-preserving or topology-
        // affecting. The legacy patch / topology split was deleted.
        var issueId = "ABC123";
        var issue = CreateTestIssue(issueId, "Updated Issue", IssueType.Bug);

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _fleeceServiceMock
            .Setup(x => x.GetIssueAsync(It.IsAny<string>(), issueId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestIssue(issueId, "Original Issue", IssueType.Bug));
        _fleeceServiceMock
            .Setup(x => x.UpdateIssueAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<IssueStatus?>(), It.IsAny<IssueType?>(), It.IsAny<string?>(),
                It.IsAny<int?>(), It.IsAny<ExecutionMode?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(issue);

        var request = new UpdateIssueRequest { ProjectId = TestProject.Id, Status = IssueStatus.Progress };

        // Act
        await _controller.Update(issueId, request);

        // Assert
        _groupClientsMock.Verify(
            x => x.SendCoreAsync("IssueChanged",
                It.Is<object?[]>(args =>
                    args.Length == 4 &&
                    (string)args[0]! == TestProject.Id &&
                    (IssueChangeType)args[1]! == IssueChangeType.Updated &&
                    (string)args[2]! == issueId &&
                    args[3] is IssueResponse),
                default),
            Times.Once);
    }

    [Test]
    public async Task Update_TitleOnly_StillBroadcastsIssueChanged()
    {
        // Arrange — the legacy patch-only branch (IssueFieldsPatched) is gone.
        // Title-only edits emit the same unified IssueChanged event.
        var issueId = "ABC123";
        var issue = CreateTestIssue(issueId, "Updated Issue", IssueType.Bug);

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _fleeceServiceMock
            .Setup(x => x.GetIssueAsync(It.IsAny<string>(), issueId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestIssue(issueId, "Original Issue", IssueType.Bug));
        _fleeceServiceMock
            .Setup(x => x.UpdateIssueAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<IssueStatus?>(), It.IsAny<IssueType?>(), It.IsAny<string?>(),
                It.IsAny<int?>(), It.IsAny<ExecutionMode?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(issue);

        var request = new UpdateIssueRequest { ProjectId = TestProject.Id, Title = "Updated Issue" };

        // Act
        await _controller.Update(issueId, request);

        // Assert
        _groupClientsMock.Verify(
            x => x.SendCoreAsync("IssueChanged",
                It.Is<object?[]>(args =>
                    (string)args[0]! == TestProject.Id &&
                    (string)args[2]! == issueId),
                default),
            Times.Once);
        _groupClientsMock.Verify(
            x => x.SendCoreAsync("IssueFieldsPatched", It.IsAny<object?[]>(), default),
            Times.Never);
    }

    [Test]
    public async Task Update_IssueNotFound_DoesNotBroadcast()
    {
        // Arrange
        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _fleeceServiceMock
            .Setup(x => x.UpdateIssueAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<IssueStatus?>(), It.IsAny<IssueType?>(), It.IsAny<string?>(),
                It.IsAny<int?>(), It.IsAny<ExecutionMode?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Issue?)null);

        var request = new UpdateIssueRequest { ProjectId = TestProject.Id, Title = "Updated Issue" };

        // Act
        await _controller.Update("nonexistent", request);

        // Assert
        _allClientsMock.Verify(
            x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), default),
            Times.Never);
    }

    #endregion

    #region Delete Tests

    [Test]
    public async Task Delete_BroadcastsIssueChanged_WithDeletedChangeType()
    {
        // Arrange
        var issueId = "ABC123";

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _fleeceServiceMock
            .Setup(x => x.DeleteIssueAsync(TestProject.LocalPath, issueId))
            .ReturnsAsync(true);

        // Act
        await _controller.Delete(issueId, TestProject.Id);

        // Assert — Delete carries a null issue body.
        _groupClientsMock.Verify(
            x => x.SendCoreAsync("IssueChanged",
                It.Is<object?[]>(args =>
                    args.Length == 4 &&
                    (string)args[0]! == TestProject.Id &&
                    (IssueChangeType)args[1]! == IssueChangeType.Deleted &&
                    (string)args[2]! == issueId &&
                    args[3] == null),
                default),
            Times.Once);
    }

    [Test]
    public async Task Delete_IssueNotFound_DoesNotBroadcast()
    {
        // Arrange
        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _fleeceServiceMock
            .Setup(x => x.DeleteIssueAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(false);

        // Act
        await _controller.Delete("nonexistent", TestProject.Id);

        // Assert
        _allClientsMock.Verify(
            x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), default),
            Times.Never);
    }

    #endregion

    #region RunAgent Tests

    [Test]
    public async Task RunAgent_ReturnsAccepted_AndQueuesBackgroundWork()
    {
        // Arrange
        var issueId = "ABC123";
        var issue = new Issue
        {
            Id = issueId,
            Title = "Fix authentication bug",
            Type = IssueType.Bug,
            Status = IssueStatus.Open,
            Description = "Users cannot log in with OAuth",
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdate = DateTimeOffset.UtcNow
        };

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _fleeceServiceMock
            .Setup(x => x.GetIssueAsync(TestProject.LocalPath, issueId))
            .ReturnsAsync(issue);
        _branchResolverServiceMock
            .Setup(x => x.ResolveIssueBranchAsync(TestProject.Id, issueId))
            .ReturnsAsync((string?)null);

        var request = new RunAgentRequest
        {
            ProjectId = TestProject.Id,
            Mode = SessionMode.Build,
            Model = "sonnet"
        };

        // Act
        var result = await _controller.RunAgent(issueId, request);

        // Assert - returns 202 Accepted immediately
        Assert.That(result.Result, Is.TypeOf<AcceptedResult>());
        var acceptedResult = (AcceptedResult)result.Result!;
        var response = (RunAgentAcceptedResponse)acceptedResult.Value!;
        Assert.That(response.IssueId, Is.EqualTo(issueId));
        Assert.That(response.Message, Is.EqualTo("Agent is starting"));

        // Verify background service was queued with Mode
        _agentStartBackgroundServiceMock.Verify(
            x => x.QueueAgentStartAsync(It.Is<AgentStartRequest>(req =>
                req.IssueId == issueId &&
                req.ProjectId == TestProject.Id &&
                req.Mode == SessionMode.Build &&
                req.Model == "sonnet")),
            Times.Once);

        // Verify startup tracker was marked
        _agentStartupTrackerMock.Verify(
            x => x.TryMarkAsStarting(issueId),
            Times.Once);
    }

    [Test]
    public async Task RunAgent_ProjectNotFound_ReturnsNotFound()
    {
        // Arrange
        _projectServiceMock
            .Setup(x => x.GetByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((Project?)null);

        var request = new RunAgentRequest { ProjectId = "nonexistent" };

        // Act
        var result = await _controller.RunAgent("issue-123", request);

        // Assert
        Assert.That(result.Result, Is.TypeOf<NotFoundObjectResult>());

        // Verify background service was NOT queued
        _agentStartBackgroundServiceMock.Verify(
            x => x.QueueAgentStartAsync(It.IsAny<AgentStartRequest>()),
            Times.Never);
    }

    [Test]
    public async Task RunAgent_IssueNotFound_ReturnsNotFound()
    {
        // Arrange
        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _fleeceServiceMock
            .Setup(x => x.GetIssueAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((Issue?)null);

        var request = new RunAgentRequest { ProjectId = TestProject.Id };

        // Act
        var result = await _controller.RunAgent("nonexistent", request);

        // Assert
        Assert.That(result.Result, Is.TypeOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task RunAgent_PassesModeToBackgroundService()
    {
        // Arrange
        var issueId = "ABC123";
        var issue = CreateTestIssue(issueId, "Test Issue");

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _fleeceServiceMock
            .Setup(x => x.GetIssueAsync(TestProject.LocalPath, issueId))
            .ReturnsAsync(issue);
        _branchResolverServiceMock
            .Setup(x => x.ResolveIssueBranchAsync(TestProject.Id, issueId))
            .ReturnsAsync((string?)null);

        var request = new RunAgentRequest
        {
            ProjectId = TestProject.Id,
            Mode = SessionMode.Plan,
            Model = "sonnet"
        };

        // Act
        var result = await _controller.RunAgent(issueId, request);

        // Assert
        Assert.That(result.Result, Is.TypeOf<AcceptedResult>());

        // Verify background service was queued with the explicit Mode
        _agentStartBackgroundServiceMock.Verify(
            x => x.QueueAgentStartAsync(It.Is<AgentStartRequest>(req =>
                req.IssueId == issueId &&
                req.Mode == SessionMode.Plan)),
            Times.Once);
    }

    [Test]
    public async Task RunAgent_ReturnsConflict_WhenActiveSessionExists()
    {
        // Arrange
        var issueId = "ABC123";
        var issue = CreateTestIssue(issueId, "Test Issue");
        var existingSession = new ClaudeSession
        {
            Id = "existing-session-123",
            EntityId = issueId,
            ProjectId = TestProject.Id,
            WorkingDirectory = "/path/to/clone",
            Model = "sonnet",
            Mode = SessionMode.Build,
            Status = ClaudeSessionStatus.Running // Active status
        };

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _fleeceServiceMock
            .Setup(x => x.GetIssueAsync(TestProject.LocalPath, issueId))
            .ReturnsAsync(issue);
        _sessionServiceMock
            .Setup(x => x.GetSessionByEntityId(issueId))
            .Returns(existingSession);

        var request = new RunAgentRequest { ProjectId = TestProject.Id, Mode = SessionMode.Build };

        // Act
        var result = await _controller.RunAgent(issueId, request);

        // Assert
        Assert.That(result.Result, Is.TypeOf<ConflictObjectResult>());
        var conflictResult = (ConflictObjectResult)result.Result!;
        var response = (AgentAlreadyRunningResponse)conflictResult.Value!;
        Assert.That(response.SessionId, Is.EqualTo("existing-session-123"));
        Assert.That(response.Status, Is.EqualTo(ClaudeSessionStatus.Running));
        Assert.That(response.Message, Is.EqualTo("An agent is already running on this issue"));

        // Verify background service was NOT queued
        _agentStartBackgroundServiceMock.Verify(
            x => x.QueueAgentStartAsync(It.IsAny<AgentStartRequest>()),
            Times.Never);
    }

    [Test]
    public async Task RunAgent_ReturnsConflict_WhenAlreadyStarting()
    {
        // Arrange
        var issueId = "ABC123";
        var issue = CreateTestIssue(issueId, "Test Issue");

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _fleeceServiceMock
            .Setup(x => x.GetIssueAsync(TestProject.LocalPath, issueId))
            .ReturnsAsync(issue);
        _sessionServiceMock
            .Setup(x => x.GetSessionByEntityId(issueId))
            .Returns((ClaudeSession?)null);

        // Simulate already starting
        _agentStartupTrackerMock.Setup(x => x.TryMarkAsStarting(issueId))
            .Returns(false);

        var request = new RunAgentRequest { ProjectId = TestProject.Id };

        // Act
        var result = await _controller.RunAgent(issueId, request);

        // Assert
        Assert.That(result.Result, Is.TypeOf<ConflictObjectResult>());
        var conflictResult = (ConflictObjectResult)result.Result!;
        var response = (AgentAlreadyRunningResponse)conflictResult.Value!;
        Assert.That(response.Status, Is.EqualTo(ClaudeSessionStatus.Starting));
        Assert.That(response.Message, Is.EqualTo("Agent is already starting on this issue"));

        // Verify background service was NOT queued
        _agentStartBackgroundServiceMock.Verify(
            x => x.QueueAgentStartAsync(It.IsAny<AgentStartRequest>()),
            Times.Never);
    }

    [Test]
    public async Task RunAgent_Succeeds_WhenNoActiveSessionExists()
    {
        // Arrange
        var issueId = "ABC123";
        var issue = CreateTestIssue(issueId, "Test Issue");
        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _fleeceServiceMock
            .Setup(x => x.GetIssueAsync(TestProject.LocalPath, issueId))
            .ReturnsAsync(issue);
        _sessionServiceMock
            .Setup(x => x.GetSessionByEntityId(issueId))
            .Returns((ClaudeSession?)null); // No existing session
        _branchResolverServiceMock
            .Setup(x => x.ResolveIssueBranchAsync(TestProject.Id, issueId))
            .ReturnsAsync((string?)null);

        var request = new RunAgentRequest { ProjectId = TestProject.Id, Mode = SessionMode.Build };

        // Act
        var result = await _controller.RunAgent(issueId, request);

        // Assert
        Assert.That(result.Result, Is.TypeOf<AcceptedResult>());
        var acceptedResult = (AcceptedResult)result.Result!;
        var response = (RunAgentAcceptedResponse)acceptedResult.Value!;
        Assert.That(response.IssueId, Is.EqualTo(issueId));

        // Verify background service was queued
        _agentStartBackgroundServiceMock.Verify(
            x => x.QueueAgentStartAsync(It.IsAny<AgentStartRequest>()),
            Times.Once);
    }

    [Test]
    public async Task RunAgent_Succeeds_WhenSessionExistsButStopped()
    {
        // Arrange
        var issueId = "ABC123";
        var issue = CreateTestIssue(issueId, "Test Issue");
        var stoppedSession = new ClaudeSession
        {
            Id = "stopped-session-123",
            EntityId = issueId,
            ProjectId = TestProject.Id,
            WorkingDirectory = "/path/to/clone",
            Model = "sonnet",
            Mode = SessionMode.Build,
            Status = ClaudeSessionStatus.Stopped // Not active
        };
        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _fleeceServiceMock
            .Setup(x => x.GetIssueAsync(TestProject.LocalPath, issueId))
            .ReturnsAsync(issue);
        _sessionServiceMock
            .Setup(x => x.GetSessionByEntityId(issueId))
            .Returns(stoppedSession); // Session exists but is stopped
        _branchResolverServiceMock
            .Setup(x => x.ResolveIssueBranchAsync(TestProject.Id, issueId))
            .ReturnsAsync((string?)null);

        var request = new RunAgentRequest { ProjectId = TestProject.Id, Mode = SessionMode.Build };

        // Act
        var result = await _controller.RunAgent(issueId, request);

        // Assert - should succeed because the existing session is stopped
        Assert.That(result.Result, Is.TypeOf<AcceptedResult>());

        // Verify background service was queued
        _agentStartBackgroundServiceMock.Verify(
            x => x.QueueAgentStartAsync(It.IsAny<AgentStartRequest>()),
            Times.Once);
    }

    [Test]
    public async Task RunAgent_ReturnsConflict_WhenSessionIsWaitingForInput()
    {
        // Arrange
        var issueId = "ABC123";
        var issue = CreateTestIssue(issueId, "Test Issue");
        var waitingSession = new ClaudeSession
        {
            Id = "waiting-session-123",
            EntityId = issueId,
            ProjectId = TestProject.Id,
            WorkingDirectory = "/path/to/clone",
            Model = "sonnet",
            Mode = SessionMode.Build,
            Status = ClaudeSessionStatus.WaitingForInput // Still active
        };

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _fleeceServiceMock
            .Setup(x => x.GetIssueAsync(TestProject.LocalPath, issueId))
            .ReturnsAsync(issue);
        _sessionServiceMock
            .Setup(x => x.GetSessionByEntityId(issueId))
            .Returns(waitingSession);

        var request = new RunAgentRequest { ProjectId = TestProject.Id, Mode = SessionMode.Build };

        // Act
        var result = await _controller.RunAgent(issueId, request);

        // Assert
        Assert.That(result.Result, Is.TypeOf<ConflictObjectResult>());
        var conflictResult = (ConflictObjectResult)result.Result!;
        var response = (AgentAlreadyRunningResponse)conflictResult.Value!;
        Assert.That(response.SessionId, Is.EqualTo("waiting-session-123"));
        Assert.That(response.Status, Is.EqualTo(ClaudeSessionStatus.WaitingForInput));
    }

    [Test]
    public async Task RunAgent_PassesBaseBranchToBackgroundService()
    {
        // Arrange
        var issueId = "ABC123";
        var issue = CreateTestIssue(issueId, "Test Issue");
        var customBaseBranch = "develop";

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _fleeceServiceMock
            .Setup(x => x.GetIssueAsync(TestProject.LocalPath, issueId))
            .ReturnsAsync(issue);
        _branchResolverServiceMock
            .Setup(x => x.ResolveIssueBranchAsync(TestProject.Id, issueId))
            .ReturnsAsync((string?)null);

        var request = new RunAgentRequest
        {
            ProjectId = TestProject.Id,
            BaseBranch = customBaseBranch
        };

        // Act
        var result = await _controller.RunAgent(issueId, request);

        // Assert
        Assert.That(result.Result, Is.TypeOf<AcceptedResult>());

        // Verify background service was queued with the custom base branch
        _agentStartBackgroundServiceMock.Verify(
            x => x.QueueAgentStartAsync(It.Is<AgentStartRequest>(req =>
                req.IssueId == issueId &&
                req.BaseBranch == customBaseBranch)),
            Times.Once);
    }

    [Test]
    public async Task RunAgent_PassesUserInstructionsToBackgroundService()
    {
        // Arrange
        var issueId = "ABC123";
        var issue = CreateTestIssue(issueId, "Test Issue");

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _fleeceServiceMock
            .Setup(x => x.GetIssueAsync(TestProject.LocalPath, issueId))
            .ReturnsAsync(issue);
        _branchResolverServiceMock
            .Setup(x => x.ResolveIssueBranchAsync(TestProject.Id, issueId))
            .ReturnsAsync((string?)null);

        var request = new RunAgentRequest
        {
            ProjectId = TestProject.Id,
            UserInstructions = "Custom instructions for the agent"
        };

        // Act
        var result = await _controller.RunAgent(issueId, request);

        // Assert
        Assert.That(result.Result, Is.TypeOf<AcceptedResult>());

        // Verify background service was queued with UserInstructions
        _agentStartBackgroundServiceMock.Verify(
            x => x.QueueAgentStartAsync(It.Is<AgentStartRequest>(req =>
                req.IssueId == issueId &&
                req.UserInstructions == "Custom instructions for the agent")),
            Times.Once);
    }

    [Test]
    public async Task RunAgent_PassesSkillNameAndArgsToBackgroundService()
    {
        // Arrange
        var issueId = "ABC123";
        var issue = CreateTestIssue(issueId, "Test Issue");

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _fleeceServiceMock
            .Setup(x => x.GetIssueAsync(TestProject.LocalPath, issueId))
            .ReturnsAsync(issue);
        _branchResolverServiceMock
            .Setup(x => x.ResolveIssueBranchAsync(TestProject.Id, issueId))
            .ReturnsAsync((string?)null);

        var request = new RunAgentRequest
        {
            ProjectId = TestProject.Id,
            SkillName = "fix-bug",
            SkillArgs = new Dictionary<string, string>
            {
                ["issue-id"] = issueId
            }
        };

        // Act
        var result = await _controller.RunAgent(issueId, request);

        // Assert
        Assert.That(result.Result, Is.TypeOf<AcceptedResult>());

        _agentStartBackgroundServiceMock.Verify(
            x => x.QueueAgentStartAsync(It.Is<AgentStartRequest>(req =>
                req.IssueId == issueId &&
                req.SkillName == "fix-bug" &&
                req.SkillArgs != null &&
                req.SkillArgs["issue-id"] == issueId)),
            Times.Once);
    }

    #endregion

    #region Update Auto-Assign Tests

    [Test]
    public async Task Update_WhenIssueHasNoAssignee_AutoAssignsCurrentUser()
    {
        // Arrange
        var issueId = "ABC123";
        var currentUserEmail = "currentuser@example.com";
        var currentIssue = new Issue
        {
            Id = issueId,
            Title = "Original Issue",
            Type = IssueType.Task,
            Status = IssueStatus.Open,
            AssignedTo = null, // No current assignee
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdate = DateTimeOffset.UtcNow
        };
        var updatedIssue = new Issue
        {
            Id = issueId,
            Title = "Updated Issue",
            Type = IssueType.Task,
            Status = IssueStatus.Open,
            AssignedTo = currentUserEmail,
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdate = DateTimeOffset.UtcNow
        };

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _dataStoreMock
            .Setup(x => x.UserEmail)
            .Returns(currentUserEmail);
        _fleeceServiceMock
            .Setup(x => x.GetIssueAsync(TestProject.LocalPath, issueId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentIssue);
        _fleeceServiceMock
            .Setup(x => x.UpdateIssueAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<IssueStatus?>(), It.IsAny<IssueType?>(), It.IsAny<string?>(),
                It.IsAny<int?>(), It.IsAny<ExecutionMode?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updatedIssue);

        var request = new UpdateIssueRequest
        {
            ProjectId = TestProject.Id,
            Title = "Updated Issue"
            // AssignedTo is null (not provided)
        };

        // Act
        await _controller.Update(issueId, request);

        // Assert - verify that the current user email was auto-assigned
        _fleeceServiceMock.Verify(
            x => x.UpdateIssueAsync(
                TestProject.LocalPath,
                issueId,
                request.Title,
                request.Status,
                request.Type,
                request.Description,
                request.Priority,
                request.ExecutionMode,
                request.WorkingBranchId,
                currentUserEmail, // Should be auto-assigned
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task Update_WhenIssueHasNoAssignee_AndNoCurrentUser_DoesNotAutoAssign()
    {
        // Arrange
        var issueId = "ABC123";
        var currentIssue = new Issue
        {
            Id = issueId,
            Title = "Original Issue",
            Type = IssueType.Task,
            Status = IssueStatus.Open,
            AssignedTo = null, // No current assignee
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdate = DateTimeOffset.UtcNow
        };
        var updatedIssue = new Issue
        {
            Id = issueId,
            Title = "Updated Issue",
            Type = IssueType.Task,
            Status = IssueStatus.Open,
            AssignedTo = null,
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdate = DateTimeOffset.UtcNow
        };

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _dataStoreMock
            .Setup(x => x.UserEmail)
            .Returns((string?)null); // No current user configured
        _fleeceServiceMock
            .Setup(x => x.GetIssueAsync(TestProject.LocalPath, issueId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentIssue);
        _fleeceServiceMock
            .Setup(x => x.UpdateIssueAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<IssueStatus?>(), It.IsAny<IssueType?>(), It.IsAny<string?>(),
                It.IsAny<int?>(), It.IsAny<ExecutionMode?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updatedIssue);

        var request = new UpdateIssueRequest
        {
            ProjectId = TestProject.Id,
            Title = "Updated Issue"
            // AssignedTo is null (not provided)
        };

        // Act
        await _controller.Update(issueId, request);

        // Assert - verify that null was passed (no auto-assignment)
        _fleeceServiceMock.Verify(
            x => x.UpdateIssueAsync(
                TestProject.LocalPath,
                issueId,
                request.Title,
                request.Status,
                request.Type,
                request.Description,
                request.Priority,
                request.ExecutionMode,
                request.WorkingBranchId,
                (string?)null, // Should remain null
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task Update_WhenIssueAlreadyHasAssignee_DoesNotOverwrite()
    {
        // Arrange
        var issueId = "ABC123";
        var existingAssignee = "existing@example.com";
        var currentUserEmail = "currentuser@example.com";
        var currentIssue = new Issue
        {
            Id = issueId,
            Title = "Original Issue",
            Type = IssueType.Task,
            Status = IssueStatus.Open,
            AssignedTo = existingAssignee, // Already has assignee
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdate = DateTimeOffset.UtcNow
        };
        var updatedIssue = new Issue
        {
            Id = issueId,
            Title = "Updated Issue",
            Type = IssueType.Task,
            Status = IssueStatus.Open,
            AssignedTo = existingAssignee,
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdate = DateTimeOffset.UtcNow
        };

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _dataStoreMock
            .Setup(x => x.UserEmail)
            .Returns(currentUserEmail);
        _fleeceServiceMock
            .Setup(x => x.GetIssueAsync(TestProject.LocalPath, issueId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentIssue);
        _fleeceServiceMock
            .Setup(x => x.UpdateIssueAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<IssueStatus?>(), It.IsAny<IssueType?>(), It.IsAny<string?>(),
                It.IsAny<int?>(), It.IsAny<ExecutionMode?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updatedIssue);

        var request = new UpdateIssueRequest
        {
            ProjectId = TestProject.Id,
            Title = "Updated Issue"
            // AssignedTo is null (not provided - meaning "don't change")
        };

        // Act
        await _controller.Update(issueId, request);

        // Assert - verify that null was passed (not the current user)
        // When request.AssignedTo is null and issue already has assignee,
        // don't override with current user
        _fleeceServiceMock.Verify(
            x => x.UpdateIssueAsync(
                TestProject.LocalPath,
                issueId,
                request.Title,
                request.Status,
                request.Type,
                request.Description,
                request.Priority,
                request.ExecutionMode,
                request.WorkingBranchId,
                (string?)null, // Should be null to preserve existing assignee
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task Update_WhenRequestExplicitlySetsAssignee_UsesRequestValue()
    {
        // Arrange
        var issueId = "ABC123";
        var requestAssignee = "requested@example.com";
        var currentUserEmail = "currentuser@example.com";
        var currentIssue = new Issue
        {
            Id = issueId,
            Title = "Original Issue",
            Type = IssueType.Task,
            Status = IssueStatus.Open,
            AssignedTo = null, // No current assignee
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdate = DateTimeOffset.UtcNow
        };
        var updatedIssue = new Issue
        {
            Id = issueId,
            Title = "Updated Issue",
            Type = IssueType.Task,
            Status = IssueStatus.Open,
            AssignedTo = requestAssignee,
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdate = DateTimeOffset.UtcNow
        };

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _dataStoreMock
            .Setup(x => x.UserEmail)
            .Returns(currentUserEmail);
        _fleeceServiceMock
            .Setup(x => x.GetIssueAsync(TestProject.LocalPath, issueId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentIssue);
        _fleeceServiceMock
            .Setup(x => x.UpdateIssueAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<IssueStatus?>(), It.IsAny<IssueType?>(), It.IsAny<string?>(),
                It.IsAny<int?>(), It.IsAny<ExecutionMode?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updatedIssue);

        var request = new UpdateIssueRequest
        {
            ProjectId = TestProject.Id,
            Title = "Updated Issue",
            AssignedTo = requestAssignee // Explicitly set
        };

        // Act
        await _controller.Update(issueId, request);

        // Assert - verify that the request value was used, not auto-assigned
        _fleeceServiceMock.Verify(
            x => x.UpdateIssueAsync(
                TestProject.LocalPath,
                issueId,
                request.Title,
                request.Status,
                request.Type,
                request.Description,
                request.Priority,
                request.ExecutionMode,
                request.WorkingBranchId,
                requestAssignee, // Should use the request value
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region Update AssignedTo Tests

    [Test]
    public async Task Update_PassesAssignedToToFleeceService()
    {
        // Arrange
        var issueId = "ABC123";
        var assignedEmail = "dev@example.com";
        var currentIssue = CreateTestIssue(issueId, "Original Issue");
        var updatedIssue = new Issue
        {
            Id = issueId,
            Title = "Original Issue",
            Type = IssueType.Task,
            Status = IssueStatus.Open,
            AssignedTo = assignedEmail,
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdate = DateTimeOffset.UtcNow
        };

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _fleeceServiceMock
            .Setup(x => x.GetIssueAsync(TestProject.LocalPath, issueId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentIssue);
        _fleeceServiceMock
            .Setup(x => x.UpdateIssueAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<IssueStatus?>(), It.IsAny<IssueType?>(), It.IsAny<string?>(),
                It.IsAny<int?>(), It.IsAny<ExecutionMode?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updatedIssue);

        var request = new UpdateIssueRequest
        {
            ProjectId = TestProject.Id,
            AssignedTo = assignedEmail
        };

        // Act
        await _controller.Update(issueId, request);

        // Assert - verify that the assignedTo was passed to UpdateIssueAsync
        _fleeceServiceMock.Verify(
            x => x.UpdateIssueAsync(
                TestProject.LocalPath,
                issueId,
                request.Title,
                request.Status,
                request.Type,
                request.Description,
                request.Priority,
                request.ExecutionMode,
                request.WorkingBranchId,
                assignedEmail,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region GetProjectAssignees Tests

    [Test]
    public async Task GetProjectAssignees_ReturnsUniqueAssignees()
    {
        // Arrange
        var issues = new List<Issue>
        {
            new Issue { Id = "1", Title = "Issue 1", Type = IssueType.Task, Status = IssueStatus.Open, AssignedTo = "alice@example.com", CreatedAt = DateTimeOffset.UtcNow, LastUpdate = DateTimeOffset.UtcNow },
            new Issue { Id = "2", Title = "Issue 2", Type = IssueType.Task, Status = IssueStatus.Open, AssignedTo = "bob@example.com", CreatedAt = DateTimeOffset.UtcNow, LastUpdate = DateTimeOffset.UtcNow },
            new Issue { Id = "3", Title = "Issue 3", Type = IssueType.Task, Status = IssueStatus.Open, AssignedTo = "alice@example.com", CreatedAt = DateTimeOffset.UtcNow, LastUpdate = DateTimeOffset.UtcNow }, // Duplicate
            new Issue { Id = "4", Title = "Issue 4", Type = IssueType.Task, Status = IssueStatus.Open, AssignedTo = null, CreatedAt = DateTimeOffset.UtcNow, LastUpdate = DateTimeOffset.UtcNow } // No assignee
        };

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _fleeceServiceMock
            .Setup(x => x.ListIssuesAsync(TestProject.LocalPath, null, null, null, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(issues);
        _dataStoreMock
            .Setup(x => x.UserEmail)
            .Returns((string?)null);

        // Act
        var result = await _controller.GetProjectAssignees(TestProject.Id);

        // Assert
        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var response = (ProjectAssigneesResponse)okResult.Value!;
        var assignees = response.Assignees;

        Assert.That(assignees, Has.Count.EqualTo(2));
        Assert.That(assignees, Contains.Item("alice@example.com"));
        Assert.That(assignees, Contains.Item("bob@example.com"));
    }

    [Test]
    public async Task GetProjectAssignees_IncludesCurrentUser()
    {
        // Arrange
        var issues = new List<Issue>
        {
            new Issue { Id = "1", Title = "Issue 1", Type = IssueType.Task, Status = IssueStatus.Open, AssignedTo = "alice@example.com", CreatedAt = DateTimeOffset.UtcNow, LastUpdate = DateTimeOffset.UtcNow }
        };
        var currentUserEmail = "currentuser@example.com";

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _fleeceServiceMock
            .Setup(x => x.ListIssuesAsync(TestProject.LocalPath, null, null, null, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(issues);
        _dataStoreMock
            .Setup(x => x.UserEmail)
            .Returns(currentUserEmail);

        // Act
        var result = await _controller.GetProjectAssignees(TestProject.Id);

        // Assert
        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var response = (ProjectAssigneesResponse)okResult.Value!;
        var assignees = response.Assignees;

        Assert.That(assignees, Contains.Item(currentUserEmail));
        Assert.That(assignees, Contains.Item("alice@example.com"));
        // Current user should be first in the list
        Assert.That(assignees[0], Is.EqualTo(currentUserEmail));
    }

    [Test]
    public async Task GetProjectAssignees_ProjectNotFound_Returns404()
    {
        // Arrange
        _projectServiceMock
            .Setup(x => x.GetByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((Project?)null);

        // Act
        var result = await _controller.GetProjectAssignees("nonexistent");

        // Assert
        Assert.That(result.Result, Is.TypeOf<NotFoundObjectResult>());
    }

    #endregion

    [Test]
    public async Task RunAgent_resolves_request_model_alias_through_catalog()
    {
        var issueId = "ABC123";
        var issue = new Issue
        {
            Id = issueId,
            Title = "Resolve model",
            Type = IssueType.Task,
            Status = IssueStatus.Open,
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdate = DateTimeOffset.UtcNow,
        };

        _projectServiceMock.Setup(x => x.GetByIdAsync(TestProject.Id)).ReturnsAsync(TestProject);
        _fleeceServiceMock.Setup(x => x.GetIssueAsync(TestProject.LocalPath, issueId)).ReturnsAsync(issue);
        _branchResolverServiceMock
            .Setup(x => x.ResolveIssueBranchAsync(TestProject.Id, issueId))
            .ReturnsAsync((string?)null);
        _modelCatalogMock
            .Setup(m => m.ResolveModelIdAsync("sonnet", It.IsAny<CancellationToken>()))
            .ReturnsAsync("claude-sonnet-4-6-20250601");

        var request = new RunAgentRequest
        {
            ProjectId = TestProject.Id,
            Mode = SessionMode.Build,
            Model = "sonnet",
        };

        var result = await _controller.RunAgent(issueId, request);

        Assert.That(result.Result, Is.TypeOf<AcceptedResult>());
        _modelCatalogMock.Verify(
            m => m.ResolveModelIdAsync("sonnet", It.IsAny<CancellationToken>()),
            Times.Once);
        _agentStartBackgroundServiceMock.Verify(
            x => x.QueueAgentStartAsync(It.Is<AgentStartRequest>(r => r.Model == "claude-sonnet-4-6-20250601")),
            Times.Once);
    }
}
