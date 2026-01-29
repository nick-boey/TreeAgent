using Fleece.Core.Models;
using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Gitgraph.Data;
using Homespun.Features.Gitgraph.Services;
using Homespun.Features.GitHub;
using Homespun.Features.Projects;
using Homespun.Features.PullRequests;
using Homespun.Features.PullRequests.Data.Entities;
using Homespun.Features.Testing;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.Gitgraph;

/// <summary>
/// Tests for the GraphService's linked issue filtering functionality.
/// Issues that are linked to PRs should be filtered from the graph.
/// </summary>
[TestFixture]
public class GraphServiceLinkedIssueFilterTests
{
    private MockDataStore _dataStore = null!;
    private Mock<IProjectService> _mockProjectService = null!;
    private Mock<IGitHubService> _mockGitHubService = null!;
    private Mock<IFleeceService> _mockFleeceService = null!;
    private Mock<IClaudeSessionStore> _mockSessionStore = null!;
    private Mock<PullRequestWorkflowService> _mockWorkflowService = null!;
    private Mock<ILogger<GraphService>> _mockLogger = null!;
    private GraphService _service = null!;
    private Project _testProject = null!;

    [SetUp]
    public async Task SetUp()
    {
        _dataStore = new MockDataStore();

        // Create test project - use a temp path that exists with .fleece directory
        var testPath = Path.Combine(Path.GetTempPath(), $"graphservice-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(testPath);
        Directory.CreateDirectory(Path.Combine(testPath, ".fleece"));

        _testProject = new Project
        {
            Name = "test-repo",
            LocalPath = testPath,
            GitHubOwner = "test-owner",
            GitHubRepo = "test-repo",
            DefaultBranch = "main"
        };
        await _dataStore.AddProjectAsync(_testProject);

        // Set up mocks
        _mockProjectService = new Mock<IProjectService>();
        _mockProjectService.Setup(s => s.GetByIdAsync(_testProject.Id))
            .ReturnsAsync(_testProject);

        _mockGitHubService = new Mock<IGitHubService>();
        _mockGitHubService.Setup(s => s.GetOpenPullRequestsAsync(_testProject.Id))
            .ReturnsAsync(new List<PullRequestInfo>());
        _mockGitHubService.Setup(s => s.GetClosedPullRequestsAsync(_testProject.Id))
            .ReturnsAsync(new List<PullRequestInfo>());

        _mockFleeceService = new Mock<IFleeceService>();
        _mockFleeceService.Setup(s => s.ListIssuesAsync(_testProject.LocalPath, null, null, null, default))
            .ReturnsAsync(new List<Issue>());

        _mockSessionStore = new Mock<IClaudeSessionStore>();
        _mockSessionStore.Setup(s => s.GetByProjectId(_testProject.Id))
            .Returns(new List<ClaudeSession>());

        _mockWorkflowService = new Mock<PullRequestWorkflowService>(
            MockBehavior.Loose,
            _dataStore,
            null!,
            null!,
            null!,
            null!);

        _mockLogger = new Mock<ILogger<GraphService>>();

        _service = new GraphService(
            _mockProjectService.Object,
            _mockGitHubService.Object,
            _mockFleeceService.Object,
            _mockSessionStore.Object,
            _dataStore,
            _mockWorkflowService.Object,
            _mockLogger.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _dataStore.Clear();

        // Clean up the temp directory
        if (_testProject != null && Directory.Exists(_testProject.LocalPath))
        {
            try
            {
                Directory.Delete(_testProject.LocalPath, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    #region Linked Issue Filtering Tests

    [Test]
    public async Task BuildGraphAsync_IssueWithLinkedPR_IsFilteredFromGraph()
    {
        // Arrange - Create an issue and a PR linked to it
        var issue = CreateIssue("hsp-123");
        var pr = await CreatePullRequestWithLinkedIssue(_testProject.Id, "hsp-123");

        _mockFleeceService.Setup(s => s.ListIssuesAsync(_testProject.LocalPath, null, null, null, default))
            .ReturnsAsync(new List<Issue> { issue });

        // Act
        var graph = await _service.BuildGraphAsync(_testProject.Id);

        // Assert - Issue should NOT be in the graph
        var issueNodes = graph.Nodes.OfType<IssueNode>().ToList();
        Assert.That(issueNodes, Has.Count.EqualTo(0), "Issue with linked PR should be filtered from graph");
    }

    [Test]
    public async Task BuildGraphAsync_IssueWithoutLinkedPR_IsIncludedInGraph()
    {
        // Arrange - Create an issue without any linked PR
        var issue = CreateIssue("hsp-123");

        _mockFleeceService.Setup(s => s.ListIssuesAsync(_testProject.LocalPath, null, null, null, default))
            .ReturnsAsync(new List<Issue> { issue });

        // Act
        var graph = await _service.BuildGraphAsync(_testProject.Id);

        // Assert - Issue should be in the graph
        var issueNodes = graph.Nodes.OfType<IssueNode>().ToList();
        Assert.That(issueNodes, Has.Count.EqualTo(1), "Issue without linked PR should be included in graph");
        Assert.That(issueNodes[0].Issue.Id, Is.EqualTo("hsp-123"));
    }

    [Test]
    public async Task BuildGraphAsync_MixedIssues_OnlyUnlinkedAreIncluded()
    {
        // Arrange - Create multiple issues, some linked, some not
        var linkedIssue = CreateIssue("linked-issue");
        var unlinkedIssue1 = CreateIssue("unlinked-1");
        var unlinkedIssue2 = CreateIssue("unlinked-2");

        await CreatePullRequestWithLinkedIssue(_testProject.Id, "linked-issue");

        _mockFleeceService.Setup(s => s.ListIssuesAsync(_testProject.LocalPath, null, null, null, default))
            .ReturnsAsync(new List<Issue> { linkedIssue, unlinkedIssue1, unlinkedIssue2 });

        // Act
        var graph = await _service.BuildGraphAsync(_testProject.Id);

        // Assert - Only unlinked issues should be in the graph
        var issueNodes = graph.Nodes.OfType<IssueNode>().ToList();
        Assert.That(issueNodes, Has.Count.EqualTo(2));
        Assert.That(issueNodes.Any(n => n.Issue.Id == "unlinked-1"), Is.True);
        Assert.That(issueNodes.Any(n => n.Issue.Id == "unlinked-2"), Is.True);
        Assert.That(issueNodes.Any(n => n.Issue.Id == "linked-issue"), Is.False);
    }

    [Test]
    public async Task BuildGraphAsync_CaseInsensitiveIssueIdMatching()
    {
        // Arrange - Create an issue with lowercase ID and PR with uppercase ID
        var issue = CreateIssue("HSP-ABC");
        await CreatePullRequestWithLinkedIssue(_testProject.Id, "hsp-abc"); // Lowercase

        _mockFleeceService.Setup(s => s.ListIssuesAsync(_testProject.LocalPath, null, null, null, default))
            .ReturnsAsync(new List<Issue> { issue });

        // Act
        var graph = await _service.BuildGraphAsync(_testProject.Id);

        // Assert - Issue should be filtered (case-insensitive matching)
        var issueNodes = graph.Nodes.OfType<IssueNode>().ToList();
        Assert.That(issueNodes, Has.Count.EqualTo(0), "Issue matching should be case-insensitive");
    }

    [Test]
    public async Task BuildGraphAsync_PRWithNullBeadsIssueId_DoesNotFilterAnyIssue()
    {
        // Arrange - Create PR without linked issue and an issue
        var issue = CreateIssue("hsp-123");
        await CreatePullRequest(_testProject.Id, beadsIssueId: null);

        _mockFleeceService.Setup(s => s.ListIssuesAsync(_testProject.LocalPath, null, null, null, default))
            .ReturnsAsync(new List<Issue> { issue });

        // Act
        var graph = await _service.BuildGraphAsync(_testProject.Id);

        // Assert - Issue should be included
        var issueNodes = graph.Nodes.OfType<IssueNode>().ToList();
        Assert.That(issueNodes, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task BuildGraphAsync_PRWithEmptyBeadsIssueId_DoesNotFilterAnyIssue()
    {
        // Arrange - Create PR with empty linked issue ID and an issue
        var issue = CreateIssue("hsp-123");
        await CreatePullRequest(_testProject.Id, beadsIssueId: "");

        _mockFleeceService.Setup(s => s.ListIssuesAsync(_testProject.LocalPath, null, null, null, default))
            .ReturnsAsync(new List<Issue> { issue });

        // Act
        var graph = await _service.BuildGraphAsync(_testProject.Id);

        // Assert - Issue should be included
        var issueNodes = graph.Nodes.OfType<IssueNode>().ToList();
        Assert.That(issueNodes, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task BuildGraphAsync_MultiplePRsLinkedToDifferentIssues_AllLinkedIssuesFiltered()
    {
        // Arrange - Create multiple PRs linked to different issues
        var issue1 = CreateIssue("issue-1");
        var issue2 = CreateIssue("issue-2");
        var issue3 = CreateIssue("issue-3"); // Not linked

        await CreatePullRequestWithLinkedIssue(_testProject.Id, "issue-1");
        await CreatePullRequestWithLinkedIssue(_testProject.Id, "issue-2");

        _mockFleeceService.Setup(s => s.ListIssuesAsync(_testProject.LocalPath, null, null, null, default))
            .ReturnsAsync(new List<Issue> { issue1, issue2, issue3 });

        // Act
        var graph = await _service.BuildGraphAsync(_testProject.Id);

        // Assert - Only issue-3 should be in the graph
        var issueNodes = graph.Nodes.OfType<IssueNode>().ToList();
        Assert.That(issueNodes, Has.Count.EqualTo(1));
        Assert.That(issueNodes[0].Issue.Id, Is.EqualTo("issue-3"));
    }

    [Test]
    public async Task BuildGraphAsync_NoIssues_ReturnsEmptyGraph()
    {
        // Arrange - No issues
        _mockFleeceService.Setup(s => s.ListIssuesAsync(_testProject.LocalPath, null, null, null, default))
            .ReturnsAsync(new List<Issue>());

        // Act
        var graph = await _service.BuildGraphAsync(_testProject.Id);

        // Assert
        var issueNodes = graph.Nodes.OfType<IssueNode>().ToList();
        Assert.That(issueNodes, Has.Count.EqualTo(0));
    }

    [Test]
    public async Task BuildGraphAsync_NoPRs_AllIssuesIncluded()
    {
        // Arrange - Multiple issues, no PRs
        var issue1 = CreateIssue("issue-1");
        var issue2 = CreateIssue("issue-2");

        _mockFleeceService.Setup(s => s.ListIssuesAsync(_testProject.LocalPath, null, null, null, default))
            .ReturnsAsync(new List<Issue> { issue1, issue2 });

        // Act
        var graph = await _service.BuildGraphAsync(_testProject.Id);

        // Assert - All issues should be included
        var issueNodes = graph.Nodes.OfType<IssueNode>().ToList();
        Assert.That(issueNodes, Has.Count.EqualTo(2));
    }

    #endregion

    #region Helper Methods

    private static Issue CreateIssue(string id, IssueStatus status = IssueStatus.Next)
    {
        return new Issue
        {
            Id = id,
            Title = $"Issue {id}",
            Status = status,
            Type = IssueType.Task,
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdate = DateTimeOffset.UtcNow
        };
    }

    private async Task<PullRequest> CreatePullRequestWithLinkedIssue(string projectId, string beadsIssueId)
    {
        var pr = new PullRequest
        {
            ProjectId = projectId,
            Title = $"PR linked to {beadsIssueId}",
            BranchName = $"issues/feature/test+{beadsIssueId}",
            BeadsIssueId = beadsIssueId,
            Status = OpenPullRequestStatus.InDevelopment
        };
        await _dataStore.AddPullRequestAsync(pr);
        return pr;
    }

    private async Task<PullRequest> CreatePullRequest(string projectId, string? beadsIssueId)
    {
        var pr = new PullRequest
        {
            ProjectId = projectId,
            Title = "Test PR",
            BranchName = "feature/test",
            BeadsIssueId = beadsIssueId,
            Status = OpenPullRequestStatus.InDevelopment
        };
        await _dataStore.AddPullRequestAsync(pr);
        return pr;
    }

    #endregion
}
