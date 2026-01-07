using Homespun.Features.Beads.Data;
using Homespun.Features.OpenCode.Models;
using Homespun.Features.OpenCode.Services;
using Homespun.Features.PullRequests.Data;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Homespun.Tests.Features.OpenCode;

[TestFixture]
public class AgentWorkflowServiceTests
{
    private Mock<IOpenCodeServerManager> _mockServerManager = null!;
    private Mock<IOpenCodeClient> _mockClient = null!;
    private Mock<IOpenCodeConfigGenerator> _mockConfigGenerator = null!;
    private Mock<PullRequestDataService> _mockPullRequestService = null!;
    private Mock<ILogger<AgentWorkflowService>> _mockLogger = null!;

    [SetUp]
    public void SetUp()
    {
        _mockServerManager = new Mock<IOpenCodeServerManager>();
        _mockClient = new Mock<IOpenCodeClient>();
        _mockConfigGenerator = new Mock<IOpenCodeConfigGenerator>();
        _mockPullRequestService = new Mock<PullRequestDataService>(MockBehavior.Loose, null!);
        _mockLogger = new Mock<ILogger<AgentWorkflowService>>();
    }

    #region BuildInitialPromptForBeadsIssue Tests

    [Test]
    public void BuildInitialPromptForBeadsIssue_IncludesBranchName()
    {
        var issue = CreateTestIssue();
        var branchName = "core/feature/add-auth+bd-a3f8";
        
        var prompt = AgentWorkflowService.BuildInitialPromptForBeadsIssue(issue, branchName, "main", AgentMode.Building);
        
        Assert.That(prompt, Does.Contain(branchName));
        Assert.That(prompt, Does.Contain("Branch:").Or.Contain("branch"));
    }

    [Test]
    public void BuildInitialPromptForBeadsIssue_IncludesTitle()
    {
        var issue = CreateTestIssue();
        var branchName = "core/feature/add-auth+bd-a3f8";
        
        var prompt = AgentWorkflowService.BuildInitialPromptForBeadsIssue(issue, branchName, "main", AgentMode.Building);
        
        Assert.That(prompt, Does.Contain("Add Authentication"));
    }

    [Test]
    public void BuildInitialPromptForBeadsIssue_IncludesIssueId()
    {
        var issue = CreateTestIssue();
        var branchName = "core/feature/add-auth+bd-a3f8";
        
        var prompt = AgentWorkflowService.BuildInitialPromptForBeadsIssue(issue, branchName, "main", AgentMode.Building);
        
        Assert.That(prompt, Does.Contain("bd-a3f8"));
    }

    [Test]
    public void BuildInitialPromptForBeadsIssue_IncludesDescription_WhenProvided()
    {
        var issue = CreateTestIssue();
        issue.Description = "Implement OAuth2 authentication flow";
        var branchName = "core/feature/add-auth+bd-a3f8";
        
        var prompt = AgentWorkflowService.BuildInitialPromptForBeadsIssue(issue, branchName, "main", AgentMode.Building);
        
        Assert.That(prompt, Does.Contain("OAuth2 authentication flow"));
    }

    [Test]
    public void BuildInitialPromptForBeadsIssue_ExcludesDescription_WhenNull()
    {
        var issue = CreateTestIssue();
        issue.Description = null;
        var branchName = "core/feature/add-auth+bd-a3f8";
        
        var prompt = AgentWorkflowService.BuildInitialPromptForBeadsIssue(issue, branchName, "main", AgentMode.Building);
        
        Assert.That(prompt, Does.Not.Contain("Description:"));
    }

    [Test]
    public void BuildInitialPromptForBeadsIssue_IncludesPriority_WhenProvided()
    {
        var issue = CreateTestIssue();
        issue.Priority = 1;
        var branchName = "core/feature/add-auth+bd-a3f8";
        
        var prompt = AgentWorkflowService.BuildInitialPromptForBeadsIssue(issue, branchName, "main", AgentMode.Building);
        
        Assert.That(prompt, Does.Contain("P1"));
    }

    [Test]
    public void BuildInitialPromptForBeadsIssue_IncludesPrCreationInstructions()
    {
        var issue = CreateTestIssue();
        var branchName = "core/feature/add-auth+bd-a3f8";
        
        var prompt = AgentWorkflowService.BuildInitialPromptForBeadsIssue(issue, branchName, "main", AgentMode.Building);
        
        Assert.That(prompt, Does.Contain("gh pr create"));
    }

    [Test]
    public void BuildInitialPromptForBeadsIssue_InstructsToCommitToBranch()
    {
        var issue = CreateTestIssue();
        var branchName = "core/feature/add-auth+bd-a3f8";
        
        var prompt = AgentWorkflowService.BuildInitialPromptForBeadsIssue(issue, branchName, "main", AgentMode.Building);
        
        Assert.That(prompt, Does.Contain("commit").IgnoreCase);
        Assert.That(prompt, Does.Contain(branchName));
    }

    [Test]
    public void BuildInitialPromptForBeadsIssue_IncludesWorkflowInstructions()
    {
        var issue = CreateTestIssue();
        var branchName = "core/feature/add-auth+bd-a3f8";
        
        var prompt = AgentWorkflowService.BuildInitialPromptForBeadsIssue(issue, branchName, "main", AgentMode.Building);
        
        // Should instruct to create PR when done
        Assert.That(prompt, Does.Contain("create").IgnoreCase.And.Contain("pull request").IgnoreCase
            .Or.Contain("create").IgnoreCase.And.Contain("PR").IgnoreCase);
    }

    [Test]
    public void BuildInitialPromptForBeadsIssue_UsesProvidedBaseBranch()
    {
        var issue = CreateTestIssue();
        var branchName = "core/feature/add-auth+bd-a3f8";
        
        var prompt = AgentWorkflowService.BuildInitialPromptForBeadsIssue(issue, branchName, "develop", AgentMode.Building);
        
        // Should mention the base branch for the PR
        Assert.That(prompt, Does.Contain("develop"));
    }

    #endregion

    #region Helper Methods

    private static BeadsIssue CreateTestIssue()
    {
        return new BeadsIssue
        {
            Id = "bd-a3f8",
            Title = "Add Authentication",
            Type = BeadsIssueType.Feature,
            Status = BeadsIssueStatus.Open,
            Description = null,
            Priority = null
        };
    }

    #endregion
}
