using Fleece.Core.Models;
using Homespun.Features.ClaudeCode.Hubs;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Notifications;
using Homespun.Features.Testing.Services;
using Homespun.Shared.Models.Fleece;
using Homespun.Shared.Models.Sessions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using AgentStartRequest = Homespun.Features.AgentOrchestration.Services.AgentStartRequest;

namespace Homespun.Tests.Features.Testing;

[TestFixture]
public class MockAgentStartBackgroundServiceTests
{
    private Mock<IClaudeSessionService> _sessionService = null!;
    private Mock<ISkillDiscoveryService> _skillDiscovery = null!;
    private Mock<IAgentStartupTracker> _startupTracker = null!;
    private Mock<IHubContext<ClaudeCodeHub>> _claudeHub = null!;
    private Mock<IHubContext<NotificationHub>> _notificationHub = null!;
    private Mock<IHubClients> _hubClients = null!;
    private Mock<IClientProxy> _clientProxy = null!;
    private MockAgentStartBackgroundService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _sessionService = new Mock<IClaudeSessionService>();
        _skillDiscovery = new Mock<ISkillDiscoveryService>();
        _startupTracker = new Mock<IAgentStartupTracker>();
        _claudeHub = new Mock<IHubContext<ClaudeCodeHub>>();
        _notificationHub = new Mock<IHubContext<NotificationHub>>();
        _hubClients = new Mock<IHubClients>();
        _clientProxy = new Mock<IClientProxy>();

        _claudeHub.Setup(x => x.Clients).Returns(_hubClients.Object);
        _notificationHub.Setup(x => x.Clients).Returns(_hubClients.Object);
        _hubClients.Setup(x => x.All).Returns(_clientProxy.Object);
        _hubClients.Setup(x => x.Group(It.IsAny<string>())).Returns(_clientProxy.Object);

        _skillDiscovery
            .Setup(x => x.GetSkillAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SkillDescriptor?)null);

        _service = new MockAgentStartBackgroundService(
            _sessionService.Object,
            _skillDiscovery.Object,
            _startupTracker.Object,
            _claudeHub.Object,
            _notificationHub.Object,
            Mock.Of<ILogger<MockAgentStartBackgroundService>>());
    }

    private static AgentStartRequest CreateRequest(string projectLocalPath)
    {
        return new AgentStartRequest
        {
            IssueId = "ISSUE-009",
            ProjectId = "demo-project",
            ProjectLocalPath = projectLocalPath,
            ProjectDefaultBranch = "main",
            Issue = new Issue
            {
                Id = "ISSUE-009",
                Title = "Demo",
                Status = IssueStatus.Progress,
                Type = IssueType.Task,
                CreatedAt = DateTimeOffset.UtcNow,
                LastUpdate = DateTimeOffset.UtcNow
            },
            BranchName = "task/demo+ISSUE-009",
            Model = "sonnet",
            Mode = SessionMode.Plan
        };
    }

    private ClaudeSession StubSession(string workingDirectory) => new()
    {
        Id = "session-1",
        EntityId = "ISSUE-009",
        ProjectId = "demo-project",
        WorkingDirectory = workingDirectory,
        Model = "sonnet",
        Mode = SessionMode.Plan,
        Status = ClaudeSessionStatus.Running
    };

    [Test]
    public async Task QueueAgentStartAsync_UsesProjectLocalPath_AsWorkingDirectory()
    {
        var seededPath = Path.Combine(Path.GetTempPath(), "homespun-mock-test", "projects", "demo-project");
        var request = CreateRequest(seededPath);

        _sessionService
            .Setup(x => x.StartSessionAsync(
                request.IssueId, request.ProjectId, seededPath,
                SessionMode.Plan, "sonnet", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(StubSession(seededPath));

        await _service.QueueAgentStartAsync(request);

        _sessionService.Verify(x => x.StartSessionAsync(
            request.IssueId, request.ProjectId, seededPath,
            SessionMode.Plan, "sonnet", null, It.IsAny<CancellationToken>()), Times.Once);
        _startupTracker.Verify(x => x.MarkAsStarted(request.IssueId), Times.Once);
    }

    [Test]
    public async Task QueueAgentStartAsync_FallsBackToMockClonePath_WhenProjectLocalPathEmpty()
    {
        var request = CreateRequest(projectLocalPath: string.Empty);
        var fallback = $"/mock/clones/{request.BranchName}";

        _sessionService
            .Setup(x => x.StartSessionAsync(
                request.IssueId, request.ProjectId, fallback,
                SessionMode.Plan, "sonnet", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(StubSession(fallback));

        await _service.QueueAgentStartAsync(request);

        _sessionService.Verify(x => x.StartSessionAsync(
            request.IssueId, request.ProjectId, fallback,
            SessionMode.Plan, "sonnet", null, It.IsAny<CancellationToken>()), Times.Once);
    }
}
