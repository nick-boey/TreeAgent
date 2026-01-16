using Homespun.Features.Commands;
using Homespun.Features.GitHub;
using Homespun.Features.PullRequests.Data.Entities;
using Homespun.Tests.Helpers;
using Microsoft.Extensions.Configuration;
using Moq;

namespace Homespun.Tests.Features.GitHub;

/// <summary>
/// Integration tests that require real GitHub authentication.
/// These tests are marked with [Ignore] and should be run manually.
/// </summary>
[TestFixture]
[Category("Integration")]
public class GitHubServiceIntegrationTests
{
    private TestDataStore _dataStore = null!;

    [SetUp]
    public void SetUp()
    {
        _dataStore = new TestDataStore();
    }

    [TearDown]
    public void TearDown()
    {
        _dataStore.Clear();
    }

    [Test]
    [Category("Integration")]
    public async Task GetOpenPullRequests_RealGitHub_ReturnsData()
    {
        // This test requires:
        // 1. GITHUB_TOKEN environment variable set
        // 2. A real GitHub repository configured

        // Skip if not configured
        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (string.IsNullOrEmpty(token))
        {
            Assert.Ignore("GITHUB_TOKEN environment variable not set");
            return;
        }

        // Add a project pointing to a real repo
        var project = new Project
        {
            Name = "Hello-World",
            LocalPath = ".",
            GitHubOwner = "octocat",  // Change to your test repo
            GitHubRepo = "Hello-World",
            DefaultBranch = "master"
        };
        await _dataStore.AddProjectAsync(project);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GITHUB_TOKEN"] = token
            })
            .Build();

        var mockGitHubEnv = new Mock<IGitHubEnvironmentService>();
        mockGitHubEnv.Setup(g => g.GetGitHubEnvironment()).Returns(new Dictionary<string, string>());
        var runner = new CommandRunner(mockGitHubEnv.Object, new Microsoft.Extensions.Logging.Abstractions.NullLogger<CommandRunner>());
        var client = new GitHubClientWrapper();
        var mockLinkingService = new Mock<IIssuePrLinkingService>();
        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<GitHubService>();
        var service = new GitHubService(_dataStore, runner, config, client, mockLinkingService.Object, logger);

        // Act
        var result = await service.GetOpenPullRequestsAsync(project.Id);

        // Assert - just verify it doesn't throw
        Assert.That(result, Is.Not.Null);
    }
}
