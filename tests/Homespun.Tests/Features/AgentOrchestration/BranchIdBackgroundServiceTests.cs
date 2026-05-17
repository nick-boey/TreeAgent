using Fleece.Core.Models;
using Homespun.Features.AgentOrchestration.Services;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Notifications;
using Homespun.Features.Projects;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.AgentOrchestration;

[TestFixture]
public class BranchIdBackgroundServiceTests
{
    private Mock<IServiceProvider> _mockServiceProvider = null!;
    private Mock<IServiceScope> _mockServiceScope = null!;
    private Mock<IServiceScopeFactory> _mockServiceScopeFactory = null!;
    private Mock<IBranchIdGeneratorService> _mockBranchIdGenerator = null!;
    private Mock<IProjectFleeceService> _mockFleeceService = null!;
    private Mock<IProjectService> _mockProjectService = null!;
    private Mock<IHubContext<NotificationHub>> _mockHubContext = null!;
    private Mock<IHubClients> _mockHubClients = null!;
    private Mock<IClientProxy> _mockClientProxy = null!;
    private Mock<ILogger<BranchIdBackgroundService>> _mockLogger = null!;
    private BranchIdBackgroundService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _mockServiceProvider = new Mock<IServiceProvider>();
        _mockServiceScope = new Mock<IServiceScope>();
        _mockServiceScopeFactory = new Mock<IServiceScopeFactory>();
        _mockBranchIdGenerator = new Mock<IBranchIdGeneratorService>();
        _mockFleeceService = new Mock<IProjectFleeceService>();
        _mockProjectService = new Mock<IProjectService>();
        _mockHubContext = new Mock<IHubContext<NotificationHub>>();
        _mockHubClients = new Mock<IHubClients>();
        _mockClientProxy = new Mock<IClientProxy>();
        _mockLogger = new Mock<ILogger<BranchIdBackgroundService>>();

        // Setup service scope
        var scopedServiceProvider = new Mock<IServiceProvider>();
        scopedServiceProvider.Setup(x => x.GetService(typeof(IBranchIdGeneratorService)))
            .Returns(_mockBranchIdGenerator.Object);
        scopedServiceProvider.Setup(x => x.GetService(typeof(IProjectFleeceService)))
            .Returns(_mockFleeceService.Object);
        scopedServiceProvider.Setup(x => x.GetService(typeof(IProjectService)))
            .Returns(_mockProjectService.Object);
        scopedServiceProvider.Setup(x => x.GetService(typeof(IHubContext<NotificationHub>)))
            .Returns(_mockHubContext.Object);

        _mockServiceScope.Setup(x => x.ServiceProvider).Returns(scopedServiceProvider.Object);
        _mockServiceScopeFactory.Setup(x => x.CreateScope()).Returns(_mockServiceScope.Object);
        _mockServiceProvider.Setup(x => x.GetService(typeof(IServiceScopeFactory)))
            .Returns(_mockServiceScopeFactory.Object);

        // Setup hub context
        _mockHubContext.Setup(x => x.Clients).Returns(_mockHubClients.Object);
        _mockHubClients.Setup(x => x.All).Returns(_mockClientProxy.Object);

