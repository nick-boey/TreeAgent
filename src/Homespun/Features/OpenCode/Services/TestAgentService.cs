using System.Collections.Concurrent;
using Homespun.Features.Agents.Abstractions;
using Homespun.Features.Agents.Abstractions.Models;
using Homespun.Features.Commands;
using Homespun.Features.Git;
using Homespun.Features.PullRequests.Data;

namespace Homespun.Features.OpenCode.Services;

/// <summary>
/// Service for running test agents to debug Claude Code integration.
/// Only available in DEBUG builds.
/// </summary>
public class TestAgentService(
    IAgentHarnessFactory harnessFactory,
    IGitWorktreeService worktreeService,
    IDataStore dataStore,
    ICommandRunner commandRunner,
    ILogger<TestAgentService> logger) : ITestAgentService
{
    private const string TestBranchName = "hsp/test";
    private const string TestFileName = "test.txt";
    private const string TestEntityPrefix = "test-agent-";

    // Use Claude Code UI harness for test agents (uses Claude Max subscription)
    private const string TestHarnessType = "claudeui";

    private readonly ConcurrentDictionary<string, TestAgentStatus> _activeTestAgents = new();

    public async Task<TestAgentResult> StartTestAgentAsync(string projectId, CancellationToken ct = default)
    {
        try
        {
            // 1. Get project
            var project = dataStore.GetProject(projectId);
            if (project == null)
            {
                return TestAgentResult.Fail($"Project {projectId} not found");
            }

            logger.LogInformation("Starting test agent for project {ProjectId} at {LocalPath}",
                projectId, project.LocalPath);

            // 2. Clean up any existing test worktree and branch first
            // This ensures a fresh start even if previous cleanup failed
            if (await worktreeService.WorktreeExistsAsync(project.LocalPath, TestBranchName))
            {
                logger.LogInformation("Removing existing test worktree for branch {Branch}", TestBranchName);
                await worktreeService.RemoveWorktreeAsync(project.LocalPath, TestBranchName);

                // Also delete the branch to ensure clean state
                await commandRunner.RunAsync("git", $"branch -D \"{TestBranchName}\"", project.LocalPath);
            }

            // Prune any stale worktree references
            await worktreeService.PruneWorktreesAsync(project.LocalPath);

            // 3. Create worktree path
            // Worktree will be: ~/.homespun/src/<project>/hsp/test
            var worktreePath = await worktreeService.CreateWorktreeAsync(
                project.LocalPath,
                TestBranchName,
                createBranch: true,
                baseBranch: project.DefaultBranch);

            if (string.IsNullOrEmpty(worktreePath))
            {
                return TestAgentResult.Fail("Failed to create test worktree");
            }

            // Normalize the path
            worktreePath = Path.GetFullPath(worktreePath);
            logger.LogInformation("Test worktree at {WorktreePath}", worktreePath);

            // 4. Delete test.txt if it exists
            var testFilePath = Path.Combine(worktreePath, TestFileName);
            if (File.Exists(testFilePath))
            {
                File.Delete(testFilePath);
                logger.LogInformation("Deleted existing {TestFile}", testFilePath);
            }

            // 5. Get the Claude Code UI harness
            var harness = harnessFactory.GetHarness(TestHarnessType);

            // 6. Start agent with initial prompt
            var entityId = $"{TestEntityPrefix}{projectId}";
            var options = new AgentStartOptions
            {
                EntityId = entityId,
                WorkingDirectory = worktreePath,
                SessionTitle = "Test Agent Session",
                InitialPrompt = new AgentPrompt
                {
                    Text = $"Create a file called '{TestFileName}' in the current directory with the content 'Hello from Claude Code test agent - created at {DateTime.UtcNow:O}'"
                }
            };

            var agent = await harness.StartAgentAsync(options, ct);

            logger.LogInformation("Test Claude Code agent started at {WebViewUrl}", agent.WebViewUrl);

            // 7. Track status
            var status = new TestAgentStatus
            {
                ProjectId = projectId,
                ServerUrl = agent.ApiBaseUrl ?? "",
                SessionId = agent.ActiveSessionId ?? "",
                WorktreePath = worktreePath,
                WebViewUrl = agent.WebViewUrl,
                StartedAt = DateTime.UtcNow
            };
            _activeTestAgents[projectId] = status;

            return TestAgentResult.Ok(agent.ApiBaseUrl ?? "", agent.ActiveSessionId ?? "", worktreePath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start test agent for project {ProjectId}", projectId);
            return TestAgentResult.Fail(ex.Message);
        }
    }

    public async Task StopTestAgentAsync(string projectId, CancellationToken ct = default)
    {
        try
        {
            var entityId = $"{TestEntityPrefix}{projectId}";

            // 1. Stop the agent via harness
            var harness = harnessFactory.GetHarness(TestHarnessType);
            await harness.StopAgentAsync(entityId, ct);
            logger.LogInformation("Stopped test agent for project {ProjectId}", projectId);

            // 2. Get project for worktree cleanup
            var project = dataStore.GetProject(projectId);
            if (project != null)
            {
                // 3. Remove worktree
                var removed = await worktreeService.RemoveWorktreeAsync(project.LocalPath, TestBranchName);
                if (removed)
                {
                    logger.LogInformation("Removed test worktree for branch {Branch}", TestBranchName);
                }
                else
                {
                    logger.LogWarning("Failed to remove test worktree for branch {Branch}", TestBranchName);
                }

                // 4. Delete the branch
                var branchResult = await commandRunner.RunAsync(
                    "git",
                    $"branch -D \"{TestBranchName}\"",
                    project.LocalPath);

                if (branchResult.Success)
                {
                    logger.LogInformation("Deleted test branch {Branch}", TestBranchName);
                }
                else
                {
                    logger.LogWarning("Failed to delete test branch {Branch}: {Error}", TestBranchName, branchResult.Error);
                }
            }

            // 5. Remove from tracking
            _activeTestAgents.TryRemove(projectId, out _);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error stopping test agent for project {ProjectId}", projectId);
        }
    }

    public TestAgentStatus? GetTestAgentStatus(string projectId)
    {
        return _activeTestAgents.GetValueOrDefault(projectId);
    }

    public async Task<SessionVerificationResult> VerifySessionVisibilityAsync(string projectId, CancellationToken ct = default)
    {
        var status = GetTestAgentStatus(projectId);
        if (status == null)
        {
            return new SessionVerificationResult
            {
                SessionFound = false,
                Error = "No active test agent for this project"
            };
        }

        try
        {
            // Get agent instance from harness
            var harness = harnessFactory.GetHarness(TestHarnessType);
            var entityId = $"{TestEntityPrefix}{projectId}";
            var agent = harness.GetAgentForEntity(entityId);

            if (agent == null)
            {
                return new SessionVerificationResult
                {
                    SessionFound = false,
                    Error = "Agent not found in harness"
                };
            }

            return new SessionVerificationResult
            {
                SessionFound = !string.IsNullOrEmpty(agent.ActiveSessionId),
                TotalSessions = 1,
                SessionId = agent.ActiveSessionId,
                SessionTitle = "Test Agent Session",
                AllSessionIds = agent.ActiveSessionId != null ? [agent.ActiveSessionId] : []
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to verify session visibility for project {ProjectId}", projectId);
            return new SessionVerificationResult
            {
                SessionFound = false,
                Error = ex.Message
            };
        }
    }
}
