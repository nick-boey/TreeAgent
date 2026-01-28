using System.Net;
using System.Net.Http.Json;
using Homespun.Features.ClaudeCode.Controllers;
using Homespun.Features.PullRequests.Data.Entities;

namespace Homespun.Api.Tests;

/// <summary>
/// Integration tests for the Sessions API endpoints.
/// </summary>
[TestFixture]
public class SessionsApiTests
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
    public async Task GetSessions_ReturnsEmptyList_WhenNoSessions()
    {
        // Act
        var response = await _client.GetAsync("/api/sessions");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var sessions = await response.Content.ReadFromJsonAsync<List<SessionSummary>>();
        Assert.That(sessions, Is.Not.Null);
        Assert.That(sessions, Is.Empty);
    }

    [Test]
    public async Task GetSessionById_ReturnsNotFound_WhenNotExists()
    {
        // Act
        var response = await _client.GetAsync("/api/sessions/nonexistent");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task CreateSession_ReturnsNotFound_WhenProjectNotExists()
    {
        // Arrange
        var createRequest = new CreateSessionRequest
        {
            EntityId = "entity1",
            ProjectId = "nonexistent"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/sessions", createRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetSessionsByProject_ReturnsEmptyList_WhenNoProjectSessions()
    {
        // Arrange
        var project = new Project { Id = "proj1", Name = "TestProject", LocalPath = "/path", DefaultBranch = "main" };
        _factory.MockDataStore.SeedProject(project);

        // Act
        var response = await _client.GetAsync("/api/sessions/project/proj1");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var sessions = await response.Content.ReadFromJsonAsync<List<SessionSummary>>();
        Assert.That(sessions, Is.Not.Null);
        Assert.That(sessions, Is.Empty);
    }

    [Test]
    public async Task StopSession_ReturnsNotFound_WhenNotExists()
    {
        // Act
        var response = await _client.DeleteAsync("/api/sessions/nonexistent");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task SendMessage_ReturnsNotFound_WhenSessionNotExists()
    {
        // Arrange
        var messageRequest = new SendMessageRequest { Message = "Hello" };

        // Act
        var response = await _client.PostAsJsonAsync("/api/sessions/nonexistent/messages", messageRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