        _service = new BranchIdBackgroundService(_mockServiceProvider.Object, _mockLogger.Object);
    }

    #region QueueBranchIdGenerationAsync Tests

    [Test]
    public async Task QueueBranchIdGenerationAsync_SuccessfulGeneration_UpdatesIssueAndSendsNotification()
    {
        // Arrange
        var issueId = "issue123";
        var projectId = "proj123";
        var title = "Fix authentication bug";
        var generatedBranchId = "fix-auth-bug";

        var project = new Project { Id = projectId, Name = "Test", LocalPath = "/path", DefaultBranch = "main" };
        var ts = DateTimeOffset.UtcNow;
        var issue = new Issue { Id = issueId, Title = title, Status = IssueStatus.Open, Type = IssueType.Task, CreatedAt = ts, LastUpdate = ts };
        var updatedIssue = new Issue { Id = issueId, Title = title, Status = IssueStatus.Open, Type = IssueType.Task, CreatedAt = ts, LastUpdate = ts, WorkingBranchId = generatedBranchId };

        _mockBranchIdGenerator.Setup(x => x.GenerateAsync(title, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BranchIdResult(true, generatedBranchId, null, true));

        _mockProjectService.Setup(x => x.GetByIdAsync(projectId))
            .ReturnsAsync(project);

        _mockFleeceService.Setup(x => x.GetIssueAsync("/path", issueId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(issue);

        _mockFleeceService.Setup(x => x.UpdateIssueAsync(
                "/path", issueId, null, null, null, null, null, null, generatedBranchId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(updatedIssue);

        // Act
        await _service.QueueBranchIdGenerationAsync(issueId, projectId, title);

        // Wait for background task to complete
        await Task.Delay(100);

        // Assert
        _mockBranchIdGenerator.Verify(x => x.GenerateAsync(title, It.IsAny<CancellationToken>()), Times.Once);
        _mockFleeceService.Verify(x => x.UpdateIssueAsync(
                "/path", issueId, null, null, null, null, null, null, generatedBranchId, null, It.IsAny<CancellationToken>()),
            Times.Once);
        _mockClientProxy.Verify(x => x.SendCoreAsync(
            "BranchIdGenerated",
            It.Is<object?[]>(args => args[0]!.Equals(issueId) && args[1]!.Equals(projectId) &&
                                     args[2]!.Equals(generatedBranchId) && args[3]!.Equals(true)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task QueueBranchIdGenerationAsync_GenerationFails_SendsFailureNotification()
    {
        // Arrange
        var issueId = "issue123";
        var projectId = "proj123";
        var title = "Fix authentication bug";
        var errorMessage = "AI service unavailable";

        _mockBranchIdGenerator.Setup(x => x.GenerateAsync(title, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BranchIdResult(false, null, errorMessage, false));

        // Act
        await _service.QueueBranchIdGenerationAsync(issueId, projectId, title);

        // Wait for background task to complete
        await Task.Delay(100);

        // Assert
        _mockBranchIdGenerator.Verify(x => x.GenerateAsync(title, It.IsAny<CancellationToken>()), Times.Once);
        _mockFleeceService.Verify(x => x.UpdateIssueAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<IssueStatus?>(), It.IsAny<IssueType?>(), It.IsAny<string>(),
            It.IsAny<int?>(), It.IsAny<ExecutionMode?>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockClientProxy.Verify(x => x.SendCoreAsync(
            "BranchIdGenerationFailed",
            It.Is<object?[]>(args => args[0]!.Equals(issueId) && args[1]!.Equals(projectId) &&
                                     args[2]!.Equals(errorMessage)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task QueueBranchIdGenerationAsync_IssueAlreadyHasBranchId_SkipsGeneration()
    {
        // Arrange
        var issueId = "issue123";
        var projectId = "proj123";
        var title = "Fix authentication bug";
        var existingBranchId = "existing-branch";

        var project = new Project { Id = projectId, Name = "Test", LocalPath = "/path", DefaultBranch = "main" };
        var ts = DateTimeOffset.UtcNow;
        var issue = new Issue { Id = issueId, Title = title, Status = IssueStatus.Open, Type = IssueType.Task, CreatedAt = ts, LastUpdate = ts, WorkingBranchId = existingBranchId };

        _mockBranchIdGenerator.Setup(x => x.GenerateAsync(title, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BranchIdResult(true, "new-branch", null, true));

        _mockProjectService.Setup(x => x.GetByIdAsync(projectId))
            .ReturnsAsync(project);

        _mockFleeceService.Setup(x => x.GetIssueAsync("/path", issueId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(issue);

        // Act
        await _service.QueueBranchIdGenerationAsync(issueId, projectId, title);

        // Wait for background task to complete
        await Task.Delay(100);

        // Assert
        _mockBranchIdGenerator.Verify(x => x.GenerateAsync(title, It.IsAny<CancellationToken>()), Times.Once);
        _mockFleeceService.Verify(x => x.UpdateIssueAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<IssueStatus?>(), It.IsAny<IssueType?>(), It.IsAny<string>(),
            It.IsAny<int?>(), It.IsAny<ExecutionMode?>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockClientProxy.Verify(x => x.SendCoreAsync(
            It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task QueueBranchIdGenerationAsync_ProjectNotFound_SkipsGenerationGracefully()
    {
        // Arrange
        var issueId = "issue123";
        var projectId = "proj123";
        var title = "Fix authentication bug";

        _mockBranchIdGenerator.Setup(x => x.GenerateAsync(title, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BranchIdResult(true, "fix-auth-bug", null, true));

        _mockProjectService.Setup(x => x.GetByIdAsync(projectId))
            .ReturnsAsync((Project?)null);

        // Act
        await _service.QueueBranchIdGenerationAsync(issueId, projectId, title);

        // Wait for background task to complete
        await Task.Delay(100);

        // Assert
        _mockFleeceService.Verify(
            x => x.GetIssueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockFleeceService.Verify(x => x.UpdateIssueAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<IssueStatus?>(), It.IsAny<IssueType?>(), It.IsAny<string>(),
            It.IsAny<int?>(), It.IsAny<ExecutionMode?>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockClientProxy.Verify(x => x.SendCoreAsync(
            It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task QueueBranchIdGenerationAsync_GenerationTimesOut_SendsTimeoutNotification()
    {
        // Arrange
        var issueId = "issue123";
        var projectId = "proj123";
        var title = "Fix authentication bug";

        _mockBranchIdGenerator.Setup(x => x.GenerateAsync(title, It.IsAny<CancellationToken>()))
            .Returns(async (string t, CancellationToken ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(15), ct);
                ct.ThrowIfCancellationRequested();
                return new BranchIdResult(true, "fix-auth-bug", null, true);
            });

        // Act
        await _service.QueueBranchIdGenerationAsync(issueId, projectId, title);

        // Wait for timeout
        await Task.Delay(TimeSpan.FromSeconds(11));

        // Assert
        _mockClientProxy.Verify(x => x.SendCoreAsync(
            "BranchIdGenerationFailed",
            It.Is<object?[]>(args => args[0]!.Equals(issueId) && args[1]!.Equals(projectId) &&
                                     args[2]!.Equals("Generation timed out")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task QueueBranchIdGenerationAsync_DuplicateRequest_IgnoresSecondRequest()
    {
        // Arrange
        var issueId = "issue123";
        var projectId = "proj123";
        var title = "Fix authentication bug";

        _mockBranchIdGenerator.Setup(x => x.GenerateAsync(title, It.IsAny<CancellationToken>()))
            .Returns(async (string t, CancellationToken ct) =>
            {
                await Task.Delay(200, ct); // Simulate slow generation
                return new BranchIdResult(true, "fix-auth-bug", null, true);
            });

        // Act
        await _service.QueueBranchIdGenerationAsync(issueId, projectId, title);
        await _service.QueueBranchIdGenerationAsync(issueId, projectId, title); // Duplicate request

        // Wait for background tasks
        await Task.Delay(300);

        // Assert
        _mockBranchIdGenerator.Verify(x => x.GenerateAsync(title, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion
}
