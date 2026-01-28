using System.Net;
using System.Net.Http.Json;
using Homespun.Features.PullRequests.Controllers;
using Homespun.Features.PullRequests.Data.Entities;

namespace Homespun.Api.Tests;

/// <summary>
/// Integration tests for the Pull Requests API endpoints.
/// </summary>
[TestFixture]
public class PullRequestsApiTests
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
    public async Task GetPullRequestsByProject_ReturnsEmptyList_WhenNoProjectPRs()
    {
        // Arrange
        var project = new Project { Id = "proj1", Name = "TestProject", LocalPath = "/path", DefaultBranch = "main" };
        _factory.MockDataStore.SeedProject(project);

        // Act
        var response = await _client.GetAsync("/api/projects/proj1/pull-requests");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var prs = await response.Content.ReadFromJsonAsync<List<PullRequest>>();
        Assert.That(prs, Is.Not.Null);
        Assert.That(prs, Is.Empty);
    }

    [Test]
    public async Task GetPullRequestsByProject_ReturnsNotFound_WhenProjectNotExists()
    {
        // Act
        var response = await _client.GetAsync("/api/projects/nonexistent/pull-requests");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetPullRequestsByProject_ReturnsPRs_WhenExist()
    {
        // Arrange
        var project = new Project { Id = "proj1", Name = "TestProject", LocalPath = "/path", DefaultBranch = "main" };
        var pr1 = new PullRequest { Id = "pr1", ProjectId = "proj1", Title = "PR1" };
        var pr2 = new PullRequest { Id = "pr2", ProjectId = "proj1", Title = "PR2" };
        _factory.MockDataStore.SeedProject(project);
        _factory.MockDataStore.SeedPullRequest(pr1);
        _factory.MockDataStore.SeedPullRequest(pr2);

        // Act
        var response = await _client.GetAsync("/api/projects/proj1/pull-requests");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var prs = await response.Content.ReadFromJsonAsync<List<PullRequest>>();
        Assert.That(prs, Is.Not.Null);
        Assert.That(prs, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task GetPullRequestById_ReturnsPR_WhenExists()
    {
        // Arrange
        var pr = new PullRequest { Id = "pr1", ProjectId = "proj1", Title = "Test PR" };
        _factory.MockDataStore.SeedPullRequest(pr);

        // Act
        var response = await _client.GetAsync("/api/pull-requests/pr1");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<PullRequest>();
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Id, Is.EqualTo("pr1"));
        Assert.That(result.Title, Is.EqualTo("Test PR"));
    }

    [Test]
    public async Task GetPullRequestById_ReturnsNotFound_WhenNotExists()
    {
        // Act
        var response = await _client.GetAsync("/api/pull-requests/nonexistent");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task CreatePullRequest_ReturnsCreated_WhenValid()
    {
        // Arrange
        var project = new Project { Id = "proj1", Name = "TestProject", LocalPath = "/path", DefaultBranch = "main" };
        _factory.MockDataStore.SeedProject(project);

        var createRequest = new CreatePullRequestRequest
        {
            ProjectId = "proj1",
            Title = "New PR",
            Description = "Test description",
            BranchName = "feature/test"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/pull-requests", createRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var result = await response.Content.ReadFromJsonAsync<PullRequest>();
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Title, Is.EqualTo("New PR"));
        Assert.That(result.Description, Is.EqualTo("Test description"));
        Assert.That(result.BranchName, Is.EqualTo("feature/test"));
    }

    [Test]
    public async Task CreatePullRequest_ReturnsNotFound_WhenProjectNotExists()
    {
        // Arrange
        var createRequest = new CreatePullRequestRequest
        {
            ProjectId = "nonexistent",
            Title = "New PR"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/pull-requests", createRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task UpdatePullRequest_ReturnsUpdated_WhenValid()
    {
        // Arrange
        var pr = new PullRequest { Id = "pr1", ProjectId = "proj1", Title = "Original Title" };
        _factory.MockDataStore.SeedPullRequest(pr);

        var updateRequest = new UpdatePullRequestRequest
        {
            Title = "Updated Title",
            Status = OpenPullRequestStatus.ReadyForReview
        };

        // Act
        var response = await _client.PutAsJsonAsync("/api/pull-requests/pr1", updateRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<PullRequest>();
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Title, Is.EqualTo("Updated Title"));
        Assert.That(result.Status, Is.EqualTo(OpenPullRequestStatus.ReadyForReview));
    }

    [Test]
    public async Task UpdatePullRequest_ReturnsNotFound_WhenNotExists()
    {
        // Arrange
        var updateRequest = new UpdatePullRequestRequest { Title = "Updated" };

        // Act
        var response = await _client.PutAsJsonAsync("/api/pull-requests/nonexistent", updateRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DeletePullRequest_ReturnsNoContent_WhenExists()
    {
        // Arrange
        var pr = new PullRequest { Id = "pr1", ProjectId = "proj1", Title = "Test PR" };
        _factory.MockDataStore.SeedPullRequest(pr);

        // Act
        var response = await _client.DeleteAsync("/api/pull-requests/pr1");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That(_factory.MockDataStore.GetPullRequest("pr1"), Is.Null);
    }

    [Test]
    public async Task DeletePullRequest_ReturnsNotFound_WhenNotExists()
    {
        // Act
        var response = await _client.DeleteAsync("/api/pull-requests/nonexistent");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
