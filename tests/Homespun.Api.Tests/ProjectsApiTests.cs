using System.Net;
using System.Net.Http.Json;
using Homespun.Features.Projects.Controllers;
using Homespun.Features.PullRequests.Data.Entities;

namespace Homespun.Api.Tests;

/// <summary>
/// Integration tests for the Projects API endpoints.
/// </summary>
[TestFixture]
public class ProjectsApiTests
{
    private HomespunWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    [SetUp]
    public void SetUp()
    {
        _factory = new HomespunWebApplicationFactory();
        _client = _factory.CreateClient();
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Test]
    public async Task GetProjects_ReturnsEmptyList_WhenNoProjects()
    {
        // Act
        var response = await _client.GetAsync("/api/projects");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var projects = await response.Content.ReadFromJsonAsync<List<Project>>();
        Assert.That(projects, Is.Not.Null);
        Assert.That(projects, Is.Empty);
    }

    [Test]
    public async Task GetProjects_ReturnsAllProjects_WhenProjectsExist()
    {
        // Arrange
        var project1 = new Project { Id = "p1", Name = "Project1", LocalPath = "/path/1", DefaultBranch = "main" };
        var project2 = new Project { Id = "p2", Name = "Project2", LocalPath = "/path/2", DefaultBranch = "main" };
        _factory.MockDataStore.SeedProject(project1);
        _factory.MockDataStore.SeedProject(project2);

        // Act
        var response = await _client.GetAsync("/api/projects");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var projects = await response.Content.ReadFromJsonAsync<List<Project>>();
        Assert.That(projects, Is.Not.Null);
        Assert.That(projects, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task GetProjectById_ReturnsProject_WhenExists()
    {
        // Arrange
        var project = new Project { Id = "test-id", Name = "TestProject", LocalPath = "/path", DefaultBranch = "main" };
        _factory.MockDataStore.SeedProject(project);

        // Act
        var response = await _client.GetAsync("/api/projects/test-id");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<Project>();
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Id, Is.EqualTo("test-id"));
        Assert.That(result.Name, Is.EqualTo("TestProject"));
    }

    [Test]
    public async Task GetProjectById_ReturnsNotFound_WhenNotExists()
    {
        // Act
        var response = await _client.GetAsync("/api/projects/nonexistent");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task UpdateProject_ReturnsUpdatedProject_WhenExists()
    {
        // Arrange
        var project = new Project { Id = "test-id", Name = "TestProject", LocalPath = "/path", DefaultBranch = "main" };
        _factory.MockDataStore.SeedProject(project);

        var updateRequest = new UpdateProjectRequest { DefaultModel = "claude-sonnet-4-20250514" };

        // Act
        var response = await _client.PutAsJsonAsync("/api/projects/test-id", updateRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<Project>();
        Assert.That(result, Is.Not.Null);
        Assert.That(result.DefaultModel, Is.EqualTo("claude-sonnet-4-20250514"));
    }

    [Test]
    public async Task UpdateProject_ReturnsNotFound_WhenNotExists()
    {
        // Arrange
        var updateRequest = new UpdateProjectRequest { DefaultModel = "test-model" };

        // Act
        var response = await _client.PutAsJsonAsync("/api/projects/nonexistent", updateRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DeleteProject_ReturnsNoContent_WhenExists()
    {
        // Arrange
        var project = new Project { Id = "test-id", Name = "TestProject", LocalPath = "/path", DefaultBranch = "main" };
        _factory.MockDataStore.SeedProject(project);

        // Act
        var response = await _client.DeleteAsync("/api/projects/test-id");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That(_factory.MockDataStore.GetProject("test-id"), Is.Null);
    }

    [Test]
    public async Task DeleteProject_ReturnsNotFound_WhenNotExists()
    {
        // Act
        var response = await _client.DeleteAsync("/api/projects/nonexistent");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
