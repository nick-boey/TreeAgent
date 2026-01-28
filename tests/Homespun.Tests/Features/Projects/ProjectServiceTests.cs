using Homespun.Features.Commands;
using Homespun.Features.GitHub;
using Homespun.Features.Projects;
using Homespun.Features.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.Projects;

[TestFixture]
public class ProjectServiceTests
{
    private MockDataStore _dataStore = null!;
    private Mock<IGitHubService> _mockGitHubService = null!;
    private Mock<ICommandRunner> _mockCommandRunner = null!;
    private Mock<IConfiguration> _mockConfiguration = null!;
    private Mock<ILogger<ProjectService>> _mockLogger = null!;
    private ProjectService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _dataStore = new MockDataStore();
        _mockGitHubService = new Mock<IGitHubService>();
        _mockCommandRunner = new Mock<ICommandRunner>();
        _mockConfiguration = new Mock<IConfiguration>();
        _mockLogger = new Mock<ILogger<ProjectService>>();

        // Default configuration - no overrides, use default paths
        _mockConfiguration.Setup(c => c[It.IsAny<string>()]).Returns((string?)null);

        _service = new ProjectService(
            _dataStore,
            _mockGitHubService.Object,
            _mockCommandRunner.Object,
            _mockConfiguration.Object,
            _mockLogger.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _dataStore.Clear();
    }

    #region CreateAsync Tests

    [Test]
    public async Task CreateAsync_ValidOwnerRepo_ReturnsSuccessWithProject()
    {
        // Arrange
        _mockGitHubService.Setup(s => s.GetDefaultBranchAsync("microsoft", "vscode"))
            .ReturnsAsync("main");
        _mockCommandRunner.Setup(r => r.RunAsync("git", It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new CommandResult { Success = true });

        // Act
        var result = await _service.CreateAsync("microsoft/vscode");

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.Project, Is.Not.Null);
        Assert.That(result.Project!.Name, Is.EqualTo("vscode"));
        Assert.That(result.Project.GitHubOwner, Is.EqualTo("microsoft"));
        Assert.That(result.Project.GitHubRepo, Is.EqualTo("vscode"));
        Assert.That(result.Project.DefaultBranch, Is.EqualTo("main"));
    }

    [Test]
    public async Task CreateAsync_ValidOwnerRepo_SetsCorrectLocalPath()
    {
        // Arrange
        _mockGitHubService.Setup(s => s.GetDefaultBranchAsync("owner", "repo"))
            .ReturnsAsync("main");
        _mockCommandRunner.Setup(r => r.RunAsync("git", It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new CommandResult { Success = true });

        // Act
        var result = await _service.CreateAsync("owner/repo");

        // Assert
        var expectedPathEnd = Path.Combine(".homespun", "src", "repo", "main");
        Assert.That(result.Project!.LocalPath, Does.EndWith(expectedPathEnd.Replace('/', Path.DirectorySeparatorChar)));
    }

    [Test]
    public async Task CreateAsync_InvalidFormat_ReturnsError()
    {
        // Arrange - no mocking needed, should fail validation

        // Act
        var result = await _service.CreateAsync("invalid-format");

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("Invalid format"));
    }

