using Homespun.Features.Agents.Hubs;
using Homespun.Features.Beads.Services;
using Homespun.Features.GitHub;
using Homespun.Features.Projects;
using Homespun.Features.PullRequests.Data;
using Homespun.Features.PullRequests.Data.Entities;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NUnit.Framework;

namespace Homespun.Tests.Features.GitHub;

/// <summary>
/// Tests for GitHubSyncPollingService SignalR event broadcasting.
/// </summary>
[TestFixture]
public class GitHubSyncPollingServiceTests
{
    private IServiceScopeFactory _mockServiceScopeFactory;
    private IHubContext<AgentHub> _mockHubContext;
    private IOptions<GitHubSyncPollingOptions> _mockOptions;
    private ILogger<GitHubSyncPollingService> _mockLogger;
    private GitHubSyncPollingService _service;

    [SetUp]
    public void SetUp()
    {
        _mockServiceScopeFactory = Substitute.For<IServiceScopeFactory>();
        _mockHubContext = Substitute.For<IHubContext<AgentHub>>();
        _mockOptions = Substitute.For<IOptions<GitHubSyncPollingOptions>>();
        _mockLogger = Substitute.For<ILogger<GitHubSyncPollingService>>();

        _mockOptions.Value.Returns(new GitHubSyncPollingOptions { PollingIntervalSeconds = 60 });

        _service = new GitHubSyncPollingService(
            _mockServiceScopeFactory,
            _mockHubContext,
            _mockOptions,
            _mockLogger);
    }

