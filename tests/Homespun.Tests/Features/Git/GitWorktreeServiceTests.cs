using Homespun.Features.Commands;
using Homespun.Features.Git;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.Git;

[TestFixture]
public class GitWorktreeServiceTests
{
    private Mock<ICommandRunner> _mockRunner = null!;
    private GitWorktreeService _service = null!;
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _mockRunner = new Mock<ICommandRunner>();
        _service = new GitWorktreeService(_mockRunner.Object, Mock.Of<ILogger<GitWorktreeService>>());
        _tempDir = Path.Combine(Path.GetTempPath(), "homespun-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Test]
    public async Task CreateWorktree_Success_ReturnsPath()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");
        Directory.CreateDirectory(repoPath);
        var branchName = "feature/test";

        _mockRunner.Setup(r => r.RunAsync("git", It.IsAny<string>(), repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        // Act
        var result = await _service.CreateWorktreeAsync(repoPath, branchName);

        // Assert
        Assert.That(result, Is.Not.Null);
        // Path is normalized to platform-native separators, so check for the folder structure
        // On Windows: feature\test, on Unix: feature/test
        Assert.That(result, Does.Contain("feature").And.Contain("test"));
    }

    [Test]
    public async Task CreateWorktree_GitError_ReturnsNull()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");
        Directory.CreateDirectory(repoPath);
        var branchName = "feature/test";

        _mockRunner.Setup(r => r.RunAsync("git", It.IsAny<string>(), repoPath))
            .ReturnsAsync(new CommandResult { Success = false, Error = "fatal: 'feature/test' is already checked out" });

        // Act
        var result = await _service.CreateWorktreeAsync(repoPath, branchName);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task RemoveWorktree_Success_ReturnsTrue()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");
        var worktreePath = Path.Combine(repoPath, ".worktrees", "feature-test");

        _mockRunner.Setup(r => r.RunAsync("git", $"worktree remove \"{worktreePath}\" --force", repoPath))
            .ReturnsAsync(new CommandResult { Success = true });

        // Act
        var result = await _service.RemoveWorktreeAsync(repoPath, worktreePath);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task RemoveWorktree_GitError_ReturnsFalse()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");
        var worktreePath = Path.Combine(repoPath, ".worktrees", "feature-test");

        _mockRunner.Setup(r => r.RunAsync("git", It.IsAny<string>(), repoPath))
            .ReturnsAsync(new CommandResult { Success = false });

        // Act
        var result = await _service.RemoveWorktreeAsync(repoPath, worktreePath);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task ListWorktrees_Success_ReturnsWorktrees()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");
        var worktree1 = Path.Combine(repoPath, ".worktrees", "feature-1");
        var worktree2 = Path.Combine(repoPath, ".worktrees", "feature-2");

        _mockRunner.Setup(r => r.RunAsync("git", "worktree list --porcelain", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = $"worktree {repoPath}\nworktree {worktree1}\nworktree {worktree2}" });

        // Act
        var result = await _service.ListWorktreesAsync(repoPath);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task ListWorktrees_GitError_ReturnsEmptyList()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");

        _mockRunner.Setup(r => r.RunAsync("git", It.IsAny<string>(), repoPath))
            .ReturnsAsync(new CommandResult { Success = false });

        // Act
        var result = await _service.ListWorktreesAsync(repoPath);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task PruneWorktrees_CallsGitPrune()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");

        _mockRunner.Setup(r => r.RunAsync("git", "worktree prune", repoPath))
            .ReturnsAsync(new CommandResult { Success = true });

        // Act
        await _service.PruneWorktreesAsync(repoPath);

        // Assert
        _mockRunner.Verify(r => r.RunAsync("git", "worktree prune", repoPath), Times.Once);
    }

    [Test]
    public void SanitizeBranchName_PreservesSlashes()
    {
        // Act
        var result = GitWorktreeService.SanitizeBranchName("feature/new-thing");

        // Assert - slashes are preserved for folder structure
        Assert.That(result, Is.EqualTo("feature/new-thing"));
    }

    [Test]
    public void SanitizeBranchName_RemovesSpecialCharactersButPreservesSlashes()
    {
        // Act
        var result = GitWorktreeService.SanitizeBranchName("feature/test@branch#1");

        // Assert - slashes preserved, special chars replaced with dashes
        Assert.That(result, Is.EqualTo("feature/test-branch-1"));
    }

    [Test]
    public void SanitizeBranchName_NormalizesBackslashesToForwardSlashes()
    {
        // Act
        var result = GitWorktreeService.SanitizeBranchName("app\\feature\\test");

        // Assert - backslashes converted to forward slashes
        Assert.That(result, Is.EqualTo("app/feature/test"));
    }

    [Test]
    public void SanitizeBranchName_TrimsSlashesFromEnds()
    {
        // Act
        var result = GitWorktreeService.SanitizeBranchName("/feature/test/");

        // Assert - leading/trailing slashes removed
        Assert.That(result, Is.EqualTo("feature/test"));
    }

    [Test]
    public async Task CreateWorktree_WithNewBranch_CreatesBranchFirst()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");
        Directory.CreateDirectory(repoPath);
        var branchName = "feature/new-branch";

        _mockRunner.SetupSequence(r => r.RunAsync("git", It.IsAny<string>(), repoPath))
            .ReturnsAsync(new CommandResult { Success = true }) // branch check
            .ReturnsAsync(new CommandResult { Success = true }) // create branch
            .ReturnsAsync(new CommandResult { Success = true }); // create worktree

        // Act
        var result = await _service.CreateWorktreeAsync(repoPath, branchName, createBranch: true);

        // Assert
        Assert.That(result, Is.Not.Null);
    }
}
