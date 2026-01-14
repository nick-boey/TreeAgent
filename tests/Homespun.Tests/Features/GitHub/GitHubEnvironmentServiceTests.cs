using Homespun.Features.GitHub;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.GitHub;

[TestFixture]
public class GitHubEnvironmentServiceTests
{
    private Mock<ILogger<GitHubEnvironmentService>> _mockLogger = null!;

    [SetUp]
    public void SetUp()
    {
        _mockLogger = new Mock<ILogger<GitHubEnvironmentService>>();
    }

    [Test]
    public void GetGitHubEnvironment_WithToken_ReturnsExpectedVariables()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GITHUB_TOKEN"] = "ghp_test_token_12345"
            })
            .Build();

        using var service = new GitHubEnvironmentService(config, _mockLogger.Object);

        // Act
        var env = service.GetGitHubEnvironment();

        // Assert
        Assert.That(env, Does.ContainKey("GITHUB_TOKEN"));
        Assert.That(env["GITHUB_TOKEN"], Is.EqualTo("ghp_test_token_12345"));
        Assert.That(env, Does.ContainKey("GH_TOKEN"));
        Assert.That(env["GH_TOKEN"], Is.EqualTo("ghp_test_token_12345"));
        Assert.That(env, Does.ContainKey("GIT_ASKPASS"));
        Assert.That(env, Does.ContainKey("GIT_TERMINAL_PROMPT"));
        Assert.That(env["GIT_TERMINAL_PROMPT"], Is.EqualTo("0"));
    }

    [Test]
    public void GetGitHubEnvironment_WithoutToken_DoesNotIncludeTokenVars()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        using var service = new GitHubEnvironmentService(config, _mockLogger.Object);

        // Act
        var env = service.GetGitHubEnvironment();

        // Assert - Git identity is always present, but token-related vars should not be
        Assert.That(env, Does.Not.ContainKey("GITHUB_TOKEN"));
        Assert.That(env, Does.Not.ContainKey("GH_TOKEN"));
        Assert.That(env, Does.Not.ContainKey("GIT_ASKPASS"));
        Assert.That(env, Does.Not.ContainKey("GIT_TERMINAL_PROMPT"));

        // Git identity should always be present for git operations
        Assert.That(env, Does.ContainKey("GIT_AUTHOR_NAME"));
        Assert.That(env, Does.ContainKey("GIT_AUTHOR_EMAIL"));
        Assert.That(env, Does.ContainKey("GIT_COMMITTER_NAME"));
        Assert.That(env, Does.ContainKey("GIT_COMMITTER_EMAIL"));
    }

    [Test]
    public void IsConfigured_WithToken_ReturnsTrue()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GITHUB_TOKEN"] = "ghp_test_token"
            })
            .Build();

        using var service = new GitHubEnvironmentService(config, _mockLogger.Object);

        // Assert
        Assert.That(service.IsConfigured, Is.True);
    }

    [Test]
    public void IsConfigured_WithoutToken_ReturnsFalse()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        using var service = new GitHubEnvironmentService(config, _mockLogger.Object);

        // Assert
        Assert.That(service.IsConfigured, Is.False);
    }

    [Test]
    public void GetMaskedToken_WithToken_ReturnsPartiallyMaskedToken()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GITHUB_TOKEN"] = "ghp_abc123xyz789"
            })
            .Build();

        using var service = new GitHubEnvironmentService(config, _mockLogger.Object);

        // Act
        var masked = service.GetMaskedToken();

        // Assert - Token "ghp_abc123xyz789" -> first 4 "ghp_" + "***" + last 4 "z789"
        Assert.That(masked, Is.Not.Null);
        Assert.That(masked, Does.StartWith("ghp_"));
        Assert.That(masked, Does.EndWith("z789"));
        Assert.That(masked, Does.Contain("***"));
    }

    [Test]
    public void GetMaskedToken_WithoutToken_ReturnsNull()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        using var service = new GitHubEnvironmentService(config, _mockLogger.Object);

        // Act
        var masked = service.GetMaskedToken();

        // Assert
        Assert.That(masked, Is.Null);
    }

    [Test]
    public void GetMaskedToken_WithShortToken_ReturnsMasked()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GITHUB_TOKEN"] = "short"
            })
            .Build();

        using var service = new GitHubEnvironmentService(config, _mockLogger.Object);

        // Act
        var masked = service.GetMaskedToken();

        // Assert
        Assert.That(masked, Is.EqualTo("***"));
    }

    [Test]
    public void TokenResolution_PrefersGitHubTokenKey()
    {
        // Arrange - Test that GitHub:Token takes priority over GITHUB_TOKEN
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GitHub:Token"] = "ghp_preferred_token",
                ["GITHUB_TOKEN"] = "ghp_fallback_token"
            })
            .Build();

        using var service = new GitHubEnvironmentService(config, _mockLogger.Object);

        // Act
        var env = service.GetGitHubEnvironment();

        // Assert
        Assert.That(env["GITHUB_TOKEN"], Is.EqualTo("ghp_preferred_token"));
    }

    [Test]
    public async Task CheckGhAuthStatusAsync_WithToken_ReturnsAuthenticated()
    {
        // Arrange - Token is configured but gh CLI might not be available
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GITHUB_TOKEN"] = "ghp_test_token"
            })
            .Build();

        using var service = new GitHubEnvironmentService(config, _mockLogger.Object);

        // Act
        var status = await service.CheckGhAuthStatusAsync();

        // Assert - Should at least report token is configured even if gh CLI fails
        Assert.That(status.IsAuthenticated, Is.True);
        Assert.That(status.AuthMethod, Is.Not.EqualTo(GitHubAuthMethod.None));
    }

    [Test]
    public async Task CheckGhAuthStatusAsync_WithoutToken_ReportsNoTokenConfigured()
    {
        // Arrange - No token configured in our configuration
        // Note: This test verifies the service correctly identifies no token is configured.
        // The gh CLI might still be authenticated globally, which is outside our control.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        using var service = new GitHubEnvironmentService(config, _mockLogger.Object);

        // Act
        var status = await service.CheckGhAuthStatusAsync();

        // Assert - Service should report that no token is configured via IsConfigured
        Assert.That(service.IsConfigured, Is.False);
        // The auth status may still show authenticated if gh CLI is globally authenticated,
        // but our service should report Token auth method is not available
        Assert.That(status.AuthMethod, Is.Not.EqualTo(GitHubAuthMethod.Token));
        Assert.That(status.AuthMethod, Is.Not.EqualTo(GitHubAuthMethod.Both));
    }

    [Test]
    public void GIT_ASKPASS_ScriptPath_IsNotEmpty()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GITHUB_TOKEN"] = "ghp_test_token"
            })
            .Build();

        using var service = new GitHubEnvironmentService(config, _mockLogger.Object);

        // Act
        var env = service.GetGitHubEnvironment();

        // Assert
        Assert.That(env["GIT_ASKPASS"], Is.Not.Empty);
        Assert.That(File.Exists(env["GIT_ASKPASS"]), Is.True);
    }
}
