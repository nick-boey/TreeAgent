using Microsoft.EntityFrameworkCore;
using Moq;
using TreeAgent.Web.Data;
using TreeAgent.Web.Data.Entities;
using TreeAgent.Web.Services;

namespace TreeAgent.Web.Tests.Services;

public class SystemPromptServiceTests : IDisposable
{
    private readonly TreeAgentDbContext _db;
    private readonly Mock<FeatureService> _mockFeatureService;
    private readonly SystemPromptService _service;

    public SystemPromptServiceTests()
    {
        var options = new DbContextOptionsBuilder<TreeAgentDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new TreeAgentDbContext(options);

        // Create mock FeatureService
        var mockWorktreeService = new Mock<GitWorktreeService>();
        _mockFeatureService = new Mock<FeatureService>(_db, mockWorktreeService.Object);

        _service = new SystemPromptService(_db, _mockFeatureService.Object);
    }

    public void Dispose()
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

    private async Task<Feature> CreateTestFeature(string projectId)
    {
        var feature = new Feature
        {
            ProjectId = projectId,
            Title = "Test Feature",
            Description = "Test Description",
            BranchName = "feature/test",
            Status = FeatureStatus.InDevelopment
        };

        _db.Features.Add(feature);
        await _db.SaveChangesAsync();
        return feature;
    }

    [Fact]
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
        Assert.NotNull(template);
        Assert.Equal("Test Template", template.Name);
        Assert.Equal("This is the content", template.Content);
        Assert.True(template.IsGlobal);
    }

    [Fact]
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
        Assert.Equal(2, templates.Count);
        Assert.All(templates, t => Assert.True(t.IsGlobal));
    }

    [Fact]
    public async Task GetProjectTemplatesAsync_ReturnsProjectAndGlobalTemplates()
    {
        // Arrange
        var project = await CreateTestProject();

        await _service.CreateAsync("Global", "Content 1", null, null, isGlobal: true);
        await _service.CreateAsync("Project Specific", "Content 2", null, project.Id, isGlobal: false);

        // Act
        var templates = await _service.GetProjectTemplatesAsync(project.Id);

        // Assert
        Assert.Equal(2, templates.Count);
    }

    [Fact]
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
        Assert.NotNull(updated);
        Assert.Equal("Updated", updated.Name);
        Assert.Equal("Updated Content", updated.Content);
        Assert.False(updated.IsGlobal);
    }

    [Fact]
    public async Task DeleteAsync_DeletesTemplate()
    {
        // Arrange
        var template = await _service.CreateAsync("To Delete", "Content", null, null, true);

        // Act
        var result = await _service.DeleteAsync(template.Id);

        // Assert
        Assert.True(result);
        var deleted = await _service.GetByIdAsync(template.Id);
        Assert.Null(deleted);
    }

    [Fact]
    public async Task ProcessTemplateAsync_ReplacesProjectVariables()
    {
        // Arrange
        var project = await CreateTestProject();
        var feature = await CreateTestFeature(project.Id);

        var template = "Project: {{PROJECT_NAME}}, Branch: {{DEFAULT_BRANCH}}";

        // Act
        var result = await _service.ProcessTemplateAsync(template, feature.Id);

        // Assert
        Assert.Contains("Test Project", result);
        Assert.Contains("main", result);
    }

    [Fact]
    public async Task ProcessTemplateAsync_ReplacesFeatureVariables()
    {
        // Arrange
        var project = await CreateTestProject();
        var feature = await CreateTestFeature(project.Id);

        var template = "Feature: {{FEATURE_TITLE}}, Status: {{FEATURE_STATUS}}, Branch: {{BRANCH_NAME}}";

        // Act
        var result = await _service.ProcessTemplateAsync(template, feature.Id);

        // Assert
        Assert.Contains("Test Feature", result);
        Assert.Contains("InDevelopment", result);
        Assert.Contains("feature/test", result);
    }

    [Fact]
    public async Task ProcessTemplateAsync_RemovesUnprocessedVariables()
    {
        // Arrange
        var template = "This {{UNKNOWN_VARIABLE}} should be removed";

        // Act
        var result = await _service.ProcessTemplateAsync(template);

        // Assert
        Assert.DoesNotContain("{{", result);
        Assert.DoesNotContain("}}", result);
    }

    [Fact]
    public async Task ProcessTemplateAsync_HandlesEmptyTemplate()
    {
        // Act
        var result = await _service.ProcessTemplateAsync("");

        // Assert
        Assert.Equal("", result);
    }

    [Fact]
    public void GetAvailableVariables_ReturnsExpectedVariables()
    {
        // Act
        var variables = SystemPromptService.GetAvailableVariables();

        // Assert
        Assert.NotEmpty(variables);
        Assert.Contains(variables, v => v.Variable == "{{PROJECT_NAME}}");
        Assert.Contains(variables, v => v.Variable == "{{FEATURE_TITLE}}");
        Assert.Contains(variables, v => v.Variable == "{{BRANCH_NAME}}");
        Assert.Contains(variables, v => v.Variable == "{{FEATURE_TREE}}");
    }
}
