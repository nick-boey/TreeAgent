using Homespun.Features.Commands;
using Homespun.Features.Git;
using Moq;

namespace Homespun.Tests.Features.Git;

[TestFixture]
public class GitWorktreeServiceTests
{
    private Mock<ICommandRunner> _mockRunner = null!;
    private GitWorktreeService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _mockRunner = new Mock<ICommandRunner>();
        _service = new GitWorktreeService(_mockRunner.Object);
    }

    [Test]
    public async Task CreateWorktree_Success_ReturnsPath()
    {
        // Arrange
        var repoPath = "/repo";
        var branchName = "feature/test";
        var expectedWorktreePath = "/repo/.worktrees/feature-test";

        _mockRunner.Setup(r => r.RunAsync("git", It.IsAny<string>(), repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        // Act
        var result = await _service.CreateWorktreeAsync(repoPath, branchName);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("feature-test"));
    }

    [Test]
    public async Task CreateWorktree_GitError_ReturnsNull()
    {
        // Arrange
        var repoPath = "/repo";
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
        var repoPath = "/repo";
        var worktreePath = "/repo/.worktrees/feature-test";

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
        var repoPath = "/repo";
        var worktreePath = "/repo/.worktrees/feature-test";

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
        var repoPath = "/repo";
        var gitOutput = "/repo\n/repo/.worktrees/feature-1\n/repo/.worktrees/feature-2\n";

        _mockRunner.Setup(r => r.RunAsync("git", "worktree list --porcelain", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "worktree /repo\nworktree /repo/.worktrees/feature-1\nworktree /repo/.worktrees/feature-2" });

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
        var repoPath = "/repo";

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
        var repoPath = "/repo";

        _mockRunner.Setup(r => r.RunAsync("git", "worktree prune", repoPath))
            .ReturnsAsync(new CommandResult { Success = true });

        // Act
        await _service.PruneWorktreesAsync(repoPath);

        // Assert
        _mockRunner.Verify(r => r.RunAsync("git", "worktree prune", repoPath), Times.Once);
    }

    [Test]
    public void SanitizeBranchName_RemovesSlashes()
    {
        // Act
        var result = GitWorktreeService.SanitizeBranchName("feature/new-thing");

        // Assert
        Assert.That(result, Is.EqualTo("feature-new-thing"));
    }

    [Test]
    public void SanitizeBranchName_RemovesSpecialCharacters()
    {
        // Act
        var result = GitWorktreeService.SanitizeBranchName("feature/test@branch#1");

        // Assert
        Assert.That(result, Is.EqualTo("feature-test-branch-1"));
    }

    [Test]
    public async Task CreateWorktree_WithNewBranch_CreatesBranchFirst()
    {
        // Arrange
        var repoPath = "/repo";
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
