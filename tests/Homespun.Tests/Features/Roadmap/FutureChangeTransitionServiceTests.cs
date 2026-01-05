using Homespun.Features.OpenCode.Hubs;
using Homespun.Features.Roadmap;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.Roadmap;

[TestFixture]
public class FutureChangeTransitionServiceTests
{
    private Mock<IRoadmapService> _mockRoadmapService = null!;
    private Mock<IHubContext<AgentHub>> _mockHubContext = null!;
    private Mock<ILogger<FutureChangeTransitionService>> _mockLogger = null!;
    private Mock<IClientProxy> _mockClientProxy = null!;
    private FutureChangeTransitionService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _mockRoadmapService = new Mock<IRoadmapService>();
        _mockHubContext = new Mock<IHubContext<AgentHub>>();
        _mockLogger = new Mock<ILogger<FutureChangeTransitionService>>();
        
        // Setup hub context mocks
        var mockClients = new Mock<IHubClients>();
        _mockClientProxy = new Mock<IClientProxy>();
        mockClients.Setup(c => c.All).Returns(_mockClientProxy.Object);
        _mockHubContext.Setup(h => h.Clients).Returns(mockClients.Object);
        
        _sut = new FutureChangeTransitionService(
            _mockRoadmapService.Object,
            _mockHubContext.Object,
            _mockLogger.Object);
    }

    #region TransitionToInProgressAsync Tests

    [Test]
    public async Task TransitionToInProgressAsync_WithPendingChange_TransitionsSuccessfully()
    {
        // Arrange
        var projectId = "project1";
        var changeId = "core/feature/test-change";
        var change = CreateChange(changeId, FutureChangeStatus.Pending);
        
        _mockRoadmapService.Setup(s => s.FindChangeByIdAsync(projectId, changeId))
            .ReturnsAsync(change);
        _mockRoadmapService.Setup(s => s.UpdateChangeStatusAsync(projectId, changeId, FutureChangeStatus.InProgress))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.TransitionToInProgressAsync(projectId, changeId);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.PreviousStatus, Is.EqualTo(FutureChangeStatus.Pending));
        Assert.That(result.NewStatus, Is.EqualTo(FutureChangeStatus.InProgress));
        _mockRoadmapService.Verify(s => s.UpdateChangeStatusAsync(projectId, changeId, FutureChangeStatus.InProgress), Times.Once);
    }

    [Test]
    public async Task TransitionToInProgressAsync_WithNonExistentChange_ReturnsFailure()
    {
        // Arrange
        var projectId = "project1";
        var changeId = "non-existent";
        
        _mockRoadmapService.Setup(s => s.FindChangeByIdAsync(projectId, changeId))
            .ReturnsAsync((FutureChange?)null);

        // Act
        var result = await _sut.TransitionToInProgressAsync(projectId, changeId);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("not found").IgnoreCase);
    }

    [Test]
    public async Task TransitionToInProgressAsync_WhenAlreadyInProgress_ReturnsFailure()
    {
        // Arrange
        var projectId = "project1";
        var changeId = "core/feature/test-change";
        var change = CreateChange(changeId, FutureChangeStatus.InProgress);
        
        _mockRoadmapService.Setup(s => s.FindChangeByIdAsync(projectId, changeId))
            .ReturnsAsync(change);

        // Act
        var result = await _sut.TransitionToInProgressAsync(projectId, changeId);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("InProgress"));
    }

    [Test]
    public async Task TransitionToInProgressAsync_WhenComplete_ReturnsFailure()
    {
        // Arrange
        var projectId = "project1";
        var changeId = "core/feature/test-change";
        var change = CreateChange(changeId, FutureChangeStatus.Complete);
        
        _mockRoadmapService.Setup(s => s.FindChangeByIdAsync(projectId, changeId))
            .ReturnsAsync(change);

        // Act
        var result = await _sut.TransitionToInProgressAsync(projectId, changeId);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("Complete"));
    }

    #endregion

    #region TransitionToAwaitingPRAsync Tests

    [Test]
    public async Task TransitionToAwaitingPRAsync_WithInProgressChange_TransitionsSuccessfully()
    {
        // Arrange
        var projectId = "project1";
        var changeId = "core/feature/test-change";
        var change = CreateChange(changeId, FutureChangeStatus.InProgress);
        
        _mockRoadmapService.Setup(s => s.FindChangeByIdAsync(projectId, changeId))
            .ReturnsAsync(change);
        _mockRoadmapService.Setup(s => s.UpdateChangeStatusAsync(projectId, changeId, FutureChangeStatus.AwaitingPR))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.TransitionToAwaitingPRAsync(projectId, changeId);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.PreviousStatus, Is.EqualTo(FutureChangeStatus.InProgress));
        Assert.That(result.NewStatus, Is.EqualTo(FutureChangeStatus.AwaitingPR));
    }

    [Test]
    public async Task TransitionToAwaitingPRAsync_WithPendingChange_AllowsDirectTransition()
    {
        // Arrange - Allow direct transition from Pending to AwaitingPR (for manual PR creation)
        var projectId = "project1";
        var changeId = "core/feature/test-change";
        var change = CreateChange(changeId, FutureChangeStatus.Pending);
        
        _mockRoadmapService.Setup(s => s.FindChangeByIdAsync(projectId, changeId))
            .ReturnsAsync(change);
        _mockRoadmapService.Setup(s => s.UpdateChangeStatusAsync(projectId, changeId, FutureChangeStatus.AwaitingPR))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.TransitionToAwaitingPRAsync(projectId, changeId);

        // Assert
        Assert.That(result.Success, Is.True);
    }

    [Test]
    public async Task TransitionToAwaitingPRAsync_WhenAlreadyComplete_ReturnsFailure()
    {
        // Arrange
        var projectId = "project1";
        var changeId = "core/feature/test-change";
        var change = CreateChange(changeId, FutureChangeStatus.Complete);
        
        _mockRoadmapService.Setup(s => s.FindChangeByIdAsync(projectId, changeId))
            .ReturnsAsync(change);

        // Act
        var result = await _sut.TransitionToAwaitingPRAsync(projectId, changeId);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("Complete"));
    }

    #endregion

    #region TransitionToCompleteAsync Tests

    [Test]
    public async Task TransitionToCompleteAsync_WithAwaitingPRChange_TransitionsSuccessfully()
    {
        // Arrange
        var projectId = "project1";
        var changeId = "core/feature/test-change";
        var change = CreateChange(changeId, FutureChangeStatus.AwaitingPR);
        
        _mockRoadmapService.Setup(s => s.FindChangeByIdAsync(projectId, changeId))
            .ReturnsAsync(change);
        _mockRoadmapService.Setup(s => s.UpdateChangeStatusAsync(projectId, changeId, FutureChangeStatus.Complete))
            .ReturnsAsync(true);
        _mockRoadmapService.Setup(s => s.RemoveParentReferenceAsync(projectId, changeId))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.TransitionToCompleteAsync(projectId, changeId);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.PreviousStatus, Is.EqualTo(FutureChangeStatus.AwaitingPR));
        Assert.That(result.NewStatus, Is.EqualTo(FutureChangeStatus.Complete));
    }

    [Test]
    public async Task TransitionToCompleteAsync_RemovesParentReference()
    {
        // Arrange
        var projectId = "project1";
        var changeId = "core/feature/test-change";
        var change = CreateChange(changeId, FutureChangeStatus.AwaitingPR);
        
        _mockRoadmapService.Setup(s => s.FindChangeByIdAsync(projectId, changeId))
            .ReturnsAsync(change);
        _mockRoadmapService.Setup(s => s.UpdateChangeStatusAsync(projectId, changeId, FutureChangeStatus.Complete))
            .ReturnsAsync(true);
        _mockRoadmapService.Setup(s => s.RemoveParentReferenceAsync(projectId, changeId))
            .ReturnsAsync(true);

        // Act
        await _sut.TransitionToCompleteAsync(projectId, changeId);

        // Assert
        _mockRoadmapService.Verify(s => s.RemoveParentReferenceAsync(projectId, changeId), Times.Once);
    }

    [Test]
    public async Task TransitionToCompleteAsync_WithInProgressChange_AllowsDirectTransition()
    {
        // Arrange - Allow direct transition for cases where PR detection was missed
        var projectId = "project1";
        var changeId = "core/feature/test-change";
        var change = CreateChange(changeId, FutureChangeStatus.InProgress);
        
        _mockRoadmapService.Setup(s => s.FindChangeByIdAsync(projectId, changeId))
            .ReturnsAsync(change);
        _mockRoadmapService.Setup(s => s.UpdateChangeStatusAsync(projectId, changeId, FutureChangeStatus.Complete))
            .ReturnsAsync(true);
        _mockRoadmapService.Setup(s => s.RemoveParentReferenceAsync(projectId, changeId))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.TransitionToCompleteAsync(projectId, changeId);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.NewStatus, Is.EqualTo(FutureChangeStatus.Complete));
    }

    [Test]
    public async Task TransitionToCompleteAsync_WhenAlreadyComplete_ReturnsFailure()
    {
        // Arrange
        var projectId = "project1";
        var changeId = "core/feature/test-change";
        var change = CreateChange(changeId, FutureChangeStatus.Complete);
        
        _mockRoadmapService.Setup(s => s.FindChangeByIdAsync(projectId, changeId))
            .ReturnsAsync(change);

        // Act
        var result = await _sut.TransitionToCompleteAsync(projectId, changeId);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("already").IgnoreCase);
    }

    #endregion

    #region HandleAgentFailureAsync Tests

    [Test]
    public async Task HandleAgentFailureAsync_WithInProgressChange_RevertsToPending()
    {
        // Arrange
        var projectId = "project1";
        var changeId = "core/feature/test-change";
        var error = "Agent crashed unexpectedly";
        var change = CreateChange(changeId, FutureChangeStatus.InProgress);
        
        _mockRoadmapService.Setup(s => s.FindChangeByIdAsync(projectId, changeId))
            .ReturnsAsync(change);
        _mockRoadmapService.Setup(s => s.UpdateChangeStatusAsync(projectId, changeId, FutureChangeStatus.Pending))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.HandleAgentFailureAsync(projectId, changeId, error);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.PreviousStatus, Is.EqualTo(FutureChangeStatus.InProgress));
        Assert.That(result.NewStatus, Is.EqualTo(FutureChangeStatus.Pending));
    }

    [Test]
    public async Task HandleAgentFailureAsync_WithAwaitingPRChange_RevertsToPending()
    {
        // Arrange
        var projectId = "project1";
        var changeId = "core/feature/test-change";
        var error = "PR creation failed";
        var change = CreateChange(changeId, FutureChangeStatus.AwaitingPR);
        
        _mockRoadmapService.Setup(s => s.FindChangeByIdAsync(projectId, changeId))
            .ReturnsAsync(change);
        _mockRoadmapService.Setup(s => s.UpdateChangeStatusAsync(projectId, changeId, FutureChangeStatus.Pending))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.HandleAgentFailureAsync(projectId, changeId, error);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.NewStatus, Is.EqualTo(FutureChangeStatus.Pending));
    }

    [Test]
    public async Task HandleAgentFailureAsync_WithPendingChange_NoOpButSucceeds()
    {
        // Arrange - Already pending, nothing to revert
        var projectId = "project1";
        var changeId = "core/feature/test-change";
        var error = "Some error";
        var change = CreateChange(changeId, FutureChangeStatus.Pending);
        
        _mockRoadmapService.Setup(s => s.FindChangeByIdAsync(projectId, changeId))
            .ReturnsAsync(change);

        // Act
        var result = await _sut.HandleAgentFailureAsync(projectId, changeId, error);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.PreviousStatus, Is.EqualTo(FutureChangeStatus.Pending));
        Assert.That(result.NewStatus, Is.EqualTo(FutureChangeStatus.Pending));
        _mockRoadmapService.Verify(s => s.UpdateChangeStatusAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<FutureChangeStatus>()), Times.Never);
    }

    [Test]
    public async Task HandleAgentFailureAsync_WithCompleteChange_ReturnsFailure()
    {
        // Arrange - Can't revert a completed change
        var projectId = "project1";
        var changeId = "core/feature/test-change";
        var error = "Some error";
        var change = CreateChange(changeId, FutureChangeStatus.Complete);
        
        _mockRoadmapService.Setup(s => s.FindChangeByIdAsync(projectId, changeId))
            .ReturnsAsync(change);

        // Act
        var result = await _sut.HandleAgentFailureAsync(projectId, changeId, error);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("complete").IgnoreCase);
    }

    #endregion

    #region GetStatusAsync Tests

    [Test]
    public async Task GetStatusAsync_WithExistingChange_ReturnsStatus()
    {
        // Arrange
        var projectId = "project1";
        var changeId = "core/feature/test-change";
        var change = CreateChange(changeId, FutureChangeStatus.InProgress);
        
        _mockRoadmapService.Setup(s => s.FindChangeByIdAsync(projectId, changeId))
            .ReturnsAsync(change);

        // Act
        var result = await _sut.GetStatusAsync(projectId, changeId);

        // Assert
        Assert.That(result, Is.EqualTo(FutureChangeStatus.InProgress));
    }

    [Test]
    public async Task GetStatusAsync_WithNonExistentChange_ReturnsNull()
    {
        // Arrange
        var projectId = "project1";
        var changeId = "non-existent";
        
        _mockRoadmapService.Setup(s => s.FindChangeByIdAsync(projectId, changeId))
            .ReturnsAsync((FutureChange?)null);

        // Act
        var result = await _sut.GetStatusAsync(projectId, changeId);

        // Assert
        Assert.That(result, Is.Null);
    }

    #endregion

    #region SignalR Notification Tests

    [Test]
    public async Task TransitionToInProgressAsync_BroadcastsStatusChange()
    {
        // Arrange
        var projectId = "project1";
        var changeId = "core/feature/test-change";
        var change = CreateChange(changeId, FutureChangeStatus.Pending);
        
        _mockRoadmapService.Setup(s => s.FindChangeByIdAsync(projectId, changeId))
            .ReturnsAsync(change);
        _mockRoadmapService.Setup(s => s.UpdateChangeStatusAsync(projectId, changeId, FutureChangeStatus.InProgress))
            .ReturnsAsync(true);

        // Act
        await _sut.TransitionToInProgressAsync(projectId, changeId);

        // Assert
        _mockClientProxy.Verify(
            c => c.SendCoreAsync(
                "FutureChangeStatusChanged",
                It.Is<object?[]>(args => 
                    args.Length == 3 && 
                    (string)args[0]! == projectId && 
                    (string)args[1]! == changeId),
                default),
            Times.Once);
    }

    #endregion

    private static FutureChange CreateChange(string id, FutureChangeStatus status)
    {
        return new FutureChange
        {
            Id = id,
            ShortTitle = "test-change",
            Group = "core",
            Type = ChangeType.Feature,
            Title = "Test Change",
            Status = status
        };
    }
}
