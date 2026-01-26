using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.PullRequests;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Homespun.Tests.Features.ClaudeCode;

[TestFixture]
public class RebaseAgentServiceTests
{
    private RebaseAgentService _service = null!;
    private Mock<IClaudeSessionService> _sessionServiceMock = null!;
    private Mock<ILogger<RebaseAgentService>> _loggerMock = null!;

    [SetUp]
    public void SetUp()
    {
        _sessionServiceMock = new Mock<IClaudeSessionService>();
        _loggerMock = new Mock<ILogger<RebaseAgentService>>();
        _service = new RebaseAgentService(_sessionServiceMock.Object, _loggerMock.Object);
    }

    #region GenerateRebaseSystemPrompt Tests

    [Test]
    public void GenerateRebaseSystemPrompt_IncludesBranchName()
    {
        // Arrange
        var branchName = "issues/feature/my-feature+ABC123";
        var defaultBranch = "main";

        // Act
        var prompt = _service.GenerateRebaseSystemPrompt(branchName, defaultBranch);

        // Assert
        Assert.That(prompt, Does.Contain(branchName));
    }

    [Test]
    public void GenerateRebaseSystemPrompt_IncludesDefaultBranch()
    {
        // Arrange
        var branchName = "issues/feature/my-feature+ABC123";
        var defaultBranch = "main";

        // Act
        var prompt = _service.GenerateRebaseSystemPrompt(branchName, defaultBranch);

        // Assert
        Assert.That(prompt, Does.Contain(defaultBranch));
    }