    [Test]
    public async Task PollProjectAsync_BroadcastsPullRequestsSyncedEvent()
    {
        // Arrange
        var project = new Project
        {
            Id = "test-project",
            LocalPath = "/test/path",
            GitHubOwner = "owner",
            GitHubRepo = "repo"
        };

        var syncResult = new SyncResult
        {
            Imported = 2,
            Updated = 1,
            Removed = 0
        };

        // Setup service scope
        var mockScope = Substitute.For<IServiceScope>();
        var mockServiceProvider = Substitute.For<IServiceProvider>();
        var mockDataStore = Substitute.For<IDataStore>();
        var mockGitHubService = Substitute.For<IGitHubService>();
        var mockBeadsDatabaseService = Substitute.For<IBeadsDatabaseService>();

        _mockServiceScopeFactory.CreateScope().Returns(mockScope);
        mockScope.ServiceProvider.Returns(mockServiceProvider);
        mockServiceProvider.GetRequiredService<IDataStore>().Returns(mockDataStore);
        mockServiceProvider.GetRequiredService<IGitHubService>().Returns(mockGitHubService);
        mockServiceProvider.GetRequiredService<IBeadsDatabaseService>().Returns(mockBeadsDatabaseService);

        // Setup data store to return our test project
        mockDataStore.GetAllProjects().Returns(new List<Project> { project });
        mockDataStore.GetPullRequestsByProject(project.Id).Returns(new List<PullRequest>());

        // Setup GitHub service mock
        mockGitHubService.IsConfiguredAsync(project.Id).Returns(Task.FromResult(true));
        mockGitHubService.SyncPullRequestsAsync(project.Id).Returns(Task.FromResult(syncResult));

        // Setup hub clients
        var mockClients = Substitute.For<IHubClients>();
        var mockClientProxy = Substitute.For<IClientProxy>();
        _mockHubContext.Clients.Returns(mockClients);
        mockClients.All.Returns(mockClientProxy);

        // Act
        var cts = new CancellationTokenSource();
        var executeTask = _service.StartAsync(cts.Token);

        // Wait a bit for the first poll
        await Task.Delay(100);
        cts.Cancel();

        try
        {
            await executeTask;
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation token is triggered
        }

        // Assert
        await mockClientProxy.Received(1).SendAsync(
            "PullRequestsSynced",
            project.Id,
            Arg.Is<SyncResult>(r => r.Imported == 2 && r.Updated == 1 && r.Removed == 0),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PollProjectAsync_SkipsProjectsWithoutGitHubConfig()
    {
        // Arrange
        var project = new Project
        {
            Id = "test-project",
            LocalPath = "/test/path",
            // No GitHub owner/repo configured
            GitHubOwner = null,
            GitHubRepo = null
        };

        // Setup service scope
        var mockScope = Substitute.For<IServiceScope>();
        var mockServiceProvider = Substitute.For<IServiceProvider>();
        var mockDataStore = Substitute.For<IDataStore>();
        var mockGitHubService = Substitute.For<IGitHubService>();

        _mockServiceScopeFactory.CreateScope().Returns(mockScope);
        mockScope.ServiceProvider.Returns(mockServiceProvider);
        mockServiceProvider.GetRequiredService<IDataStore>().Returns(mockDataStore);
        mockServiceProvider.GetRequiredService<IGitHubService>().Returns(mockGitHubService);

        mockDataStore.GetAllProjects().Returns(new List<Project> { project });

        // Setup hub clients
        var mockClients = Substitute.For<IHubClients>();
        var mockClientProxy = Substitute.For<IClientProxy>();
        _mockHubContext.Clients.Returns(mockClients);
        mockClients.All.Returns(mockClientProxy);

        // Act
        var cts = new CancellationTokenSource();
        var executeTask = _service.StartAsync(cts.Token);

        await Task.Delay(100);
        cts.Cancel();

        try
        {
            await executeTask;
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Assert - No sync should have been attempted
        await mockGitHubService.DidNotReceive().SyncPullRequestsAsync(Arg.Any<string>());
        await mockClientProxy.DidNotReceive().SendAsync(
            "PullRequestsSynced",
            Arg.Any<string>(),
            Arg.Any<SyncResult>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PollProjectAsync_HandlesGitHubServiceErrors()
    {
        // Arrange
        var project = new Project
        {
            Id = "test-project",
            LocalPath = "/test/path",
            GitHubOwner = "owner",
            GitHubRepo = "repo"
        };

        // Setup service scope
        var mockScope = Substitute.For<IServiceScope>();
        var mockServiceProvider = Substitute.For<IServiceProvider>();
        var mockDataStore = Substitute.For<IDataStore>();
        var mockGitHubService = Substitute.For<IGitHubService>();
        var mockBeadsDatabaseService = Substitute.For<IBeadsDatabaseService>();

        _mockServiceScopeFactory.CreateScope().Returns(mockScope);
        mockScope.ServiceProvider.Returns(mockServiceProvider);
        mockServiceProvider.GetRequiredService<IDataStore>().Returns(mockDataStore);
        mockServiceProvider.GetRequiredService<IGitHubService>().Returns(mockGitHubService);
        mockServiceProvider.GetRequiredService<IBeadsDatabaseService>().Returns(mockBeadsDatabaseService);

        mockDataStore.GetAllProjects().Returns(new List<Project> { project });
        mockGitHubService.IsConfiguredAsync(project.Id).Returns(Task.FromResult(true));
        mockGitHubService.SyncPullRequestsAsync(project.Id)
            .Returns(x => throw new Exception("GitHub API error"));

        // Setup hub clients
        var mockClients = Substitute.For<IHubClients>();
        var mockClientProxy = Substitute.For<IClientProxy>();
        _mockHubContext.Clients.Returns(mockClients);
        mockClients.All.Returns(mockClientProxy);

        // Act
        var cts = new CancellationTokenSource();
        var executeTask = _service.StartAsync(cts.Token);

        await Task.Delay(100);
        cts.Cancel();

        try
        {
            await executeTask;
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Assert - Should log error but not crash or broadcast event
        _mockLogger.Received().Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Error during GitHub sync")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());

        await mockClientProxy.DidNotReceive().SendAsync(
            "PullRequestsSynced",
            Arg.Any<string>(),
            Arg.Any<SyncResult>(),
            Arg.Any<CancellationToken>());
    }
}