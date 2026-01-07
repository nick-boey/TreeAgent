using Homespun.Features.OpenCode.Models;

namespace Homespun.Tests.Features.OpenCode;

[TestFixture]
public class OpenCodeServerModelTests
{
    [Test]
    public void Base64UrlEncode_SimpleString_EncodesCorrectly()
    {
        var result = OpenCodeServer.Base64UrlEncode("hello");
        Assert.That(result, Is.EqualTo("aGVsbG8"));
    }

    [Test]
    public void Base64UrlEncode_WindowsPath_UsesUrlSafeCharacters()
    {
        var result = OpenCodeServer.Base64UrlEncode(@"C:\Users\test\project");
        
        // Should not contain standard base64 characters that are URL-unsafe
        Assert.That(result, Does.Not.Contain("+"));
        Assert.That(result, Does.Not.Contain("/"));
        Assert.That(result, Does.Not.EndWith("="));
    }

    [Test]
    public void Base64UrlEncode_UnixPath_EncodesCorrectly()
    {
        var result = OpenCodeServer.Base64UrlEncode("/home/user/project");
        
        Assert.That(result, Does.Not.Contain("+"));
        Assert.That(result, Does.Not.Contain("/"));
        Assert.That(result, Does.Not.EndWith("="));
    }

    [Test]
    public void Base64UrlEncode_MatchesOpenCodeFormat()
    {
        // This path should encode to match what OpenCode expects
        // The encoding should use URL-safe base64: - instead of +, _ instead of /, no padding
        var path = @"C:\Users\nboey\.homespun\src\Sharpitect\hsp\test";
        var result = OpenCodeServer.Base64UrlEncode(path);
        
        // Verify it matches the expected OpenCode format
        Assert.That(result, Is.EqualTo("QzpcVXNlcnNcbmJvZXlcLmhvbWVzcHVuXHNyY1xTaGFycGl0ZWN0XGhzcFx0ZXN0"));
    }

    [Test]
    public void Base64UrlEncode_PathWithSpaces_EncodesCorrectly()
    {
        var result = OpenCodeServer.Base64UrlEncode(@"C:\My Documents\test project");
        
        Assert.That(result, Does.Not.Contain("+"));
        Assert.That(result, Does.Not.Contain("/"));
        Assert.That(result, Does.Not.EndWith("="));
        
        // Verify it's valid base64url by checking it only contains allowed characters
        Assert.That(result, Does.Match(@"^[A-Za-z0-9_-]+$"));
    }

    [Test]
    public void Base64UrlEncode_EmptyString_ReturnsEmptyString()
    {
        var result = OpenCodeServer.Base64UrlEncode("");
        Assert.That(result, Is.EqualTo(""));
    }

    [Test]
    public void WebViewUrl_WithActiveSession_ReturnsFullUrl()
    {
        var server = new OpenCodeServer
        {
            EntityId = "test-entity",
            WorktreePath = @"C:\test\path",
            Port = 4099,
            ActiveSessionId = "ses_123abc"
        };
        
        Assert.That(server.WebViewUrl, Is.Not.Null);
        Assert.That(server.WebViewUrl, Does.StartWith("http://127.0.0.1:4099/"));
        Assert.That(server.WebViewUrl, Does.Contain("/session/ses_123abc"));
    }

    [Test]
    public void WebViewUrl_WithoutActiveSession_ReturnsNull()
    {
        var server = new OpenCodeServer
        {
            EntityId = "test-entity",
            WorktreePath = @"C:\test\path",
            Port = 4099,
            ActiveSessionId = null
        };
        
        Assert.That(server.WebViewUrl, Is.Null);
    }

    [Test]
    public void WebViewUrl_ContainsEncodedPath()
    {
        var worktreePath = @"C:\my\path";
        var server = new OpenCodeServer
        {
            EntityId = "test-entity",
            WorktreePath = worktreePath,
            Port = 4099,
            ActiveSessionId = "ses_123"
        };
        
        var encodedPath = OpenCodeServer.Base64UrlEncode(worktreePath);
        
        Assert.That(server.WebViewUrl, Does.Contain(encodedPath));
    }

    [Test]
    public void WebViewUrl_MatchesExpectedFormat()
    {
        var server = new OpenCodeServer
        {
            EntityId = "test-entity",
            WorktreePath = @"C:\test",
            Port = 4099,
            ActiveSessionId = "ses_abc123"
        };
        
        var expectedEncodedPath = OpenCodeServer.Base64UrlEncode(@"C:\test");
        var expectedUrl = $"http://127.0.0.1:4099/{expectedEncodedPath}/session/ses_abc123";
        
        Assert.That(server.WebViewUrl, Is.EqualTo(expectedUrl));
    }

    [Test]
    public void BaseUrl_ReturnsCorrectFormat()
    {
        var server = new OpenCodeServer
        {
            EntityId = "test",
            WorktreePath = @"C:\test",
            Port = 4100
        };
        
        Assert.That(server.BaseUrl, Is.EqualTo("http://127.0.0.1:4100"));
    }
}
