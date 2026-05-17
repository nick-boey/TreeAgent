using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Homespun.Features.AgentOrchestration.Services;
using Homespun.Shared.Requests;
using Microsoft.Extensions.DependencyInjection;

namespace Homespun.Api.Tests.Features.AgentOrchestration;

/// <summary>
/// Integration tests for queue status and control API endpoints.
/// </summary>
[TestFixture]
public class QueueApiTests
{
    private HomespunWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = new HomespunWebApplicationFactory();
        _client = _factory.CreateClient();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Test]
    public void QueueCoordinator_IsRegisteredInDI()
    {
        var service = _factory.Services.GetService<IActionQueueCoordinator>();

        Assert.That(service, Is.Not.Null);
        Assert.That(service, Is.InstanceOf<ActionQueueCoordinator>());
    }

    [Test]
    public async Task Start_ReturnsNotFound_WhenProjectDoesNotExist()
    {
        var request = new StartActionQueueRequest { IssueId = "issue1" };

        var response = await _client.PostAsJsonAsync(
            "/api/projects/nonexistent-project/action-queue/start", request, JsonOptions);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetStatus_ReturnsNotFound_WhenProjectDoesNotExist()
    {
        var response = await _client.GetAsync("/api/projects/nonexistent-project/action-queue/status");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Cancel_ReturnsNotFound_WhenProjectDoesNotExist()
    {
        var response = await _client.PostAsync("/api/projects/nonexistent-project/action-queue/cancel", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetStatus_ReturnsNotFound_WhenNoActiveExecution()
    {
        // Create a project first
        var projectId = await CreateTestProject("queue-status-test");
        if (projectId == null)
        {
            Assert.Inconclusive("Could not create test project in mock mode");
            return;
        }

        var response = await _client.GetAsync($"/api/projects/{projectId}/action-queue/status");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Cancel_ReturnsNotFound_WhenNoActiveExecution()
    {
        var projectId = await CreateTestProject("queue-cancel-test");
        if (projectId == null)
        {
            Assert.Inconclusive("Could not create test project in mock mode");
            return;
        }

        var response = await _client.PostAsync($"/api/projects/{projectId}/action-queue/cancel", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Start_ReturnsBadRequest_WhenIssueIdIsEmpty()
    {
        var projectId = await CreateTestProject("queue-start-empty-issue");
        if (projectId == null)
        {
            Assert.Inconclusive("Could not create test project in mock mode");
            return;
        }

        var request = new StartActionQueueRequest { IssueId = "" };
        var response = await _client.PostAsJsonAsync(
            $"/api/projects/{projectId}/action-queue/start", request, JsonOptions);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task GetStatus_RejectsLimitBelowOne()
    {
        var response = await _client.GetAsync("/api/projects/any-project/action-queue/status?limit=0");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task GetStatus_RejectsLimitAbove200()
    {
        var response = await _client.GetAsync("/api/projects/any-project/action-queue/status?limit=201");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task GetStatus_RejectsNegativeOffset()
    {
        var response = await _client.GetAsync("/api/projects/any-project/action-queue/status?offset=-1");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task GetStatus_AcceptsDefaultPagination()
    {
        // No execution exists, so we expect 404 — but specifically NOT 400.
        // This proves the default limit/offset validation passes.
        var response = await _client.GetAsync("/api/projects/any-project/action-queue/status");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Start_Endpoints_DoNotReturn500()
    {
        var request = new StartActionQueueRequest { IssueId = "issue1" };

        var startResponse = await _client.PostAsJsonAsync(
            "/api/projects/any-project/action-queue/start", request, JsonOptions);
        var statusResponse = await _client.GetAsync("/api/projects/any-project/action-queue/status");
        var cancelResponse = await _client.PostAsync("/api/projects/any-project/action-queue/cancel", null);

        Assert.Multiple(() =>
        {
            Assert.That((int)startResponse.StatusCode, Is.LessThan(500),
                "Start endpoint should not return 500");
            Assert.That((int)statusResponse.StatusCode, Is.LessThan(500),
                "Status endpoint should not return 500");
            Assert.That((int)cancelResponse.StatusCode, Is.LessThan(500),
                "Cancel endpoint should not return 500");
        });
    }

    private async Task<string?> CreateTestProject(string name)
    {
        var createRequest = new { Name = name, Path = $"/tmp/{name}", DefaultBranch = "main" };
        var response = await _client.PostAsJsonAsync("/api/projects", createRequest, JsonOptions);
        if (!response.IsSuccessStatusCode)
            return null;

        var content = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return content.TryGetProperty("id", out var id) ? id.GetString() : null;
    }
}
