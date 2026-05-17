using Fleece.Core.Models;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Navigation;
using Homespun.Features.Projects;
using Moq;

namespace Homespun.Tests.Features.Navigation;

/// <summary>
/// Unit tests for BreadcrumbService.
/// Tests follow TDD approach - written before implementation.
/// </summary>
[TestFixture]
public class BreadcrumbServiceTests
{
    private Mock<IProjectService> _mockProjectService = null!;
    private Mock<IProjectFleeceService> _mockFleeceService = null!;
    private BreadcrumbService _sut = null!;

    [SetUp]
    public void Setup()
    {
        _mockProjectService = new Mock<IProjectService>();
        _mockFleeceService = new Mock<IProjectFleeceService>();
        _sut = new BreadcrumbService(_mockProjectService.Object, _mockFleeceService.Object);
    }

    [Test]
    public void Breadcrumbs_WhenEmpty_ReturnsEmptyList()
    {
        // Assert
        Assert.That(_sut.Breadcrumbs, Is.Empty);
    }

    [Test]
    public async Task SetContextAsync_WithProjectsPage_ShowsProjectsBreadcrumb()
    {
        // Arrange
        var context = new BreadcrumbContext { PageName = "Projects" };

        // Act
        await _sut.SetContextAsync(context);

        // Assert
        Assert.That(_sut.Breadcrumbs, Has.Count.EqualTo(1));
        Assert.That(_sut.Breadcrumbs[0].Title, Is.EqualTo("Projects"));
        Assert.That(_sut.Breadcrumbs[0].Url, Is.EqualTo("/projects"));
    }

    [Test]
    public async Task SetContextAsync_WithProjectId_ShowsProjectName()
    {
        // Arrange
        var project = new Project
        {
            Id = "proj-123",
            Name = "My Project",
            LocalPath = "/path/to/project",
            DefaultBranch = "main"
        };
        _mockProjectService
            .Setup(s => s.GetByIdAsync("proj-123"))
            .ReturnsAsync(project);

        var context = new BreadcrumbContext { ProjectId = "proj-123" };

        // Act
        await _sut.SetContextAsync(context);

        // Assert
        Assert.That(_sut.Breadcrumbs, Has.Count.EqualTo(2));
        Assert.That(_sut.Breadcrumbs[0].Title, Is.EqualTo("Projects"));
        Assert.That(_sut.Breadcrumbs[0].Url, Is.EqualTo("/projects"));
        Assert.That(_sut.Breadcrumbs[1].Title, Is.EqualTo("My Project"));
        Assert.That(_sut.Breadcrumbs[1].Url, Is.EqualTo("/projects/proj-123"));
    }

    [Test]
    public async Task SetContextAsync_WithProjectIdAndPageTitle_ShowsBothWithPageTitleLast()
    {
        // Arrange
        var project = new Project
        {
            Id = "proj-123",
            Name = "My Project",
            LocalPath = "/path/to/project",
            DefaultBranch = "main"
        };
        _mockProjectService
            .Setup(s => s.GetByIdAsync("proj-123"))
            .ReturnsAsync(project);

        var context = new BreadcrumbContext
        {
            ProjectId = "proj-123",
            PageName = "Edit"
        };

        // Act
        await _sut.SetContextAsync(context);

        // Assert
        Assert.That(_sut.Breadcrumbs, Has.Count.EqualTo(3));
        Assert.That(_sut.Breadcrumbs[0].Title, Is.EqualTo("Projects"));
        Assert.That(_sut.Breadcrumbs[1].Title, Is.EqualTo("My Project"));
        Assert.That(_sut.Breadcrumbs[2].Title, Is.EqualTo("Edit"));
        Assert.That(_sut.Breadcrumbs[2].Url, Is.Null); // Last item has no URL
    }

