using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Homespun.Features.OpenCode.Models;

namespace Homespun.Features.OpenCode.Services;

/// <summary>
/// HTTP client for communicating with OpenCode server instances.
/// </summary>
public class OpenCodeClient(HttpClient httpClient, ILogger<OpenCodeClient> logger) : IOpenCodeClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<HealthResponse> GetHealthAsync(string baseUrl, CancellationToken ct = default)
    {
        var response = await httpClient.GetAsync($"{baseUrl}/global/health", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<HealthResponse>(JsonOptions, ct) 
               ?? throw new InvalidOperationException("Failed to parse health response");
    }

    public async Task<List<OpenCodeSession>> ListSessionsAsync(string baseUrl, CancellationToken ct = default)
    {
        var response = await httpClient.GetAsync($"{baseUrl}/session", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<OpenCodeSession>>(JsonOptions, ct) 
               ?? [];
    }

    public async Task<OpenCodeSession> GetSessionAsync(string baseUrl, string sessionId, CancellationToken ct = default)
    {
        var response = await httpClient.GetAsync($"{baseUrl}/session/{sessionId}", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OpenCodeSession>(JsonOptions, ct) 
               ?? throw new InvalidOperationException($"Failed to parse session {sessionId}");
    }

    public async Task<OpenCodeSession> CreateSessionAsync(string baseUrl, string? title = null, CancellationToken ct = default)
    {
        var body = new { title };
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        
        var response = await httpClient.PostAsync($"{baseUrl}/session", content, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OpenCodeSession>(JsonOptions, ct) 
               ?? throw new InvalidOperationException("Failed to parse created session");
    }

    public async Task<bool> DeleteSessionAsync(string baseUrl, string sessionId, CancellationToken ct = default)
    {
        var response = await httpClient.DeleteAsync($"{baseUrl}/session/{sessionId}", ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> AbortSessionAsync(string baseUrl, string sessionId, CancellationToken ct = default)
    {
        var response = await httpClient.PostAsync($"{baseUrl}/session/{sessionId}/abort", null, ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<List<OpenCodeMessage>> GetMessagesAsync(string baseUrl, string sessionId, CancellationToken ct = default)
    {
        var response = await httpClient.GetAsync($"{baseUrl}/session/{sessionId}/message", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<OpenCodeMessage>>(JsonOptions, ct) 
               ?? [];
    }

    public async Task<OpenCodeMessage> SendPromptAsync(string baseUrl, string sessionId, PromptRequest request, CancellationToken ct = default)
    {
        var requestJson = JsonSerializer.Serialize(request, JsonOptions);
        logger.LogInformation(
            "OpenCodeClient.SendPromptAsync: POST {Url}/session/{SessionId}/message. Request body: {RequestBody}",
            baseUrl, sessionId, requestJson);
        
        var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
        
        var response = await httpClient.PostAsync($"{baseUrl}/session/{sessionId}/message", content, ct);
        
        logger.LogInformation(
            "OpenCodeClient.SendPromptAsync: Response status {StatusCode} for session {SessionId}",
            (int)response.StatusCode, sessionId);
        
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OpenCodeMessage>(JsonOptions, ct) 
               ?? throw new InvalidOperationException("Failed to parse prompt response");
    }

    public async Task SendPromptAsyncNoWait(string baseUrl, string sessionId, PromptRequest request, CancellationToken ct = default)
    {
        var requestJson = JsonSerializer.Serialize(request, JsonOptions);
        logger.LogInformation(
            "OpenCodeClient.SendPromptAsyncNoWait: POST {Url}/session/{SessionId}/prompt_async. Request body: {RequestBody}",
            baseUrl, sessionId, requestJson);

        var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        var response = await httpClient.PostAsync($"{baseUrl}/session/{sessionId}/prompt_async", content, ct);

        logger.LogInformation(
            "OpenCodeClient.SendPromptAsyncNoWait: Response status {StatusCode} for session {SessionId}",
            (int)response.StatusCode, sessionId);

        response.EnsureSuccessStatusCode();
    }

    public async IAsyncEnumerable<OpenCodeEvent> SendPromptStreamingAsync(
        string baseUrl,
        string sessionId,
        PromptRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var requestJson = JsonSerializer.Serialize(request, JsonOptions);
        logger.LogInformation(
            "OpenCodeClient.SendPromptStreamingAsync: POST {Url}/session/{SessionId}/message (streaming). Request body: {RequestBody}",
            baseUrl, sessionId, requestJson);

        // First, send the prompt without waiting (fire and forget to initiate the request)
        var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
        var postResponse = await httpClient.PostAsync($"{baseUrl}/session/{sessionId}/prompt_async", content, ct);

        logger.LogInformation(
            "OpenCodeClient.SendPromptStreamingAsync: Initial POST response status {StatusCode} for session {SessionId}",
            (int)postResponse.StatusCode, sessionId);

        postResponse.EnsureSuccessStatusCode();

        // Track if we've seen the session become idle (indicates completion)
        var seenMessageCreated = false;
        var messageCompleted = false;

        // Then subscribe to events and yield them as they arrive
        await foreach (var evt in SubscribeToEventsAsync(baseUrl, ct))
        {
            // Filter to only relevant events for this session
            if (evt.Properties?.SessionId != null && evt.Properties.SessionId != sessionId)
            {
                continue;
            }

            // Track message lifecycle
            if (evt.Type == OpenCodeEventTypes.MessageCreated)
            {
                seenMessageCreated = true;
            }

            yield return evt;

            // Check if the session has returned to idle state after processing started
            // This indicates the response is complete
            if (seenMessageCreated && evt.Type == OpenCodeEventTypes.SessionUpdated)
            {
                var status = evt.Properties?.StatusValue;
                if (status == "idle")
                {
                    logger.LogDebug("Session {SessionId} returned to idle, streaming complete", sessionId);
                    messageCompleted = true;
                    break;
                }
            }
        }

        if (!messageCompleted)
        {
            logger.LogWarning("SendPromptStreamingAsync: Event stream ended without session becoming idle for session {SessionId}", sessionId);
        }
    }

    public async IAsyncEnumerable<OpenCodeEvent> SubscribeToEventsAsync(
        string baseUrl, 
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/event");
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));
        
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        
        var eventBuilder = new StringBuilder();
        
        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            
            if (line == null)
            {
                // Stream ended
                break;
            }
            
            if (string.IsNullOrEmpty(line))
            {
                // Empty line means end of event
                if (eventBuilder.Length > 0)
                {
                    var eventData = eventBuilder.ToString().Trim();
                    eventBuilder.Clear();
                    
                    if (eventData.StartsWith("data:"))
                    {
                        var jsonData = eventData["data:".Length..].Trim();
                        if (!string.IsNullOrEmpty(jsonData))
                        {
                            logger.LogDebug("SSE raw event data: {Data}", jsonData);
                            
                            OpenCodeEvent? evt = null;
                            try
                            {
                                evt = JsonSerializer.Deserialize<OpenCodeEvent>(jsonData, JsonOptions);
                                
                                if (evt != null)
                                {
                                    logger.LogDebug("SSE parsed event: Type={Type}, SessionId={SessionId}, Status={Status}", 
                                        evt.Type, evt.Properties?.SessionId, evt.Properties?.StatusValue);
                                }
                            }
                            catch (JsonException ex)
                            {
                                logger.LogWarning(ex, "Failed to parse SSE event: {Data}", jsonData);
                            }
                            
                            if (evt != null)
                            {
                                yield return evt;
                            }
                        }
                    }
                }
            }
            else
            {
                eventBuilder.AppendLine(line);
            }
        }
    }

    public async Task<string?> GetCurrentPathAsync(string baseUrl, CancellationToken ct = default)
    {
        try
        {
            var response = await httpClient.GetAsync($"{baseUrl}/path", ct);
            response.EnsureSuccessStatusCode();
            
            // The /path endpoint returns a Path object - log raw response for debugging
            var json = await response.Content.ReadAsStringAsync(ct);
            logger.LogDebug("GetCurrentPathAsync: Raw response from /path: {Json}", json);
            
            using var doc = JsonDocument.Parse(json);
            
            // Try property names returned by OpenCode /path endpoint
            // Response format: {"home":"...","state":"...","config":"...","worktree":"...","directory":"..."}
            if (doc.RootElement.TryGetProperty("directory", out var directoryProp) && directoryProp.ValueKind == JsonValueKind.String)
            {
                return directoryProp.GetString();
            }
            if (doc.RootElement.TryGetProperty("worktree", out var worktreeProp) && worktreeProp.ValueKind == JsonValueKind.String)
            {
                return worktreeProp.GetString();
            }
            if (doc.RootElement.TryGetProperty("path", out var pathProp) && pathProp.ValueKind == JsonValueKind.String)
            {
                return pathProp.GetString();
            }
            if (doc.RootElement.TryGetProperty("cwd", out var cwdProp) && cwdProp.ValueKind == JsonValueKind.String)
            {
                return cwdProp.GetString();
            }
            
            // If it's a simple string response
            if (doc.RootElement.ValueKind == JsonValueKind.String)
            {
                return doc.RootElement.GetString();
            }
            
            logger.LogWarning("GetCurrentPathAsync: Unable to find path property in response. Raw JSON: {Json}", json);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "GetCurrentPathAsync: Failed to get current path from {BaseUrl}", baseUrl);
            return null;
        }
    }
}
