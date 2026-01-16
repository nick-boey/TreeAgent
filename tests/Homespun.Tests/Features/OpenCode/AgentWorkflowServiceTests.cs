using Homespun.Features.Agents.Abstractions.Models;
using Homespun.Features.Agents.Services;
using Homespun.Features.Beads.Data;
using Homespun.Features.PullRequests.Data;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Homespun.Tests.Features.OpenCode;

[TestFixture]
public class AgentWorkflowServiceTests
{

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

    #region Agent Mode Tests

    [Test]
    public void BuildInitialPromptForBeadsIssue_PlanningMode_IncludesWorkflowInstructions()
    {
        var issue = CreateTestIssue();
        var branchName = "core/feature/add-auth+bd-a3f8";

        var prompt = AgentWorkflowService.BuildInitialPromptForBeadsIssue(issue, branchName, "main", AgentMode.Planning);

        Assert.That(prompt, Does.Contain("Review the change described above carefully"));
        Assert.That(prompt, Does.Contain("create an implementation plan"));
        Assert.That(prompt, Does.Contain("Wait for approval before implementing"));
    }

    [Test]
    public void BuildInitialPromptForBeadsIssue_BuildingMode_IncludesImplementationInstructions()
    {
        var issue = CreateTestIssue();
        var branchName = "core/feature/add-auth+bd-a3f8";

        var prompt = AgentWorkflowService.BuildInitialPromptForBeadsIssue(issue, branchName, "main", AgentMode.Building);

        Assert.That(prompt, Does.Contain("Implement the change described above"));
        Assert.That(prompt, Does.Contain("Write tests"));
        Assert.That(prompt, Does.Contain("gh pr create"));
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
