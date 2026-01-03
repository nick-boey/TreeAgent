using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using TreeAgent.Web.Features.OpenCode.Models;
using TreeAgent.Web.Features.OpenCode.Services;

namespace TreeAgent.Web.Tests.Features.OpenCode;

[TestFixture]
public class OpenCodeClientTests
{
    private Mock<HttpMessageHandler> _mockHandler = null!;
    private HttpClient _httpClient = null!;
    private Mock<ILogger<OpenCodeClient>> _mockLogger = null!;
    private OpenCodeClient _client = null!;

    private const string BaseUrl = "http://localhost:5000";

    [SetUp]
    public void SetUp()
    {
        _mockHandler = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_mockHandler.Object);
        _mockLogger = new Mock<ILogger<OpenCodeClient>>();
        _client = new OpenCodeClient(_httpClient, _mockLogger.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _httpClient.Dispose();
    }

    [Test]
    public async Task GetHealthAsync_ReturnsHealthResponse()
    {
        var expectedResponse = new HealthResponse { Healthy = true, Version = "1.0.0" };
        SetupMockResponse(HttpMethod.Get, $"{BaseUrl}/global/health", HttpStatusCode.OK, expectedResponse);

        var result = await _client.GetHealthAsync(BaseUrl);

        Assert.That(result.Healthy, Is.True);
        Assert.That(result.Version, Is.EqualTo("1.0.0"));
    }

    [Test]
    public async Task ListSessionsAsync_ReturnsSessionList()
    {
        var expectedSessions = new List<OpenCodeSession>
        {
            new() { Id = "session-1", Title = "Session 1" },
            new() { Id = "session-2", Title = "Session 2" }
        };
        SetupMockResponse(HttpMethod.Get, $"{BaseUrl}/session", HttpStatusCode.OK, expectedSessions);

        var result = await _client.ListSessionsAsync(BaseUrl);

        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result[0].Id, Is.EqualTo("session-1"));
    }

    [Test]
    public async Task GetSessionAsync_ReturnsSession()
    {
        var expectedSession = new OpenCodeSession { Id = "session-1", Title = "Test Session" };
        SetupMockResponse(HttpMethod.Get, $"{BaseUrl}/session/session-1", HttpStatusCode.OK, expectedSession);

        var result = await _client.GetSessionAsync(BaseUrl, "session-1");

        Assert.That(result.Id, Is.EqualTo("session-1"));
        Assert.That(result.Title, Is.EqualTo("Test Session"));
    }

    [Test]
    public async Task CreateSessionAsync_CreatesAndReturnsSession()
    {
        var expectedSession = new OpenCodeSession { Id = "new-session", Title = "New Session" };
        SetupMockResponse(HttpMethod.Post, $"{BaseUrl}/session", HttpStatusCode.OK, expectedSession);

        var result = await _client.CreateSessionAsync(BaseUrl, "New Session");

        Assert.That(result.Id, Is.EqualTo("new-session"));
    }

    [Test]
    public async Task DeleteSessionAsync_ReturnsTrue_OnSuccess()
    {
        SetupMockResponse(HttpMethod.Delete, $"{BaseUrl}/session/session-1", HttpStatusCode.OK, true);

        var result = await _client.DeleteSessionAsync(BaseUrl, "session-1");

        Assert.That(result, Is.True);
    }

    [Test]
    public async Task DeleteSessionAsync_ReturnsFalse_OnNotFound()
    {
        SetupMockResponse(HttpMethod.Delete, $"{BaseUrl}/session/session-1", HttpStatusCode.NotFound, false);

        var result = await _client.DeleteSessionAsync(BaseUrl, "session-1");

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task AbortSessionAsync_ReturnsTrue_OnSuccess()
    {
        SetupMockResponse(HttpMethod.Post, $"{BaseUrl}/session/session-1/abort", HttpStatusCode.OK, true);

        var result = await _client.AbortSessionAsync(BaseUrl, "session-1");

        Assert.That(result, Is.True);
    }

    [Test]
    public async Task GetMessagesAsync_ReturnsMessages()
    {
        var expectedMessages = new List<OpenCodeMessage>
        {
            new()
            {
                Info = new OpenCodeMessageInfo { Id = "msg-1", Role = "user" },
                Parts = [new OpenCodeMessagePart { Type = "text", Text = "Hello" }]
            }
        };
        SetupMockResponse(HttpMethod.Get, $"{BaseUrl}/session/session-1/message", HttpStatusCode.OK, expectedMessages);

        var result = await _client.GetMessagesAsync(BaseUrl, "session-1");

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Info.Id, Is.EqualTo("msg-1"));
    }

    [Test]
    public async Task SendPromptAsync_SendsAndReturnsResponse()
    {
        var expectedResponse = new OpenCodeMessage
        {
            Info = new OpenCodeMessageInfo { Id = "response-1", Role = "assistant" },
            Parts = [new OpenCodeMessagePart { Type = "text", Text = "Hello back!" }]
        };
        SetupMockResponse(HttpMethod.Post, $"{BaseUrl}/session/session-1/message", HttpStatusCode.OK, expectedResponse);

        var request = PromptRequest.FromText("Hello");
        var result = await _client.SendPromptAsync(BaseUrl, "session-1", request);

        Assert.That(result.Info.Role, Is.EqualTo("assistant"));
    }

    [Test]
    public async Task SendPromptAsyncNoWait_CompletesWithoutException()
    {
        SetupMockResponse<object>(HttpMethod.Post, $"{BaseUrl}/session/session-1/prompt_async", HttpStatusCode.NoContent, null);

        var request = PromptRequest.FromText("Hello");
        
        Assert.DoesNotThrowAsync(async () => 
            await _client.SendPromptAsyncNoWait(BaseUrl, "session-1", request));
    }

    private void SetupMockResponse<T>(HttpMethod method, string url, HttpStatusCode statusCode, T? content)
    {
        var response = new HttpResponseMessage(statusCode);
        if (content != null)
        {
            response.Content = new StringContent(
                JsonSerializer.Serialize(content),
                System.Text.Encoding.UTF8,
                "application/json");
        }

        _mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.Method == method && r.RequestUri!.ToString() == url),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);
    }
}
