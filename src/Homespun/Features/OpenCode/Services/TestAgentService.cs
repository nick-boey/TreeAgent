using System.Collections.Concurrent;
using Homespun.Features.Commands;
using Homespun.Features.Git;
using Homespun.Features.OpenCode.Models;
using Homespun.Features.PullRequests.Data;

namespace Homespun.Features.OpenCode.Services;

/// <summary>
/// Service for running test agents to debug OpenCode integration.
/// Only available in DEBUG builds.
/// </summary>
public class TestAgentService(
    IOpenCodeServerManager serverManager,
    IOpenCodeClient client,
    IGitWorktreeService worktreeService,
    IDataStore dataStore,
    IOpenCodeConfigGenerator configGenerator,
    ICommandRunner commandRunner,
    ILogger<TestAgentService> logger) : ITestAgentService
{
    private const string TestBranchName = "hsp/test";
    private const string TestFileName = "test.txt";
    private const string TestEntityPrefix = "test-agent-";
    
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
            
            // 2. Create or get worktree path
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
            
            // 3. Delete test.txt if it exists
            var testFilePath = Path.Combine(worktreePath, TestFileName);
            if (File.Exists(testFilePath))
            {
                File.Delete(testFilePath);
                logger.LogInformation("Deleted existing {TestFile}", testFilePath);
            }
            
            // 4. Generate opencode.json config
            var config = configGenerator.CreateDefaultConfig(project.DefaultModel);
            await configGenerator.GenerateConfigAsync(worktreePath, config, ct);
            
            // 5. Start OpenCode server
            var entityId = $"{TestEntityPrefix}{projectId}";
            var server = await serverManager.StartServerAsync(entityId, worktreePath, continueSession: false, ct);
            
            logger.LogInformation("Test OpenCode server started at {BaseUrl}", server.BaseUrl);
            
            // 6. Create session
            var session = await client.CreateSessionAsync(server.BaseUrl, "Test Agent Session", ct);
            logger.LogInformation("Test session created: {SessionId}", session.Id);
            
            // 7. Send test prompt (fire and forget)
            var prompt = PromptRequest.FromText(
                $"Create a file called '{TestFileName}' in the current directory with the content 'Hello from OpenCode test agent - created at {DateTime.UtcNow:O}'");
            
            await client.SendPromptAsyncNoWait(server.BaseUrl, session.Id, prompt, ct);
            logger.LogInformation("Test prompt sent to session {SessionId}", session.Id);
            
            // 8. Track status
            var status = new TestAgentStatus
            {
                ProjectId = projectId,
                ServerUrl = server.BaseUrl,
                SessionId = session.Id,
                WorktreePath = worktreePath,
                StartedAt = DateTime.UtcNow
            };
            _activeTestAgents[projectId] = status;
            
            return TestAgentResult.Ok(server.BaseUrl, session.Id, worktreePath);
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
            
            // 1. Stop the server
            await serverManager.StopServerAsync(entityId, ct);
            logger.LogInformation("Stopped test agent server for project {ProjectId}", projectId);
            
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
            // Query the OpenCode server's session list
            var sessions = await client.ListSessionsAsync(status.ServerUrl, ct);
            var allIds = sessions.Select(s => s.Id).ToList();
            var found = sessions.FirstOrDefault(s => s.Id == status.SessionId);
            
            return new SessionVerificationResult
            {
                SessionFound = found != null,
                TotalSessions = sessions.Count,
                SessionId = found?.Id,
                SessionTitle = found?.Title,
                AllSessionIds = allIds
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
