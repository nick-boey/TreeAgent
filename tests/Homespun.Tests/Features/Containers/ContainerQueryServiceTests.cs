using Fleece.Core.Models;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Containers.Services;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Projects;
using Homespun.Shared.Models.Projects;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Homespun.Tests.Features.Containers;

/// <summary>
/// Unit tests for ContainerQueryService.
/// Tests container enrichment with project and issue information.
/// </summary>
[TestFixture]
public class ContainerQueryServiceTests
{
    private Mock<IAgentExecutionService> _agentExecutionServiceMock = null!;
    private Mock<IProjectService> _projectServiceMock = null!;
    private Mock<IProjectFleeceService> _fleeceServiceMock = null!;
    private Mock<IClaudeSessionService> _sessionServiceMock = null!;
    private Mock<ILogger<ContainerQueryService>> _loggerMock = null!;
    private ContainerQueryService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _agentExecutionServiceMock = new Mock<IAgentExecutionService>();
        _projectServiceMock = new Mock<IProjectService>();
        _fleeceServiceMock = new Mock<IProjectFleeceService>();
        _sessionServiceMock = new Mock<IClaudeSessionService>();
        _loggerMock = new Mock<ILogger<ContainerQueryService>>();

        _service = new ContainerQueryService(
            _agentExecutionServiceMock.Object,
            _projectServiceMock.Object,
            _fleeceServiceMock.Object,
            _sessionServiceMock.Object,
            _loggerMock.Object);
    }

    #region Container Name Parsing Tests

    [Test]
    public void TryParseIssueContainerName_ValidName_ReturnsTrueAndExtractsIds()
    {
        // Arrange
        var containerName = "homespun-issue-project-1-abc123";

        // Act
        var result = ContainerQueryService.TryParseIssueContainerName(
            containerName, out var projectId, out var issueId);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(projectId, Is.EqualTo("project-1"));
            Assert.That(issueId, Is.EqualTo("abc123"));
        });
    }

    [Test]
    public void TryParseIssueContainerName_ValidNameWithComplexProjectId_ExtractsCorrectly()
    {
        // Arrange - project ID with multiple hyphens
        var containerName = "homespun-issue-my-cool-project-xyz789";

        // Act
        var result = ContainerQueryService.TryParseIssueContainerName(
            containerName, out var projectId, out var issueId);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(projectId, Is.EqualTo("my-cool-project"));
            Assert.That(issueId, Is.EqualTo("xyz789"));
        });
    }

    [Test]
    public void TryParseIssueContainerName_NonIssueContainer_ReturnsFalse()
    {
        // Arrange
        var containerName = "homespun-worker-abc123";

        // Act
        var result = ContainerQueryService.TryParseIssueContainerName(
            containerName, out var projectId, out var issueId);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False);
            Assert.That(projectId, Is.Null);
            Assert.That(issueId, Is.Null);
        });
    }

    [Test]
    public void TryParseIssueContainerName_RandomContainerName_ReturnsFalse()
    {
        // Arrange
        var containerName = "postgres-db";

        // Act
        var result = ContainerQueryService.TryParseIssueContainerName(
            containerName, out var projectId, out var issueId);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False);
            Assert.That(projectId, Is.Null);
            Assert.That(issueId, Is.Null);
        });
    }

    [Test]
    public void TryParseIssueContainerName_EmptyString_ReturnsFalse()
    {
        // Act
        var result = ContainerQueryService.TryParseIssueContainerName(
            "", out var projectId, out var issueId);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False);
            Assert.That(projectId, Is.Null);
            Assert.That(issueId, Is.Null);
        });
    }

    [Test]
    public void TryParseIssueContainerName_NullString_ReturnsFalse()
    {
        // Act
        var result = ContainerQueryService.TryParseIssueContainerName(
            null!, out var projectId, out var issueId);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False);
            Assert.That(projectId, Is.Null);
            Assert.That(issueId, Is.Null);
        });
    }

    [Test]
    public void TryParseIssueContainerName_OnlyPrefix_ReturnsFalse()
    {
        // Arrange - only the prefix with no project or issue
        var containerName = "homespun-issue-";

        // Act
        var result = ContainerQueryService.TryParseIssueContainerName(
            containerName, out var projectId, out var issueId);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False);
            Assert.That(projectId, Is.Null);
            Assert.That(issueId, Is.Null);
        });
    }

    [Test]
    public void TryParseIssueContainerName_OnlyProjectNoIssue_ReturnsFalse()
    {
        // Arrange - project ID but no trailing issue ID (ends with hyphen or no hyphen)
        var containerName = "homespun-issue-project";

        // Act
        var result = ContainerQueryService.TryParseIssueContainerName(
            containerName, out var projectId, out var issueId);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False);
            Assert.That(projectId, Is.Null);
            Assert.That(issueId, Is.Null);
        });
    }

    #endregion

    #region Container Enrichment Tests

    [Test]
    public async Task GetAllContainersAsync_WithProjectIdInContainerInfo_UsesProjectIdDirectly()
    {
        // Arrange
        var project = new Project
        {
            Id = "proj-123",
            Name = "Test Project",
            LocalPath = "/data/projects/test-project",
            DefaultBranch = "main"
        };

        var container = new ContainerInfo(
            ContainerId: "container-abc",
            ContainerName: "homespun-issue-proj-123-issue456",
            WorkingDirectory: "/some/other/path",
            ProjectId: "proj-123",
            IssueId: "issue456",
            CreatedAt: DateTime.UtcNow,
            State: null);

        _agentExecutionServiceMock
            .Setup(x => x.ListContainersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ContainerInfo> { container });

        _projectServiceMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(new List<Project> { project });

        _projectServiceMock
            .Setup(x => x.GetByIdAsync("proj-123"))
            .ReturnsAsync(project);

        _fleeceServiceMock
            .Setup(x => x.GetIssueAsync(project.LocalPath, "issue456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestIssue("issue456", "Fix the bug"));

        // Act
        var result = await _service.GetAllContainersAsync();

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        var dto = result[0];
        Assert.Multiple(() =>
        {
            Assert.That(dto.ProjectId, Is.EqualTo("proj-123"));
            Assert.That(dto.ProjectName, Is.EqualTo("Test Project"));
            Assert.That(dto.IssueId, Is.EqualTo("issue456"));
            Assert.That(dto.IssueTitle, Is.EqualTo("Fix the bug"));
        });
    }

    [Test]
    public async Task GetAllContainersAsync_WithoutProjectId_ParsesContainerName()
    {
        // Arrange
        var project = new Project
        {
            Id = "proj-123",
            Name = "Test Project",
            LocalPath = "/data/projects/test-project",
            DefaultBranch = "main"
        };

        var container = new ContainerInfo(
            ContainerId: "container-abc",
            ContainerName: "homespun-issue-proj-123-issue456",
            WorkingDirectory: "/some/other/path",
            ProjectId: null,
            IssueId: "issue456",
            CreatedAt: DateTime.UtcNow,
            State: null);

        _agentExecutionServiceMock
            .Setup(x => x.ListContainersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ContainerInfo> { container });

        _projectServiceMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(new List<Project> { project });

        _projectServiceMock
            .Setup(x => x.GetByIdAsync("proj-123"))
            .ReturnsAsync(project);

        _fleeceServiceMock
            .Setup(x => x.GetIssueAsync(project.LocalPath, "issue456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestIssue("issue456", "Fix the bug"));

        // Act
        var result = await _service.GetAllContainersAsync();

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        var dto = result[0];
        Assert.Multiple(() =>
        {
            Assert.That(dto.ProjectId, Is.EqualTo("proj-123"));
            Assert.That(dto.ProjectName, Is.EqualTo("Test Project"));
            Assert.That(dto.IssueId, Is.EqualTo("issue456"));
            Assert.That(dto.IssueTitle, Is.EqualTo("Fix the bug"));
        });
    }

    [Test]
    public async Task GetAllContainersAsync_ProjectNotFound_ReturnsNullProjectInfo()
    {
        // Arrange
        var container = new ContainerInfo(
            ContainerId: "container-abc",
            ContainerName: "homespun-issue-unknown-proj-issue456",
            WorkingDirectory: "/some/path",
            ProjectId: "unknown-proj",
            IssueId: "issue456",
            CreatedAt: DateTime.UtcNow,
            State: null);

        _agentExecutionServiceMock
            .Setup(x => x.ListContainersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ContainerInfo> { container });

        _projectServiceMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(new List<Project>());

        _projectServiceMock
            .Setup(x => x.GetByIdAsync("unknown-proj"))
            .ReturnsAsync((Project?)null);

        // Act
        var result = await _service.GetAllContainersAsync();

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        var dto = result[0];
        Assert.Multiple(() =>
        {
            Assert.That(dto.ProjectId, Is.Null);
            Assert.That(dto.ProjectName, Is.Null);
            Assert.That(dto.IssueId, Is.EqualTo("issue456"));
            Assert.That(dto.IssueTitle, Is.Null);
        });
    }

    [Test]
    public async Task GetAllContainersAsync_FleeceServiceThrows_GracefullyHandlesError()
    {
        // Arrange
        var project = new Project
        {
            Id = "proj-123",
            Name = "Test Project",
            LocalPath = "/data/projects/test-project",
            DefaultBranch = "main"
        };

        var container = new ContainerInfo(
            ContainerId: "container-abc",
            ContainerName: "homespun-issue-proj-123-issue456",
            WorkingDirectory: "/some/path",
            ProjectId: "proj-123",
            IssueId: "issue456",
            CreatedAt: DateTime.UtcNow,
            State: null);

        _agentExecutionServiceMock
            .Setup(x => x.ListContainersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ContainerInfo> { container });

        _projectServiceMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(new List<Project> { project });

        _projectServiceMock
            .Setup(x => x.GetByIdAsync("proj-123"))
            .ReturnsAsync(project);

        _fleeceServiceMock
            .Setup(x => x.GetIssueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Fleece service error"));

        // Act
        var result = await _service.GetAllContainersAsync();

        // Assert - should still return container with project info but no issue title
        Assert.That(result, Has.Count.EqualTo(1));
        var dto = result[0];
        Assert.Multiple(() =>
        {
            Assert.That(dto.ProjectId, Is.EqualTo("proj-123"));
            Assert.That(dto.ProjectName, Is.EqualTo("Test Project"));
            Assert.That(dto.IssueId, Is.EqualTo("issue456"));
            Assert.That(dto.IssueTitle, Is.Null);
        });
    }

    #endregion

    #region Helper Methods

    private static Issue CreateTestIssue(string id, string title)
    {
        return new Issue
        {
            Id = id,
            Title = title,
            Status = IssueStatus.Open,
            Type = IssueType.Task,
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdate = DateTimeOffset.UtcNow
        };
    }

    #endregion
}
