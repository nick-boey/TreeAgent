using Homespun.Features.SignalR;
using Microsoft.Extensions.Options;

namespace Homespun.Tests.Features.SignalR;

[TestFixture]
public class SignalRUrlProviderTests
{
    [Test]
    public void GetHubUrl_WithInternalBaseUrl_ReturnsInternalUrl()
    {
        // Arrange
        var options = Options.Create(new SignalROptions
        {
            InternalBaseUrl = "http://localhost:8080"
        });
        var provider = new SignalRUrlProvider(options);

        // Act
        var result = provider.GetHubUrl("/hubs/claudecode");

        // Assert
        Assert.That(result, Is.EqualTo("http://localhost:8080/hubs/claudecode"));
    }

    [Test]
    public void GetHubUrl_WithoutLeadingSlash_AddsSlash()
    {
        // Arrange
        var options = Options.Create(new SignalROptions
        {
            InternalBaseUrl = "http://localhost:8080"
        });
        var provider = new SignalRUrlProvider(options);

        // Act
        var result = provider.GetHubUrl("hubs/claudecode");

        // Assert
        Assert.That(result, Is.EqualTo("http://localhost:8080/hubs/claudecode"));
    }

    [Test]
    public void GetHubUrl_WithTrailingSlashOnBaseUrl_HandlesCorrectly()
    {
        // Arrange
        var options = Options.Create(new SignalROptions
        {
            InternalBaseUrl = "http://localhost:8080/"
        });
        var provider = new SignalRUrlProvider(options);

        // Act
        var result = provider.GetHubUrl("/hubs/claudecode");

        // Assert
        Assert.That(result, Is.EqualTo("http://localhost:8080/hubs/claudecode"));
    }

    [Test]
    public void GetHubUrl_WithEmptyInternalBaseUrl_UsesDefaultLocalhost()
    {
        // Arrange
        var options = Options.Create(new SignalROptions
        {
            InternalBaseUrl = ""
        });
        var provider = new SignalRUrlProvider(options);

        // Act
        var result = provider.GetHubUrl("/hubs/claudecode");

        // Assert
        Assert.That(result, Is.EqualTo("http://localhost:5000/hubs/claudecode"));
    }

    [Test]
    public void GetHubUrl_WithNullInternalBaseUrl_UsesDefaultLocalhost()
    {
        // Arrange
        var options = Options.Create(new SignalROptions
        {
            InternalBaseUrl = null
        });
        var provider = new SignalRUrlProvider(options);

        // Act
        var result = provider.GetHubUrl("/hubs/claudecode");

        // Assert
        Assert.That(result, Is.EqualTo("http://localhost:5000/hubs/claudecode"));
    }

    [Test]
    public void GetHubUrl_WithDifferentHub_ReturnsCorrectUrl()
    {
        // Arrange
        var options = Options.Create(new SignalROptions
        {
            InternalBaseUrl = "http://localhost:8080"
        });
        var provider = new SignalRUrlProvider(options);

        // Act
        var result = provider.GetHubUrl("/hubs/notifications");

        // Assert
        Assert.That(result, Is.EqualTo("http://localhost:8080/hubs/notifications"));
    }
}
