using Microsoft.EntityFrameworkCore;
using Moq;
using TreeAgent.Web.Features.Agents;
using TreeAgent.Web.Features.Git;
using TreeAgent.Web.Features.PullRequests.Data;
using TreeAgent.Web.Features.PullRequests.Data.Entities;

namespace TreeAgent.Web.Tests.Features.Agents;

[TestFixture]
public class SystemPromptServiceTests
{
    private TreeAgentDbContext _db = null!;
    private Mock<PullRequestDataService> _mockPullRequestDataService = null!;
    private SystemPromptService _service = null!;

    [SetUp]
    public void SetUp()
    {
        var options = new DbContextOptionsBuilder<TreeAgentDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new TreeAgentDbContext(options);

        // Create mock PullRequestDataService
        var mockWorktreeService = new Mock<GitWorktreeService>();
        _mockPullRequestDataService = new Mock<PullRequestDataService>(_db, mockWorktreeService.Object);

        _service = new SystemPromptService(_db, _mockPullRequestDataService.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _db.Dispose();
    }

    private async Task<Project> CreateTestProject()
    {
        var project = new Project
        {
            Name = "Test Project",
            LocalPath = "/test/path",
            GitHubOwner = "test-owner",
            GitHubRepo = "test-repo",
            DefaultBranch = "main"
        };

        _db.Projects.Add(project);
        await _db.SaveChangesAsync();
        return project;
    }

    private async Task<PullRequest> CreateTestPullRequest(string projectId)
    {
        var pullRequest = new PullRequest
        {
            ProjectId = projectId,
            Title = "Test Pull Request",
            Description = "Test Description",
            BranchName = "feature/test",
            Status = OpenPullRequestStatus.InDevelopment
        };

        _db.PullRequests.Add(pullRequest);
        await _db.SaveChangesAsync();
        return pullRequest;
    }

    [Test]
    public async Task CreateAsync_CreatesTemplate()
    {
        // Act
        var template = await _service.CreateAsync(
            "Test Template",
            "This is the content",
            "Test description",
            null,
            true);

        // Assert
        Assert.That(template, Is.Not.Null);
        Assert.That(template.Name, Is.EqualTo("Test Template"));
        Assert.That(template.Content, Is.EqualTo("This is the content"));
        Assert.That(template.IsGlobal, Is.True);
    }

    [Test]
    public async Task GetGlobalTemplatesAsync_ReturnsOnlyGlobalTemplates()
    {
        // Arrange
        var project = await CreateTestProject();

        await _service.CreateAsync("Global 1", "Content 1", null, null, isGlobal: true);
        await _service.CreateAsync("Global 2", "Content 2", null, null, isGlobal: true);
        await _service.CreateAsync("Project Specific", "Content 3", null, project.Id, isGlobal: false);

        // Act
        var templates = await _service.GetGlobalTemplatesAsync();

        // Assert
        Assert.That(templates, Has.Count.EqualTo(2));
        Assert.That(templates, Has.All.Matches<SystemPromptTemplate>(t => t.IsGlobal));
    }

    [Test]
    public async Task GetProjectTemplatesAsync_ReturnsProjectAndGlobalTemplates()
    {
        // Arrange
        var project = await CreateTestProject();

        await _service.CreateAsync("Global", "Content 1", null, null, isGlobal: true);
        await _service.CreateAsync("Project Specific", "Content 2", null, project.Id, isGlobal: false);

        // Act
        var templates = await _service.GetProjectTemplatesAsync(project.Id);

        // Assert
        Assert.That(templates, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task UpdateAsync_UpdatesTemplate()
    {
        // Arrange
        var template = await _service.CreateAsync("Original", "Original Content", null, null, true);

        // Act
        var updated = await _service.UpdateAsync(
            template.Id,
            "Updated",
            "Updated Content",
            "New Description",
            false);

        // Assert
        Assert.That(updated, Is.Not.Null);
        Assert.That(updated!.Name, Is.EqualTo("Updated"));
        Assert.That(updated.Content, Is.EqualTo("Updated Content"));
        Assert.That(updated.IsGlobal, Is.False);
    }

    [Test]
    public async Task DeleteAsync_DeletesTemplate()
    {
        // Arrange
        var template = await _service.CreateAsync("To Delete", "Content", null, null, true);

        // Act
        var result = await _service.DeleteAsync(template.Id);

        // Assert
        Assert.That(result, Is.True);
        var deleted = await _service.GetByIdAsync(template.Id);
        Assert.That(deleted, Is.Null);
    }

    [Test]
    public async Task ProcessTemplateAsync_ReplacesProjectVariables()
    {
        // Arrange
        var project = await CreateTestProject();
        var pullRequest = await CreateTestPullRequest(project.Id);

        var template = "Project: {{PROJECT_NAME}}, Branch: {{DEFAULT_BRANCH}}";

        // Act
        var result = await _service.ProcessTemplateAsync(template, pullRequest.Id);

        // Assert
        Assert.That(result, Does.Contain("Test Project"));
        Assert.That(result, Does.Contain("main"));
    }

    [Test]
    public async Task ProcessTemplateAsync_ReplacesPullRequestVariables()
    {
        // Arrange
        var project = await CreateTestProject();
        var pullRequest = await CreateTestPullRequest(project.Id);

        var template = "PR: {{PR_TITLE}}, Status: {{PR_STATUS}}, Branch: {{BRANCH_NAME}}";

        // Act
        var result = await _service.ProcessTemplateAsync(template, pullRequest.Id);

        // Assert
        Assert.That(result, Does.Contain("Test Pull Request"));
        Assert.That(result, Does.Contain("InDevelopment"));
        Assert.That(result, Does.Contain("feature/test"));
    }

    [Test]
    public async Task ProcessTemplateAsync_SupportsLegacyFeatureVariables()
    {
        // Arrange
        var project = await CreateTestProject();
        var pullRequest = await CreateTestPullRequest(project.Id);

        var template = "Feature: {{FEATURE_TITLE}}, Status: {{FEATURE_STATUS}}, Branch: {{BRANCH_NAME}}";

        // Act
        var result = await _service.ProcessTemplateAsync(template, pullRequest.Id);

        // Assert
        Assert.That(result, Does.Contain("Test Pull Request"));
        Assert.That(result, Does.Contain("InDevelopment"));
        Assert.That(result, Does.Contain("feature/test"));
    }

    [Test]
    public async Task ProcessTemplateAsync_RemovesUnprocessedVariables()
    {
        // Arrange
        var template = "This {{UNKNOWN_VARIABLE}} should be removed";

        // Act
        var result = await _service.ProcessTemplateAsync(template);

        // Assert
        Assert.That(result, Does.Not.Contain("{{"));
        Assert.That(result, Does.Not.Contain("}}"));
    }

    [Test]
    public async Task ProcessTemplateAsync_HandlesEmptyTemplate()
    {
        // Act
        var result = await _service.ProcessTemplateAsync("");

        // Assert
        Assert.That(result, Is.EqualTo(""));
    }

    [Test]
    public void GetAvailableVariables_ReturnsExpectedVariables()
    {
        // Act
        var variables = SystemPromptService.GetAvailableVariables();

        // Assert
        Assert.That(variables, Is.Not.Empty);
        Assert.That(variables, Has.Some.Matches<TemplateVariable>(v => v.Variable == "{{PROJECT_NAME}}"));
        Assert.That(variables, Has.Some.Matches<TemplateVariable>(v => v.Variable == "{{PR_TITLE}}"));
        Assert.That(variables, Has.Some.Matches<TemplateVariable>(v => v.Variable == "{{BRANCH_NAME}}"));
        Assert.That(variables, Has.Some.Matches<TemplateVariable>(v => v.Variable == "{{PR_TREE}}"));
    }
}