    [Test]
    public async Task SetContextAsync_WithProjectAndIssue_ShowsBothNames()
    {
        // Arrange
        var project = new Project
        {
            Id = "proj-123",
            Name = "My Project",
            LocalPath = "/path/to/project",
            DefaultBranch = "main"
        };
        var issue = new Issue
        {
            Id = "iss-456",
            Title = "Fix the breadcrumbs",
            Type = IssueType.Bug,
            Status = IssueStatus.Progress,
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdate = DateTimeOffset.UtcNow
        };

        _mockProjectService
            .Setup(s => s.GetByIdAsync("proj-123"))
            .ReturnsAsync(project);
        _mockFleeceService
            .Setup(s => s.GetIssueAsync("/path/to/project", "iss-456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(issue);

        var context = new BreadcrumbContext
        {
            ProjectId = "proj-123",
            IssueId = "iss-456"
        };

        // Act
        await _sut.SetContextAsync(context);

        // Assert
        Assert.That(_sut.Breadcrumbs, Has.Count.EqualTo(4));
        Assert.That(_sut.Breadcrumbs[0].Title, Is.EqualTo("Projects"));
        Assert.That(_sut.Breadcrumbs[1].Title, Is.EqualTo("My Project"));
        Assert.That(_sut.Breadcrumbs[2].Title, Is.EqualTo("Issues"));
        Assert.That(_sut.Breadcrumbs[3].Title, Is.EqualTo("Fix the breadcrumbs"));
    }

    [Test]
    public async Task SetContextAsync_WithProjectIssueAndEdit_ShowsFullChain()
    {
        // Arrange
        var project = new Project
        {
            Id = "proj-123",
            Name = "Homespun",
            LocalPath = "/path/to/project",
            DefaultBranch = "main"
        };
        var issue = new Issue
        {
            Id = "12rMan",
            Title = "Fix breadcrumbs",
            Type = IssueType.Bug,
            Status = IssueStatus.Progress,
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdate = DateTimeOffset.UtcNow
        };

        _mockProjectService
            .Setup(s => s.GetByIdAsync("proj-123"))
            .ReturnsAsync(project);
        _mockFleeceService
            .Setup(s => s.GetIssueAsync("/path/to/project", "12rMan", It.IsAny<CancellationToken>()))
            .ReturnsAsync(issue);

        var context = new BreadcrumbContext
        {
            ProjectId = "proj-123",
            IssueId = "12rMan",
            PageName = "Edit"
        };

        // Act
        await _sut.SetContextAsync(context);

        // Assert - Expected: Homespun / Projects / Homespun / Issues / Fix breadcrumbs / Edit
        Assert.That(_sut.Breadcrumbs, Has.Count.EqualTo(5));
        Assert.That(_sut.Breadcrumbs[0].Title, Is.EqualTo("Projects"));
        Assert.That(_sut.Breadcrumbs[1].Title, Is.EqualTo("Homespun"));
        Assert.That(_sut.Breadcrumbs[2].Title, Is.EqualTo("Issues"));
        Assert.That(_sut.Breadcrumbs[3].Title, Is.EqualTo("Fix breadcrumbs"));
        Assert.That(_sut.Breadcrumbs[4].Title, Is.EqualTo("Edit"));
        Assert.That(_sut.Breadcrumbs[4].Url, Is.Null); // Last item has no URL
    }

    [Test]
    public async Task SetContextAsync_WithNonExistentProject_ShowsFallbackWithId()
    {
        // Arrange
        _mockProjectService
            .Setup(s => s.GetByIdAsync("unknown-id"))
            .ReturnsAsync((Project?)null);

        var context = new BreadcrumbContext { ProjectId = "unknown-id" };

        // Act
        await _sut.SetContextAsync(context);

        // Assert - Gracefully handles missing project
        Assert.That(_sut.Breadcrumbs, Has.Count.EqualTo(2));
        Assert.That(_sut.Breadcrumbs[0].Title, Is.EqualTo("Projects"));
        Assert.That(_sut.Breadcrumbs[1].Title, Is.EqualTo("unknown-id"));
    }

    [Test]
    public async Task SetContextAsync_WithNonExistentIssue_ShowsFallbackWithId()
    {
        // Arrange
        var project = new Project
        {
            Id = "proj-123",
            Name = "My Project",
            LocalPath = "/path/to/project",
            DefaultBranch = "main"
        };

        _mockProjectService
            .Setup(s => s.GetByIdAsync("proj-123"))
            .ReturnsAsync(project);
        _mockFleeceService
            .Setup(s => s.GetIssueAsync("/path/to/project", "unknown-issue", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Issue?)null);

        var context = new BreadcrumbContext
        {
            ProjectId = "proj-123",
            IssueId = "unknown-issue"
        };

        // Act
        await _sut.SetContextAsync(context);

        // Assert - Gracefully handles missing issue
        Assert.That(_sut.Breadcrumbs, Has.Count.EqualTo(4));
        Assert.That(_sut.Breadcrumbs[2].Title, Is.EqualTo("Issues"));
        Assert.That(_sut.Breadcrumbs[3].Title, Is.EqualTo("unknown-issue"));
    }

    [Test]
    public void ClearContext_RemovesBreadcrumbs()
    {
        // Arrange - Set some breadcrumbs first
        _sut.SetContextAsync(new BreadcrumbContext { PageName = "Settings" }).Wait();
        Assert.That(_sut.Breadcrumbs, Is.Not.Empty);

        // Act
        _sut.ClearContext();

        // Assert
        Assert.That(_sut.Breadcrumbs, Is.Empty);
    }

    [Test]
    public async Task OnBreadcrumbsChanged_FiresWhenContextSet()
    {
        // Arrange
        var eventFired = false;
        _sut.OnBreadcrumbsChanged += () => eventFired = true;

        // Act
        await _sut.SetContextAsync(new BreadcrumbContext { PageName = "Settings" });

        // Assert
        Assert.That(eventFired, Is.True);
    }

    [Test]
    public void OnBreadcrumbsChanged_FiresWhenContextCleared()
    {
        // Arrange
        _sut.SetContextAsync(new BreadcrumbContext { PageName = "Settings" }).Wait();
        var eventFired = false;
        _sut.OnBreadcrumbsChanged += () => eventFired = true;

        // Act
        _sut.ClearContext();

        // Assert
        Assert.That(eventFired, Is.True);
    }

    [Test]
    public async Task SetContextAsync_WithPullRequestsCreate_ShowsCorrectChain()
    {
        // Arrange
        var project = new Project
        {
            Id = "proj-123",
            Name = "My Project",
            LocalPath = "/path/to/project",
            DefaultBranch = "main"
        };

        _mockProjectService
            .Setup(s => s.GetByIdAsync("proj-123"))
            .ReturnsAsync(project);

        var context = new BreadcrumbContext
        {
            ProjectId = "proj-123",
            PageName = "New Pull Request"
        };

        // Act
        await _sut.SetContextAsync(context);

        // Assert
        Assert.That(_sut.Breadcrumbs, Has.Count.EqualTo(3));
        Assert.That(_sut.Breadcrumbs[0].Title, Is.EqualTo("Projects"));
        Assert.That(_sut.Breadcrumbs[1].Title, Is.EqualTo("My Project"));
        Assert.That(_sut.Breadcrumbs[2].Title, Is.EqualTo("New Pull Request"));
    }

    [Test]
    public async Task SetContextAsync_WithSettingsPage_ShowsSettingsBreadcrumb()
    {
        // Arrange
        var context = new BreadcrumbContext { PageName = "Settings" };

        // Act
        await _sut.SetContextAsync(context);

        // Assert
        Assert.That(_sut.Breadcrumbs, Has.Count.EqualTo(1));
        Assert.That(_sut.Breadcrumbs[0].Title, Is.EqualTo("Settings"));
        Assert.That(_sut.Breadcrumbs[0].Url, Is.EqualTo("/settings"));
    }

    [Test]
    public async Task SetContextAsync_WithAgentsPage_ShowsAgentsBreadcrumb()
    {
        // Arrange
        var context = new BreadcrumbContext { PageName = "Agents" };

        // Act
        await _sut.SetContextAsync(context);

        // Assert
        Assert.That(_sut.Breadcrumbs, Has.Count.EqualTo(1));
        Assert.That(_sut.Breadcrumbs[0].Title, Is.EqualTo("Agents"));
        Assert.That(_sut.Breadcrumbs[0].Url, Is.EqualTo("/agents"));
    }

    [Test]
    public async Task SetContextAsync_WithSessionId_ShowsSessionBreadcrumbs()
    {
        // Arrange
        var context = new BreadcrumbContext { SessionId = "abc12345-session-id" };

        // Act
        await _sut.SetContextAsync(context);

        // Assert
        Assert.That(_sut.Breadcrumbs, Has.Count.EqualTo(2));
        Assert.That(_sut.Breadcrumbs[0].Title, Is.EqualTo("Agents"));
        Assert.That(_sut.Breadcrumbs[0].Url, Is.EqualTo("/agents"));
        Assert.That(_sut.Breadcrumbs[1].Title, Is.EqualTo("abc12345...")); // Truncated
        Assert.That(_sut.Breadcrumbs[1].Url, Is.Null);
    }

    [Test]
    public async Task SetContextAsync_WithShortSessionId_ShowsFullId()
    {
        // Arrange
        var context = new BreadcrumbContext { SessionId = "abc123" };

        // Act
        await _sut.SetContextAsync(context);

        // Assert
        Assert.That(_sut.Breadcrumbs, Has.Count.EqualTo(2));
        Assert.That(_sut.Breadcrumbs[1].Title, Is.EqualTo("abc123")); // Not truncated
    }

    [Test]
    public async Task SetContextAsync_WithSessionIdAndPageName_ShowsFullChain()
    {
        // Arrange
        var context = new BreadcrumbContext
        {
            SessionId = "abc12345-session-id",
            PageName = "Archived"
        };

        // Act
        await _sut.SetContextAsync(context);

        // Assert
        Assert.That(_sut.Breadcrumbs, Has.Count.EqualTo(3));
        Assert.That(_sut.Breadcrumbs[0].Title, Is.EqualTo("Agents"));
        Assert.That(_sut.Breadcrumbs[0].Url, Is.EqualTo("/agents"));
        Assert.That(_sut.Breadcrumbs[1].Title, Is.EqualTo("abc12345..."));
        Assert.That(_sut.Breadcrumbs[1].Url, Is.EqualTo("/session/abc12345-session-id"));
        Assert.That(_sut.Breadcrumbs[2].Title, Is.EqualTo("Archived"));
        Assert.That(_sut.Breadcrumbs[2].Url, Is.Null);
    }

    [Test]
    public async Task SetContextAsync_WithPromptsPage_ShowsPromptsBreadcrumb()
    {
        // Arrange
        var context = new BreadcrumbContext { PageName = "Prompts" };

        // Act
        await _sut.SetContextAsync(context);

        // Assert
        Assert.That(_sut.Breadcrumbs, Has.Count.EqualTo(1));
        Assert.That(_sut.Breadcrumbs[0].Title, Is.EqualTo("Prompts"));
        Assert.That(_sut.Breadcrumbs[0].Url, Is.EqualTo("/prompts"));
    }
}
