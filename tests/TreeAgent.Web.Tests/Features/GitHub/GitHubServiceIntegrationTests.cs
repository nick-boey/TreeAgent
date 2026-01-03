using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TreeAgent.Web.Features.Commands;
using TreeAgent.Web.Features.GitHub;
using TreeAgent.Web.Features.PullRequests.Data;
using TreeAgent.Web.Features.PullRequests.Data.Entities;

namespace TreeAgent.Web.Tests.Features.GitHub;

/// <summary>
/// Integration tests that require real GitHub authentication.
/// These tests are marked with [Ignore] and should be run manually.
/// </summary>
[TestFixture]
[Category("Integration")]
public class GitHubServiceIntegrationTests
{
    private TreeAgentDbContext _db = null!;

    [SetUp]
    public void SetUp()
    {
        var options = new DbContextOptionsBuilder<TreeAgentDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new TreeAgentDbContext(options);
    }

    [TearDown]
    public void TearDown()
    {
        _db.Dispose();
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
            Name = "Test",
            LocalPath = ".",
            GitHubOwner = "octocat",  // Change to your test repo
            GitHubRepo = "Hello-World"
        };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GITHUB_TOKEN"] = token
            })
            .Build();

        var runner = new CommandRunner();
        var client = new GitHubClientWrapper();
        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<GitHubService>();
        var service = new GitHubService(_db, runner, config, client, logger);

        // Act
        var result = await service.GetOpenPullRequestsAsync(project.Id);

        // Assert - just verify it doesn't throw
        Assert.That(result, Is.Not.Null);
    }
}