    [Test]
    public async Task CreateAsync_EmptyOwner_ReturnsError()
    {
        // Act
        var result = await _service.CreateAsync("/repo");

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("Invalid format"));
    }

    [Test]
    public async Task CreateAsync_EmptyRepo_ReturnsError()
    {
        // Act
        var result = await _service.CreateAsync("owner/");

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("Invalid format"));
    }

    [Test]
    public async Task CreateAsync_GitHubReturnsNull_ReturnsError()
    {
        // Arrange
        _mockGitHubService.Setup(s => s.GetDefaultBranchAsync("owner", "nonexistent"))
            .ReturnsAsync((string?)null);

        // Act
        var result = await _service.CreateAsync("owner/nonexistent");

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("Could not fetch repository"));
    }

    [Test]
    public async Task CreateAsync_CloneFails_ReturnsError()
    {
        // Arrange
        _mockGitHubService.Setup(s => s.GetDefaultBranchAsync("owner", "repo"))
            .ReturnsAsync("main");
        _mockCommandRunner.Setup(r => r.RunAsync("git", It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new CommandResult { Success = false, Error = "Clone failed" });

        // Act
        var result = await _service.CreateAsync("owner/repo");

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("Failed to clone"));
    }

    [Test]
    public async Task CreateAsync_Success_AddsProjectToDataStore()
    {
        // Arrange
        _mockGitHubService.Setup(s => s.GetDefaultBranchAsync("owner", "repo"))
            .ReturnsAsync("main");
        _mockCommandRunner.Setup(r => r.RunAsync("git", It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new CommandResult { Success = true });

        // Act
        await _service.CreateAsync("owner/repo");

        // Assert
        Assert.That(_dataStore.Projects, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task CreateAsync_WithNonMainDefaultBranch_UsesCorrectBranch()
    {
        // Arrange
        _mockGitHubService.Setup(s => s.GetDefaultBranchAsync("owner", "repo"))
            .ReturnsAsync("develop");
        _mockCommandRunner.Setup(r => r.RunAsync("git", It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new CommandResult { Success = true });

        // Act
        var result = await _service.CreateAsync("owner/repo");

        // Assert
        Assert.That(result.Project!.DefaultBranch, Is.EqualTo("develop"));
        Assert.That(result.Project.LocalPath, Does.Contain("develop"));
    }

    #endregion

    #region UpdateAsync Tests

    [Test]
    public async Task UpdateAsync_ValidProject_UpdatesDefaultModel()
    {
        // Arrange
        var project = new Homespun.Features.PullRequests.Data.Entities.Project
        {
            Name = "repo",
            LocalPath = "/path",
            GitHubOwner = "owner",
            GitHubRepo = "repo",
            DefaultBranch = "main"
        };
        await _dataStore.AddProjectAsync(project);

        // Act
        var result = await _service.UpdateAsync(project.Id, "anthropic/claude-sonnet-4-5");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.DefaultModel, Is.EqualTo("anthropic/claude-sonnet-4-5"));
    }

    [Test]
    public async Task UpdateAsync_NonExistentProject_ReturnsNull()
    {
        // Act
        var result = await _service.UpdateAsync("nonexistent", "model");

        // Assert
        Assert.That(result, Is.Null);
    }

    #endregion

    #region DeleteAsync Tests

    [Test]
    public async Task DeleteAsync_ExistingProject_ReturnsTrue()
    {
        // Arrange
        var project = new Homespun.Features.PullRequests.Data.Entities.Project
        {
            Name = "repo",
            LocalPath = "/path",
            GitHubOwner = "owner",
            GitHubRepo = "repo",
            DefaultBranch = "main"
        };
        await _dataStore.AddProjectAsync(project);

        // Act
        var result = await _service.DeleteAsync(project.Id);

        // Assert
        Assert.That(result, Is.True);
        Assert.That(_dataStore.Projects, Has.Count.EqualTo(0));
    }

    [Test]
    public async Task DeleteAsync_NonExistentProject_ReturnsFalse()
    {
        // Act
        var result = await _service.DeleteAsync("nonexistent");

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion

    #region GetAllAsync Tests

    [Test]
    public async Task GetAllAsync_ReturnsAllProjects()
    {
        // Arrange
        var project1 = new Homespun.Features.PullRequests.Data.Entities.Project
        {
            Name = "repo1",
            LocalPath = "/path1",
            GitHubOwner = "owner",
            GitHubRepo = "repo1",
            DefaultBranch = "main"
        };
        var project2 = new Homespun.Features.PullRequests.Data.Entities.Project
        {
            Name = "repo2",
            LocalPath = "/path2",
            GitHubOwner = "owner",
            GitHubRepo = "repo2",
            DefaultBranch = "main"
        };
        await _dataStore.AddProjectAsync(project1);
        await _dataStore.AddProjectAsync(project2);

        // Act
        var result = await _service.GetAllAsync();

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
    }

    #endregion
}
