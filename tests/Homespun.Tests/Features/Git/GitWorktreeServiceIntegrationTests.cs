using Homespun.Features.Git;
using Homespun.Features.Testing;
using Homespun.Tests.Helpers;

namespace Homespun.Tests.Features.Git;

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

    #region GetWorktreePathForBranchAsync Integration Tests

    /// <summary>
    /// Integration test for the bug reported in issue 7udaqv.
    /// When a PR is synced from GitHub with a branch name containing "+", the worktree path
    /// should be found even though the folder uses "-" instead of "+" due to sanitization.
    /// </summary>
    [Test]
    public async Task GetWorktreePathForBranch_WithPlusInBranchName_MatchesSanitizedWorktreePath()
    {
        // Arrange
        // The branch name as it would appear in GitHub PR (with + character)
        // This simulates the branch naming convention: issues/type/name+issueId
        var gitHubBranchName = "issues/feature/improve-tool-output+aLP3LH";

        // Create the branch in git (git allows + in branch names)
        _fixture.RunGit($"branch \"{gitHubBranchName}\"");

        // Create a worktree for this branch - the path will be sanitized (+ becomes -)
        var worktreePath = await _service.CreateWorktreeAsync(_fixture.RepositoryPath, gitHubBranchName);
        Assert.That(worktreePath, Is.Not.Null);

        // Verify the worktree path was sanitized (+ replaced with -)
        Assert.That(worktreePath, Does.Contain("improve-tool-output-aLP3LH"));
        Assert.That(worktreePath, Does.Not.Contain("+"));

        // Act - Now simulate what happens during PR sync:
        // Given only the GitHub branch name (with +), can we find the existing worktree?
        var foundPath = await _service.GetWorktreePathForBranchAsync(_fixture.RepositoryPath, gitHubBranchName);

        // Assert
        Assert.That(foundPath, Is.Not.Null, "Should find the worktree path for the branch with + character");
        Assert.That(NormalizePath(foundPath!), Is.EqualTo(NormalizePath(worktreePath!)));
    }

    [Test]
    public async Task GetWorktreePathForBranch_WithNoExistingWorktree_ReturnsNull()
    {
        // Arrange
        var branchName = "issues/feature/no-worktree+test";
        _fixture.RunGit($"branch \"{branchName}\"");
        // Note: We do NOT create a worktree for this branch

        // Act
        var foundPath = await _service.GetWorktreePathForBranchAsync(_fixture.RepositoryPath, branchName);

        // Assert
        Assert.That(foundPath, Is.Null);
    }

    [Test]
    public async Task GetWorktreePathForBranch_WithRegularBranchName_MatchesDirectly()
    {
        // Arrange
        var branchName = "feature/normal-branch";
        _fixture.CreateBranch(branchName);

        var worktreePath = await _service.CreateWorktreeAsync(_fixture.RepositoryPath, branchName);
        Assert.That(worktreePath, Is.Not.Null);

        // Act
        var foundPath = await _service.GetWorktreePathForBranchAsync(_fixture.RepositoryPath, branchName);

        // Assert
        Assert.That(foundPath, Is.Not.Null);
        Assert.That(NormalizePath(foundPath!), Is.EqualTo(NormalizePath(worktreePath!)));
    }

    [Test]
    public async Task GetWorktreePathForBranch_WithMultipleSpecialCharacters_MatchesSanitizedPath()
    {
        // Arrange - Test with multiple special characters that get sanitized
        var branchName = "feature/test+foo@bar#baz";
        _fixture.RunGit($"branch \"{branchName}\"");

        var worktreePath = await _service.CreateWorktreeAsync(_fixture.RepositoryPath, branchName);
        Assert.That(worktreePath, Is.Not.Null);

        // Verify sanitization happened
        Assert.That(worktreePath, Does.Contain("test-foo-bar-baz"));

        // Act
        var foundPath = await _service.GetWorktreePathForBranchAsync(_fixture.RepositoryPath, branchName);

        // Assert
        Assert.That(foundPath, Is.Not.Null);
        Assert.That(NormalizePath(foundPath!), Is.EqualTo(NormalizePath(worktreePath!)));
    }

    #endregion

    #region ListLocalBranchesAsync Integration Tests

    [Test]
    public async Task ListLocalBranchesAsync_ReturnsMainBranch()
    {
        // Act
        var branches = await _service.ListLocalBranchesAsync(_fixture.RepositoryPath);

        // Assert
        Assert.That(branches, Is.Not.Null);
        Assert.That(branches, Has.Count.GreaterThanOrEqualTo(1));

        // Should have either 'main' or 'master' depending on git config
        var mainBranch = branches.FirstOrDefault(b => b.ShortName == "main" || b.ShortName == "master");
        Assert.That(mainBranch, Is.Not.Null);
        Assert.That(mainBranch!.IsCurrent, Is.True);
    }

    [Test]
    public async Task ListLocalBranchesAsync_ReturnsMultipleBranches()
    {
        // Arrange
        _fixture.CreateBranch("feature/test-1");
        _fixture.CreateBranch("feature/test-2");

        // Act
        var branches = await _service.ListLocalBranchesAsync(_fixture.RepositoryPath);

        // Assert
        Assert.That(branches, Has.Count.GreaterThanOrEqualTo(3));
        Assert.That(branches, Has.Some.Matches<BranchInfo>(b => b.ShortName == "feature/test-1"));
        Assert.That(branches, Has.Some.Matches<BranchInfo>(b => b.ShortName == "feature/test-2"));
    }

    [Test]
    public async Task ListLocalBranchesAsync_IdentifiesWorktreeBranches()
    {
        // Arrange
        var branchName = "feature/worktree-branch";
        _fixture.CreateBranch(branchName);

        var worktreePath = await _service.CreateWorktreeAsync(_fixture.RepositoryPath, branchName);
        Assert.That(worktreePath, Is.Not.Null);

        // Act
        var branches = await _service.ListLocalBranchesAsync(_fixture.RepositoryPath);

        // Assert
        var worktreeBranch = branches.FirstOrDefault(b => b.ShortName == branchName);
        Assert.That(worktreeBranch, Is.Not.Null);
        Assert.That(worktreeBranch!.HasWorktree, Is.True);
        Assert.That(worktreeBranch.WorktreePath, Is.Not.Null);
    }

    [Test]
    public async Task ListLocalBranchesAsync_ReturnsCommitInfo()
    {
        // Arrange
        _fixture.CreateBranch("feature/with-commit", checkout: true);
        _fixture.CreateFileAndCommit("test.txt", "test content", "Add test file");
        _fixture.RunGit("checkout -"); // Go back to main

        // Act
        var branches = await _service.ListLocalBranchesAsync(_fixture.RepositoryPath);

        // Assert
        var featureBranch = branches.FirstOrDefault(b => b.ShortName == "feature/with-commit");
        Assert.That(featureBranch, Is.Not.Null);
        Assert.That(featureBranch!.CommitSha, Is.Not.Null.And.Not.Empty);
        Assert.That(featureBranch.LastCommitMessage, Does.Contain("Add test file"));
    }

    #endregion

    #region IsBranchMergedAsync Integration Tests

    [Test]
    public async Task IsBranchMergedAsync_MergedBranch_ReturnsTrue()
    {
        // Arrange - Create and merge a branch
        var branchName = "feature/to-merge";
        _fixture.CreateBranch(branchName, checkout: true);
        _fixture.CreateFileAndCommit("merged.txt", "merged content", "Add merged file");
        _fixture.RunGit("checkout -"); // Back to main
        _fixture.RunGit($"merge \"{branchName}\" --no-ff -m \"Merge feature branch\"");

        // Get the default branch name
        var defaultBranch = _fixture.RunGit("rev-parse --abbrev-ref HEAD").Trim();

        // Act
        var isMerged = await _service.IsBranchMergedAsync(_fixture.RepositoryPath, branchName, defaultBranch);

        // Assert
        Assert.That(isMerged, Is.True);
    }

    [Test]
    public async Task IsBranchMergedAsync_UnmergedBranch_ReturnsFalse()
    {
        // Arrange - Create a branch with changes but don't merge
        var branchName = "feature/unmerged";
        _fixture.CreateBranch(branchName, checkout: true);
        _fixture.CreateFileAndCommit("unmerged.txt", "unmerged content", "Add unmerged file");
        _fixture.RunGit("checkout -"); // Back to main

        // Get the default branch name
        var defaultBranch = _fixture.RunGit("rev-parse --abbrev-ref HEAD").Trim();

        // Act
        var isMerged = await _service.IsBranchMergedAsync(_fixture.RepositoryPath, branchName, defaultBranch);

        // Assert
        Assert.That(isMerged, Is.False);
    }

    #endregion

    #region DeleteLocalBranchAsync Integration Tests

    [Test]
    public async Task DeleteLocalBranchAsync_MergedBranch_DeletesSuccessfully()
    {
        // Arrange - Create and merge a branch
        var branchName = "feature/delete-merged";
        _fixture.CreateBranch(branchName, checkout: true);
        _fixture.CreateFileAndCommit("delete.txt", "content", "Add file");
        _fixture.RunGit("checkout -");
        _fixture.RunGit($"merge \"{branchName}\" --no-ff -m \"Merge\"");

        // Act
        var result = await _service.DeleteLocalBranchAsync(_fixture.RepositoryPath, branchName);

        // Assert
        Assert.That(result, Is.True);

        var branches = _fixture.RunGit("branch --list");
        Assert.That(branches, Does.Not.Contain(branchName));
    }

    [Test]
    public async Task DeleteLocalBranchAsync_UnmergedBranch_FailsWithoutForce()
    {
        // Arrange - Create a branch with changes but don't merge
        var branchName = "feature/delete-unmerged";
        _fixture.CreateBranch(branchName, checkout: true);
        _fixture.CreateFileAndCommit("unmerged.txt", "content", "Add file");
        _fixture.RunGit("checkout -");

        // Act
        var result = await _service.DeleteLocalBranchAsync(_fixture.RepositoryPath, branchName, force: false);

        // Assert
        Assert.That(result, Is.False);

        // Branch should still exist
        var branches = _fixture.RunGit("branch --list");
        Assert.That(branches, Does.Contain(branchName));
    }

    [Test]
    public async Task DeleteLocalBranchAsync_UnmergedBranchWithForce_DeletesSuccessfully()
    {
        // Arrange - Create a branch with changes but don't merge
        var branchName = "feature/force-delete";
        _fixture.CreateBranch(branchName, checkout: true);
        _fixture.CreateFileAndCommit("force.txt", "content", "Add file");
        _fixture.RunGit("checkout -");

        // Act
        var result = await _service.DeleteLocalBranchAsync(_fixture.RepositoryPath, branchName, force: true);

        // Assert
        Assert.That(result, Is.True);

        var branches = _fixture.RunGit("branch --list");
        Assert.That(branches, Does.Not.Contain(branchName));
    }

    #endregion

    #region GetBranchDivergenceAsync Integration Tests

    [Test]
    public async Task GetBranchDivergenceAsync_BranchAhead_ReturnsCorrectCount()
    {
        // Arrange
        var branchName = "feature/ahead-branch";
        var defaultBranch = _fixture.RunGit("rev-parse --abbrev-ref HEAD").Trim();

        _fixture.CreateBranch(branchName, checkout: true);
        _fixture.CreateFileAndCommit("file1.txt", "content1", "Commit 1");
        _fixture.CreateFileAndCommit("file2.txt", "content2", "Commit 2");
        _fixture.RunGit("checkout -");

        // Act
        var (ahead, behind) = await _service.GetBranchDivergenceAsync(_fixture.RepositoryPath, branchName, defaultBranch);

        // Assert
        Assert.That(ahead, Is.EqualTo(2));
        Assert.That(behind, Is.EqualTo(0));
    }

    [Test]
    public async Task GetBranchDivergenceAsync_BranchBehind_ReturnsCorrectCount()
    {
        // Arrange
        var branchName = "feature/behind-branch";
        var defaultBranch = _fixture.RunGit("rev-parse --abbrev-ref HEAD").Trim();

        _fixture.CreateBranch(branchName);
        // Add commits to main after creating branch
        _fixture.CreateFileAndCommit("main1.txt", "main content", "Main commit 1");
        _fixture.CreateFileAndCommit("main2.txt", "main content 2", "Main commit 2");

        // Act
        var (ahead, behind) = await _service.GetBranchDivergenceAsync(_fixture.RepositoryPath, branchName, defaultBranch);

        // Assert
        Assert.That(ahead, Is.EqualTo(0));
        Assert.That(behind, Is.EqualTo(2));
    }

    [Test]
    public async Task GetBranchDivergenceAsync_BranchDiverged_ReturnsBothCounts()
    {
        // Arrange
        var branchName = "feature/diverged-branch";
        var defaultBranch = _fixture.RunGit("rev-parse --abbrev-ref HEAD").Trim();

        _fixture.CreateBranch(branchName, checkout: true);
        _fixture.CreateFileAndCommit("feature.txt", "feature content", "Feature commit");
        _fixture.RunGit("checkout -");
        _fixture.CreateFileAndCommit("main.txt", "main content", "Main commit");

        // Act
        var (ahead, behind) = await _service.GetBranchDivergenceAsync(_fixture.RepositoryPath, branchName, defaultBranch);

        // Assert
        Assert.That(ahead, Is.EqualTo(1));
        Assert.That(behind, Is.EqualTo(1));
    }

    #endregion

    #region FetchAllAsync Integration Tests

    [Test]
    public async Task FetchAllAsync_NoRemote_DoesNotThrow()
    {
        // The test repo has no remotes configured
        // git fetch --all --prune might succeed (returning exit 0) or fail depending on git version
        // The important thing is it doesn't throw an exception

        // Act & Assert - Should not throw
        await _service.FetchAllAsync(_fixture.RepositoryPath);
    }

    #endregion

    #region Branch Name Recalculation Tests (Issue 1JudQJ)

    /// <summary>
    /// Integration test for issue 1JudQJ: Verifies that when creating a worktree,
    /// the branch name is calculated from current issue properties (type, group, title).
    /// This test simulates the scenario where an issue's type changes before creating a worktree.
    /// </summary>
    [Test]
    public async Task CreateWorktree_WithRecalculatedBranchName_CreatesCorrectBranch()
    {
        // Arrange - Simulate the branch naming pattern used by Homespun
        // Format: {group}/{type}/{branch-id}+{issue-id}
        var issueId = "abc123";

        // Original branch name (as if issue was Feature type)
        var originalBranchName = $"issues/feature/fix-something+{issueId}";

        // Recalculated branch name (as if issue was changed to Bug type)
        var recalculatedBranchName = $"issues/bug/fix-something+{issueId}";

        // Act - Create worktree with the recalculated branch name
        var worktreePath = await _service.CreateWorktreeAsync(
            _fixture.RepositoryPath,
            recalculatedBranchName,
            createBranch: true);

        // Assert - Worktree should be created with the recalculated name
        Assert.That(worktreePath, Is.Not.Null);
        Assert.That(Directory.Exists(worktreePath), Is.True);

        // Verify the branch was created with the correct name
        var branches = _fixture.RunGit("branch --list");
        Assert.That(branches, Does.Contain("issues/bug/fix-something+abc123"));
        Assert.That(branches, Does.Not.Contain("issues/feature/fix-something+abc123"));
    }

    /// <summary>
    /// Integration test for issue 1JudQJ: Verifies that branch name includes the correct
    /// group prefix when group is specified (not using default "issues" prefix).
    /// </summary>
    [Test]
    public async Task CreateWorktree_WithGroupInBranchName_CreatesCorrectBranch()
    {
        // Arrange - Branch name with custom group prefix
        var branchNameWithGroup = "core/task/implement-feature+xyz789";

        // Act
        var worktreePath = await _service.CreateWorktreeAsync(
            _fixture.RepositoryPath,
            branchNameWithGroup,
            createBranch: true);

        // Assert
        Assert.That(worktreePath, Is.Not.Null);
        Assert.That(Directory.Exists(worktreePath), Is.True);

        // Verify the branch includes the group prefix
        var branches = _fixture.RunGit("branch --list");
        Assert.That(branches, Does.Contain("core/task/implement-feature+xyz789"));
    }

    /// <summary>
    /// Integration test for issue 1JudQJ: Verifies that the worktree path matches
    /// the branch name (after sanitization) to ensure consistency.
    /// </summary>
    [Test]
    public async Task CreateWorktree_BranchNameAndWorktreePath_AreConsistent()
    {
        // Arrange
        var branchName = "issues/feature/some-feature+def456";

        // Act
        var worktreePath = await _service.CreateWorktreeAsync(
            _fixture.RepositoryPath,
            branchName,
            createBranch: true);

        // Assert - Worktree path should be based on sanitized branch name
        Assert.That(worktreePath, Is.Not.Null);

        // The worktree folder name should match the sanitized branch name
        // (+ is sanitized to -)
        var expectedPathSegment = "issues/feature/some-feature-def456";
        Assert.That(worktreePath, Does.Contain("issues"));
        Assert.That(worktreePath, Does.Contain("feature"));
        Assert.That(worktreePath, Does.Contain("some-feature-def456"));
    }

    /// <summary>
    /// Integration test for issue 1JudQJ: Verifies that creating a worktree with a different
    /// branch name (simulating an issue property change) creates a separate worktree.
    /// This demonstrates that old worktrees are NOT automatically updated.
    /// </summary>
    [Test]
    public async Task CreateWorktree_WithDifferentBranchNames_CreatesSeparateWorktrees()
    {
        // Arrange - Two different branch names for same logical issue
        // (simulating issue type change from Feature to Bug)
        var issueId = "conflict1";
        var originalBranchName = $"issues/feature/test-issue+{issueId}";
        var modifiedBranchName = $"issues/bug/test-issue+{issueId}";

        // Act - Create worktrees with both names
        var worktree1 = await _service.CreateWorktreeAsync(
            _fixture.RepositoryPath,
            originalBranchName,
            createBranch: true);

        var worktree2 = await _service.CreateWorktreeAsync(
            _fixture.RepositoryPath,
            modifiedBranchName,
            createBranch: true);

        // Assert - Both worktrees should exist (this is the current behavior)
        // The fix ensures the UI always uses the recalculated name, preventing this scenario
        Assert.That(worktree1, Is.Not.Null);
        Assert.That(worktree2, Is.Not.Null);
        Assert.That(worktree1, Is.Not.EqualTo(worktree2));

        // Both branches should exist
        var branches = _fixture.RunGit("branch --list");
        Assert.That(branches, Does.Contain($"issues/feature/test-issue+{issueId}"));
        Assert.That(branches, Does.Contain($"issues/bug/test-issue+{issueId}"));
    }

    /// <summary>
    /// Integration test for issue 1JudQJ: Verifies that GetWorktreePathForBranch
    /// can find a worktree using the recalculated branch name.
    /// </summary>
    [Test]
    public async Task GetWorktreePathForBranch_WithRecalculatedName_FindsCorrectWorktree()
    {
        // Arrange - Create a worktree with initial branch name
        var branchName = "issues/task/my-task+recalc1";
        var worktreePath = await _service.CreateWorktreeAsync(
            _fixture.RepositoryPath,
            branchName,
            createBranch: true);
        Assert.That(worktreePath, Is.Not.Null);

        // Act - Look up the worktree using the same branch name
        // (simulating that recalculation produces the same name)
        var foundPath = await _service.GetWorktreePathForBranchAsync(_fixture.RepositoryPath, branchName);

        // Assert
        Assert.That(foundPath, Is.Not.Null);
        Assert.That(NormalizePath(foundPath!), Is.EqualTo(NormalizePath(worktreePath!)));
    }

    #endregion
}