    [Test]
    public void GenerateRebaseSystemPrompt_IncludesRebaseWorkflowInstructions()
    {
        // Arrange
        var branchName = "feature/test";
        var defaultBranch = "main";

        // Act
        var prompt = _service.GenerateRebaseSystemPrompt(branchName, defaultBranch);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("git fetch"));
            Assert.That(prompt, Does.Contain("git rebase"));
            Assert.That(prompt, Does.Contain("--force-with-lease"));
        });
    }

    [Test]
    public void GenerateRebaseSystemPrompt_IncludesConflictResolutionGuidance()
    {
        // Arrange
        var branchName = "feature/test";
        var defaultBranch = "main";

        // Act
        var prompt = _service.GenerateRebaseSystemPrompt(branchName, defaultBranch);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("conflict"));
            Assert.That(prompt, Does.Contain("git add"));
            Assert.That(prompt, Does.Contain("git rebase --continue"));
        });
    }

    [Test]
    public void GenerateRebaseSystemPrompt_IncludesTestingRequirements()
    {
        // Arrange
        var branchName = "feature/test";
        var defaultBranch = "main";

        // Act
        var prompt = _service.GenerateRebaseSystemPrompt(branchName, defaultBranch);

        // Assert
        Assert.That(prompt, Does.Contain("test"));
    }

    [Test]
    public void GenerateRebaseSystemPrompt_WorksWithDifferentDefaultBranches()
    {
        // Arrange
        var branchName = "feature/test";

        // Act
        var mainPrompt = _service.GenerateRebaseSystemPrompt(branchName, "main");
        var masterPrompt = _service.GenerateRebaseSystemPrompt(branchName, "master");
        var developPrompt = _service.GenerateRebaseSystemPrompt(branchName, "develop");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(mainPrompt, Does.Contain("origin/main"));
            Assert.That(masterPrompt, Does.Contain("origin/master"));
            Assert.That(developPrompt, Does.Contain("origin/develop"));
        });
    }

    #endregion

    #region GenerateRebaseInitialMessage Tests

    [Test]
    public void GenerateRebaseInitialMessage_IncludesBranchName()
    {
        // Arrange
        var branchName = "issues/feature/my-feature+ABC123";
        var defaultBranch = "main";

        // Act
        var message = _service.GenerateRebaseInitialMessage(branchName, defaultBranch);

        // Assert
        Assert.That(message, Does.Contain(branchName));
    }

    [Test]
    public void GenerateRebaseInitialMessage_IncludesDefaultBranch()
    {
        // Arrange
        var branchName = "feature/test";
        var defaultBranch = "main";

        // Act
        var message = _service.GenerateRebaseInitialMessage(branchName, defaultBranch);

        // Assert
        Assert.That(message, Does.Contain(defaultBranch));
    }

    [Test]
    public void GenerateRebaseInitialMessage_HandlesEmptyPRList()
    {
        // Arrange
        var branchName = "feature/test";
        var defaultBranch = "main";
        var emptyPRs = new List<PullRequestInfo>();

        // Act
        var message = _service.GenerateRebaseInitialMessage(branchName, defaultBranch, emptyPRs);

        // Assert
        Assert.That(message, Does.Contain(branchName));
        Assert.That(message, Does.Contain(defaultBranch));
    }

    [Test]
    public void GenerateRebaseInitialMessage_HandlesNullPRList()
    {
        // Arrange
        var branchName = "feature/test";
        var defaultBranch = "main";

        // Act
        var message = _service.GenerateRebaseInitialMessage(branchName, defaultBranch, null);

        // Assert
        Assert.That(message, Does.Contain(branchName));
        Assert.That(message, Does.Contain(defaultBranch));
    }

    [Test]
    public void GenerateRebaseInitialMessage_IncludesRecentMergedPRs()
    {
        // Arrange
        var branchName = "feature/test";
        var defaultBranch = "main";
        var recentPRs = new List<PullRequestInfo>
        {
            new()
            {
                Number = 123,
                Title = "Add authentication module",
                Body = "This PR adds OAuth2 authentication",
                Status = PullRequestStatus.Merged
            },
            new()
            {
                Number = 124,
                Title = "Fix database connection issue",
                Body = "Resolves connection pooling problems",
                Status = PullRequestStatus.Merged
            }
        };

        // Act
        var message = _service.GenerateRebaseInitialMessage(branchName, defaultBranch, recentPRs);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(message, Does.Contain("#123"));
            Assert.That(message, Does.Contain("Add authentication module"));
            Assert.That(message, Does.Contain("#124"));
            Assert.That(message, Does.Contain("Fix database connection issue"));
        });
    }

    [Test]
    public void GenerateRebaseInitialMessage_TruncatesLongPRBodies()
    {
        // Arrange
        var branchName = "feature/test";
        var defaultBranch = "main";
        var longBody = new string('x', 500); // 500 character body
        var recentPRs = new List<PullRequestInfo>
        {
            new()
            {
                Number = 123,
                Title = "Test PR",
                Body = longBody,
                Status = PullRequestStatus.Merged
            }
        };

        // Act
        var message = _service.GenerateRebaseInitialMessage(branchName, defaultBranch, recentPRs);

        // Assert
        // The body should be truncated (not contain all 500 x's)
        var bodyOccurrences = message.Split(new[] { longBody }, StringSplitOptions.None).Length - 1;
        Assert.That(bodyOccurrences, Is.EqualTo(0), "Full body should not appear - it should be truncated");
        Assert.That(message, Does.Contain("...")); // Should have truncation indicator
    }

    #endregion

    #region StartRebaseAgentAsync Tests

    [Test]
    public async Task StartRebaseAgentAsync_CreatesSessionWithBuildMode()
    {
        // Arrange
        var projectId = "project-123";
        var worktreePath = "/path/to/worktree";
        var branchName = "feature/test";
        var model = "sonnet";
        var defaultBranch = "main";

        var expectedSession = new ClaudeSession
        {
            Id = "session-123",
            EntityId = $"rebase-{branchName}",
            ProjectId = projectId,
            WorkingDirectory = worktreePath,
            Mode = SessionMode.Build,
            Model = model,
            Status = ClaudeSessionStatus.WaitingForInput,
            CreatedAt = DateTime.UtcNow
        };

        _sessionServiceMock
            .Setup(s => s.StartSessionAsync(
                It.IsAny<string>(),
                projectId,
                worktreePath,
                SessionMode.Build,
                model,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedSession);

        // Act
        var session = await _service.StartRebaseAgentAsync(projectId, worktreePath, branchName, defaultBranch, model);

        // Assert
        Assert.That(session.Mode, Is.EqualTo(SessionMode.Build));
        _sessionServiceMock.Verify(s => s.StartSessionAsync(
            It.IsAny<string>(),
            projectId,
            worktreePath,
            SessionMode.Build,
            model,
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task StartRebaseAgentAsync_SetsCorrectEntityId()
    {
        // Arrange
        var projectId = "project-123";
        var worktreePath = "/path/to/worktree";
        var branchName = "feature/test";
        var model = "sonnet";
        var defaultBranch = "main";

        string? capturedEntityId = null;

        _sessionServiceMock
            .Setup(s => s.StartSessionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<SessionMode>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, string, SessionMode, string, string?, CancellationToken>(
                (entityId, _, _, _, _, _, _) => capturedEntityId = entityId)
            .ReturnsAsync(new ClaudeSession
            {
                Id = "session-123",
                EntityId = "rebase-feature/test",
                ProjectId = projectId,
                WorkingDirectory = worktreePath,
                Mode = SessionMode.Build,
                Model = model,
                Status = ClaudeSessionStatus.WaitingForInput,
                CreatedAt = DateTime.UtcNow
            });

        // Act
        await _service.StartRebaseAgentAsync(projectId, worktreePath, branchName, defaultBranch, model);

        // Assert
        Assert.That(capturedEntityId, Is.EqualTo($"rebase-{branchName}"));
    }

    [Test]
    public async Task StartRebaseAgentAsync_IncludesSystemPrompt()
    {
        // Arrange
        var projectId = "project-123";
        var worktreePath = "/path/to/worktree";
        var branchName = "feature/test";
        var model = "sonnet";
        var defaultBranch = "main";

        string? capturedSystemPrompt = null;

        _sessionServiceMock
            .Setup(s => s.StartSessionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<SessionMode>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, string, SessionMode, string, string?, CancellationToken>(
                (_, _, _, _, _, systemPrompt, _) => capturedSystemPrompt = systemPrompt)
            .ReturnsAsync(new ClaudeSession
            {
                Id = "session-123",
                EntityId = "rebase-feature/test",
                ProjectId = projectId,
                WorkingDirectory = worktreePath,
                Mode = SessionMode.Build,
                Model = model,
                Status = ClaudeSessionStatus.WaitingForInput,
                CreatedAt = DateTime.UtcNow
            });

        // Act
        await _service.StartRebaseAgentAsync(projectId, worktreePath, branchName, defaultBranch, model);

        // Assert
        Assert.That(capturedSystemPrompt, Is.Not.Null.And.Not.Empty);
        Assert.That(capturedSystemPrompt, Does.Contain(branchName));
        Assert.That(capturedSystemPrompt, Does.Contain(defaultBranch));
    }

    [Test]
    public async Task StartRebaseAgentAsync_SendsInitialMessage()
    {
        // Arrange
        var projectId = "project-123";
        var worktreePath = "/path/to/worktree";
        var branchName = "feature/test";
        var model = "sonnet";
        var defaultBranch = "main";
        var sessionId = "session-123";

        _sessionServiceMock
            .Setup(s => s.StartSessionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<SessionMode>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClaudeSession
            {
                Id = sessionId,
                EntityId = "rebase-feature/test",
                ProjectId = projectId,
                WorkingDirectory = worktreePath,
                Mode = SessionMode.Build,
                Model = model,
                Status = ClaudeSessionStatus.WaitingForInput,
                CreatedAt = DateTime.UtcNow
            });

        // Act
        await _service.StartRebaseAgentAsync(projectId, worktreePath, branchName, defaultBranch, model);

        // Assert
        _sessionServiceMock.Verify(s => s.SendMessageAsync(
            sessionId,
            It.Is<string>(msg => msg.Contains(branchName) && msg.Contains(defaultBranch)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task StartRebaseAgentAsync_WithRecentPRs_IncludesThemInMessage()
    {
        // Arrange
        var projectId = "project-123";
        var worktreePath = "/path/to/worktree";
        var branchName = "feature/test";
        var model = "sonnet";
        var defaultBranch = "main";
        var sessionId = "session-123";
        var recentPRs = new List<PullRequestInfo>
        {
            new()
            {
                Number = 100,
                Title = "Important change",
                Body = "Description of important change",
                Status = PullRequestStatus.Merged
            }
        };

        string? capturedMessage = null;

        _sessionServiceMock
            .Setup(s => s.StartSessionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<SessionMode>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClaudeSession
            {
                Id = sessionId,
                EntityId = "rebase-feature/test",
                ProjectId = projectId,
                WorkingDirectory = worktreePath,
                Mode = SessionMode.Build,
                Model = model,
                Status = ClaudeSessionStatus.WaitingForInput,
                CreatedAt = DateTime.UtcNow
            });

        _sessionServiceMock
            .Setup(s => s.SendMessageAsync(
                sessionId,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, msg, _) => capturedMessage = msg)
            .Returns(Task.CompletedTask);

        // Act
        await _service.StartRebaseAgentAsync(projectId, worktreePath, branchName, defaultBranch, model, recentPRs);

        // Assert
        Assert.That(capturedMessage, Does.Contain("#100"));
        Assert.That(capturedMessage, Does.Contain("Important change"));
    }

    #endregion
}
