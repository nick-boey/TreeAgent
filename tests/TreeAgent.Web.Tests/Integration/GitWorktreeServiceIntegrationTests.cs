using TreeAgent.Web.Features.Git;
using TreeAgent.Web.Tests.Integration.Fixtures;

namespace TreeAgent.Web.Tests.Integration;

/// <summary>
/// Integration tests for GitWorktreeService that test against real git repositories.
/// These tests require git to be installed and available on the PATH.
/// </summary>
[TestFixture]
[Category("Integration")]
public class GitWorktreeServiceIntegrationTests
{
    private TempGitRepositoryFixture _fixture = null!;
    private GitWorktreeService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new TempGitRepositoryFixture();
        _service = new GitWorktreeService(); // Uses real CommandRunner
    }

    [TearDown]
    public void TearDown()
    {
        _fixture.Dispose();
    }

    /// <summary>
    /// Normalizes a path for comparison (git returns forward slashes on Windows).
    /// </summary>
    private static string NormalizePath(string path) => path.Replace('\\', '/').TrimEnd('/');

    [Test]
    public async Task CreateWorktree_WithExistingBranch_CreatesWorktreeSuccessfully()
    {
        // Arrange
        var branchName = "feature/test-worktree";
        _fixture.CreateBranch(branchName);

        // Act
        var worktreePath = await _service.CreateWorktreeAsync(_fixture.RepositoryPath, branchName);

        // Assert
        Assert.That(worktreePath, Is.Not.Null);
        Assert.That(Directory.Exists(worktreePath), Is.True);
        Assert.That(File.Exists(Path.Combine(worktreePath!, "README.md")), Is.True);
    }

    [Test]
    public async Task CreateWorktree_WithNewBranch_CreatesBranchAndWorktree()
    {
        // Arrange
        var branchName = "feature/new-branch";

        // Act
        var worktreePath = await _service.CreateWorktreeAsync(
            _fixture.RepositoryPath,
            branchName,
            createBranch: true);

        // Assert
        Assert.That(worktreePath, Is.Not.Null);
        Assert.That(Directory.Exists(worktreePath), Is.True);

        // Verify the branch was created
        var branches = _fixture.RunGit("branch --list");
        Assert.That(branches, Does.Contain(branchName));
    }

    [Test]
    public async Task CreateWorktree_WithBaseBranch_CreatesBranchFromSpecifiedBase()
    {
        // Arrange
        var baseBranch = "develop";
        var featureBranch = "feature/from-develop";

        // Create develop branch with additional content
        _fixture.CreateBranch(baseBranch, checkout: true);
        _fixture.CreateFileAndCommit("develop.txt", "Develop content", "Add develop file");
        _fixture.RunGit("checkout -"); // Go back to main/master

        // Act
        var worktreePath = await _service.CreateWorktreeAsync(
            _fixture.RepositoryPath,
            featureBranch,
            createBranch: true,
            baseBranch: baseBranch);

        // Assert
        Assert.That(worktreePath, Is.Not.Null);
        Assert.That(Directory.Exists(worktreePath), Is.True);
        // The worktree should have the file from develop branch
        Assert.That(File.Exists(Path.Combine(worktreePath!, "develop.txt")), Is.True);
    }

    [Test]
    public async Task CreateWorktree_NonExistentBranch_ReturnsNull()
    {
        // Arrange
        var branchName = "non-existent-branch";

        // Act
        var worktreePath = await _service.CreateWorktreeAsync(
            _fixture.RepositoryPath,
            branchName,
            createBranch: false);

        // Assert
        Assert.That(worktreePath, Is.Null);
    }

    [Test]
    public async Task ListWorktrees_ReturnsMainWorktree()
    {
        // Act
        var worktrees = await _service.ListWorktreesAsync(_fixture.RepositoryPath);

        // Assert
        Assert.That(worktrees, Is.Not.Null);
        Assert.That(worktrees, Has.Count.EqualTo(1));
        Assert.That(NormalizePath(worktrees[0].Path), Is.EqualTo(NormalizePath(_fixture.RepositoryPath)));
    }

    [Test]
    public async Task ListWorktrees_AfterCreatingWorktree_ReturnsMultipleWorktrees()
    {
        // Arrange
        var branchName = "feature/list-test";
        _fixture.CreateBranch(branchName);
        await _service.CreateWorktreeAsync(_fixture.RepositoryPath, branchName);

        // Act
        var worktrees = await _service.ListWorktreesAsync(_fixture.RepositoryPath);

        // Assert
        Assert.That(worktrees, Is.Not.Null);
        Assert.That(worktrees, Has.Count.EqualTo(2));
        Assert.That(worktrees, Has.Some.Matches<WorktreeInfo>(w => NormalizePath(w.Path) == NormalizePath(_fixture.RepositoryPath)));
        Assert.That(worktrees, Has.Some.Matches<WorktreeInfo>(w => w.Branch?.EndsWith(branchName) == true));
    }

    [Test]
    public async Task ListWorktrees_ReturnsCorrectBranchInfo()
    {
        // Arrange
        var branchName = "feature/branch-info-test";
        _fixture.CreateBranch(branchName);
        var worktreePath = await _service.CreateWorktreeAsync(_fixture.RepositoryPath, branchName);
        Assert.That(worktreePath, Is.Not.Null);

        // Act
        var worktrees = await _service.ListWorktreesAsync(_fixture.RepositoryPath);

        // Assert
        var worktree = worktrees.FirstOrDefault(w => NormalizePath(w.Path) == NormalizePath(worktreePath!));
        Assert.That(worktree, Is.Not.Null);
        Assert.That(worktree!.Branch, Does.EndWith(branchName));
        Assert.That(worktree.HeadCommit, Is.Not.Null);
        Assert.That(worktree.IsDetached, Is.False);
    }

    [Test]
    public async Task RemoveWorktree_ExistingWorktree_RemovesSuccessfully()
    {
        // Arrange
        var branchName = "feature/remove-test";
        _fixture.CreateBranch(branchName);
        var worktreePath = await _service.CreateWorktreeAsync(_fixture.RepositoryPath, branchName);
        Assert.That(worktreePath, Is.Not.Null);
        Assert.That(Directory.Exists(worktreePath), Is.True);

        // Act
        var result = await _service.RemoveWorktreeAsync(_fixture.RepositoryPath, worktreePath!);

        // Assert
        Assert.That(result, Is.True);
        Assert.That(Directory.Exists(worktreePath), Is.False);
    }

    [Test]
    public async Task RemoveWorktree_NonExistentWorktree_ReturnsFalse()
    {
        // Arrange
        var fakeWorktreePath = Path.Combine(_fixture.RepositoryPath, ".worktrees", "non-existent");

        // Act
        var result = await _service.RemoveWorktreeAsync(_fixture.RepositoryPath, fakeWorktreePath);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task WorktreeExists_ExistingWorktree_ReturnsTrue()
    {
        // Arrange
        var branchName = "feature/exists-test";
        _fixture.CreateBranch(branchName);
        await _service.CreateWorktreeAsync(_fixture.RepositoryPath, branchName);

        // Act
        var exists = await _service.WorktreeExistsAsync(_fixture.RepositoryPath, branchName);

        // Assert
        Assert.That(exists, Is.True);
    }

    [Test]
    public async Task WorktreeExists_NonExistentWorktree_ReturnsFalse()
    {
        // Act
        var exists = await _service.WorktreeExistsAsync(_fixture.RepositoryPath, "non-existent-branch");

        // Assert
        Assert.That(exists, Is.False);
    }

    [Test]
    public async Task PruneWorktrees_RemovesStaleWorktreeReferences()
    {
        // Arrange
        var branchName = "feature/prune-test";
        _fixture.CreateBranch(branchName);
        var worktreePath = await _service.CreateWorktreeAsync(_fixture.RepositoryPath, branchName);
        Assert.That(worktreePath, Is.Not.Null);

        // Manually delete the worktree directory (simulating stale worktree)
        if (Directory.Exists(worktreePath))
        {
            Directory.Delete(worktreePath!, recursive: true);
        }

        // Act - should not throw
        await _service.PruneWorktreesAsync(_fixture.RepositoryPath);

        // Assert - worktree should be pruned from the list
        var worktrees = await _service.ListWorktreesAsync(_fixture.RepositoryPath);
        Assert.That(worktrees, Has.None.Matches<WorktreeInfo>(w => NormalizePath(w.Path) == NormalizePath(worktreePath!)));
    }

    [Test]
    public async Task CreateWorktree_WithModifiedFiles_WorktreeHasCleanState()
    {
        // Arrange
        var branchName = "feature/clean-state-test";
        _fixture.CreateBranch(branchName);

        // Modify a file in the main worktree (uncommitted change)
        var readmePath = Path.Combine(_fixture.RepositoryPath, "README.md");
        File.AppendAllText(readmePath, "\n\nModified content.");

        // Act
        var worktreePath = await _service.CreateWorktreeAsync(_fixture.RepositoryPath, branchName);

        // Assert
        Assert.That(worktreePath, Is.Not.Null);

        // The new worktree should have the clean version from the branch
        var worktreeReadme = File.ReadAllText(Path.Combine(worktreePath!, "README.md"));
        Assert.That(worktreeReadme, Does.Not.Contain("Modified content."));
    }

    [Test]
    public async Task CreateWorktree_BranchNameWithSpecialCharacters_SanitizesPath()
    {
        // Arrange
        var branchName = "feature/special@chars#test";
        _fixture.RunGit($"branch \"{branchName}\"");

        // Act
        var worktreePath = await _service.CreateWorktreeAsync(_fixture.RepositoryPath, branchName);

        // Assert
        Assert.That(worktreePath, Is.Not.Null);
        Assert.That(worktreePath, Does.Not.Contain("@"));
        Assert.That(worktreePath, Does.Not.Contain("#"));
        Assert.That(Directory.Exists(worktreePath), Is.True);
    }
}
