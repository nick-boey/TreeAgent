using Homespun.Features.Commands;
using Homespun.Features.Git;
using Homespun.Features.PullRequests.Data.Entities;
using Homespun.Features.Roadmap;
using Homespun.Tests.Helpers;
using Moq;
using Project = Homespun.Features.PullRequests.Data.Entities.Project;
using TrackedPullRequest = Homespun.Features.PullRequests.Data.Entities.PullRequest;

namespace Homespun.Tests.Features.Roadmap;

[TestFixture]
public class RoadmapServiceTests
{
    private TestDataStore _dataStore = null!;
    private Mock<ICommandRunner> _mockRunner = null!;
    private Mock<IGitWorktreeService> _mockWorktreeService = null!;
    private RoadmapService _service = null!;
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _dataStore = new TestDataStore();
        _mockRunner = new Mock<ICommandRunner>();
        _mockWorktreeService = new Mock<IGitWorktreeService>();

        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);

        _service = new RoadmapService(_dataStore, _mockRunner.Object, _mockWorktreeService.Object);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private async Task<Project> CreateTestProject()
    {
        var project = new Project
        {
            Name = "Test Project",
            LocalPath = _tempDir,
            GitHubOwner = "test-owner",
            GitHubRepo = "test-repo",
            DefaultBranch = "main"
        };

        await _dataStore.AddProjectAsync(project);
        return project;
    }

    private void CreateRoadmapFile(string content)
    {
        File.WriteAllText(Path.Combine(_tempDir, "ROADMAP.json"), content);
    }

    #region 3.1 Read and Display Future Changes

    [Test]
    public async Task FutureChanges_LoadFromRoadmap_DisplaysInTree()
    {
        // Arrange
        var project = await CreateTestProject();
        CreateRoadmapFile("""
        {
            "version": "1.0",
            "changes": [
                {
                    "id": "feature-one",
                    "group": "core",
                    "type": "feature",
                    "title": "Feature One"
                },
                {
                    "id": "feature-two",
                    "group": "web",
                    "type": "bug",
                    "title": "Bug Fix"
                }
            ]
        }
        """);

        // Act
        var result = await _service.GetFutureChangesAsync(project.Id);

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result[0].Change.Id, Is.EqualTo("feature-one"));
        Assert.That(result[1].Change.Id, Is.EqualTo("feature-two"));
    }

    [Test]
    public async Task FutureChanges_NestedChildren_DisplaysHierarchy()
    {
        // Arrange
        var project = await CreateTestProject();
        CreateRoadmapFile("""
        {
            "version": "1.0",
            "changes": [
                {
                    "id": "parent",
                    "group": "core",
                    "type": "feature",
                    "title": "Parent",
                    "children": [
                        {
                            "id": "child-1",
                            "group": "core",
                            "type": "feature",
                            "title": "Child 1"
                        },
                        {
                            "id": "child-2",
                            "group": "core",
                            "type": "feature",
                            "title": "Child 2"
                        }
                    ]
                }
            ]
        }
        """);

        // Act
        var result = await _service.GetFutureChangesAsync(project.Id);

        // Assert - Should return flat list with all changes
        Assert.That(result, Has.Count.EqualTo(3));
        Assert.That(result.Any(r => r.Change.Id == "parent"), Is.True);
        Assert.That(result.Any(r => r.Change.Id == "child-1"), Is.True);
        Assert.That(result.Any(r => r.Change.Id == "child-2"), Is.True);
    }

    [Test]
    public async Task FutureChanges_CalculatesTimeFromDepth()
    {
        // Arrange
        var project = await CreateTestProject();
        CreateRoadmapFile("""
        {
            "version": "1.0",
            "changes": [
                {
                    "id": "root",
                    "group": "core",
                    "type": "feature",
                    "title": "Root",
                    "children": [
                        {
                            "id": "child",
                            "group": "core",
                            "type": "feature",
                            "title": "Child",
                            "children": [
                                {
                                    "id": "grandchild",
                                    "group": "core",
                                    "type": "feature",
                                    "title": "Grandchild"
                                }
                            ]
                        }
                    ]
                }
            ]
        }
        """);

        // Act
        var result = await _service.GetFutureChangesAsync(project.Id);

        // Assert - Root at depth 0 -> t=2, child at depth 1 -> t=3, grandchild at depth 2 -> t=4
        Assert.That(result.First(r => r.Change.Id == "root").Time, Is.EqualTo(2));
        Assert.That(result.First(r => r.Change.Id == "child").Time, Is.EqualTo(3));
        Assert.That(result.First(r => r.Change.Id == "grandchild").Time, Is.EqualTo(4));
    }

    [Test]
    public async Task FutureChanges_GroupsDisplayedCorrectly()
    {
        // Arrange
        var project = await CreateTestProject();
        CreateRoadmapFile("""
        {
            "version": "1.0",
            "changes": [
                {
                    "id": "core-feature",
                    "group": "core",
                    "type": "feature",
                    "title": "Core Feature"
                },
                {
                    "id": "web-feature",
                    "group": "web",
                    "type": "feature",
                    "title": "Web Feature"
                },
                {
                    "id": "api-feature",
                    "group": "api",
                    "type": "feature",
                    "title": "API Feature"
                }
            ]
        }
        """);

        // Act
        var result = await _service.GetFutureChangesAsync(project.Id);
        var byGroup = await _service.GetFutureChangesByGroupAsync(project.Id);

        // Assert
        Assert.That(byGroup.Keys, Does.Contain("core"));
        Assert.That(byGroup.Keys, Does.Contain("web"));
        Assert.That(byGroup.Keys, Does.Contain("api"));
        Assert.That(byGroup["core"], Has.Count.EqualTo(1));
        Assert.That(byGroup["web"], Has.Count.EqualTo(1));
        Assert.That(byGroup["api"], Has.Count.EqualTo(1));
    }

    #endregion

    #region 3.2 Promote Future Change to Current PR

    [Test]
    public async Task PromoteChange_CreatesWorktree_WithCorrectBranchName()
    {
        // Arrange
        var project = await CreateTestProject();
        CreateRoadmapFile("""
        {
            "version": "1.0",
            "changes": [
                {
                    "id": "new-feature",
                    "group": "core",
                    "type": "feature",
                    "title": "New Feature"
                }
            ]
        }
        """);

        _mockWorktreeService.Setup(w => w.CreateWorktreeAsync(
            project.LocalPath,
            "core/feature/new-feature",
            It.IsAny<bool>(),
            It.IsAny<string?>()))
            .ReturnsAsync("/worktrees/core-feature-new-feature");

        // Act
        var result = await _service.PromoteChangeAsync(project.Id, "new-feature");

        // Assert
        Assert.That(result, Is.Not.Null);
        _mockWorktreeService.Verify(w => w.CreateWorktreeAsync(
            project.LocalPath,
            "core/feature/new-feature",
            It.IsAny<bool>(),
            It.IsAny<string?>()), Times.AtLeastOnce);
    }

    [Test]
    public async Task PromoteChange_CreatesFeature_WithChangeDetails()
    {
        // Arrange
        var project = await CreateTestProject();
        CreateRoadmapFile("""
        {
            "version": "1.0",
            "changes": [
                {
                    "id": "new-feature",
                    "group": "core",
                    "type": "feature",
                    "title": "New Feature Title",
                    "description": "Detailed description of the feature"
                }
            ]
        }
        """);

        _mockWorktreeService.Setup(w => w.CreateWorktreeAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<string?>()))
            .ReturnsAsync("/worktrees/new-feature");

        // Act
        var result = await _service.PromoteChangeAsync(project.Id, "new-feature");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Title, Is.EqualTo("New Feature Title"));
        Assert.That(result.Description, Is.EqualTo("Detailed description of the feature"));
        Assert.That(result.BranchName, Is.EqualTo("core/feature/new-feature"));
        Assert.That(result.Status, Is.EqualTo(OpenPullRequestStatus.InDevelopment));
    }

    [Test]
    public async Task PromoteChange_RemovesFromRoadmap_UpdatesTree()
    {
        // Arrange
        var project = await CreateTestProject();
        CreateRoadmapFile("""
        {
            "version": "1.0",
            "changes": [
                {
                    "id": "feature-to-promote",
                    "group": "core",
                    "type": "feature",
                    "title": "Feature To Promote"
                },
                {
                    "id": "other-feature",
                    "group": "core",
                    "type": "feature",
                    "title": "Other Feature"
                }
            ]
        }
        """);

        _mockWorktreeService.Setup(w => w.CreateWorktreeAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<string?>()))
            .ReturnsAsync("/worktrees/feature");

        // Act
        await _service.PromoteChangeAsync(project.Id, "feature-to-promote");

        // Assert - Verify the roadmap was updated
        var roadmapPath = Path.Combine(_tempDir, "ROADMAP.json");
        var updatedRoadmap = await RoadmapParser.LoadAsync(roadmapPath);

        Assert.That(updatedRoadmap.Changes, Has.Count.EqualTo(1));
        Assert.That(updatedRoadmap.Changes[0].Id, Is.EqualTo("other-feature"));
    }

    [Test]
    public async Task PromoteChange_PromotesChildren_ToParentLevel()
    {
        // Arrange
        var project = await CreateTestProject();
        CreateRoadmapFile("""
        {
            "version": "1.0",
            "changes": [
                {
                    "id": "parent-feature",
                    "group": "core",
                    "type": "feature",
                    "title": "Parent Feature",
                    "children": [
                        {
                            "id": "child-1",
                            "group": "core",
                            "type": "feature",
                            "title": "Child 1"
                        },
                        {
                            "id": "child-2",
                            "group": "core",
                            "type": "feature",
                            "title": "Child 2"
                        }
                    ]
                }
            ]
        }
        """);

        _mockWorktreeService.Setup(w => w.CreateWorktreeAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<string?>()))
            .ReturnsAsync("/worktrees/feature");

        // Act
        await _service.PromoteChangeAsync(project.Id, "parent-feature");

        // Assert - Children should be promoted to root level
        var roadmapPath = Path.Combine(_tempDir, "ROADMAP.json");
        var updatedRoadmap = await RoadmapParser.LoadAsync(roadmapPath);

        Assert.That(updatedRoadmap.Changes, Has.Count.EqualTo(2));
        Assert.That(updatedRoadmap.Changes.Any(c => c.Id == "child-1"), Is.True);
        Assert.That(updatedRoadmap.Changes.Any(c => c.Id == "child-2"), Is.True);
    }

    #endregion

    #region 3.3 Plan Update PRs

    [Test]
    public async Task PlanUpdate_OnlyRoadmapChanges_DetectedCorrectly()
    {
        // Arrange
        var project = await CreateTestProject();
        var pullRequest = new TrackedPullRequest
        {
            ProjectId = project.Id,
            Title = "Update Roadmap",
            BranchName = "plan-update/add-features",
            Status = OpenPullRequestStatus.InDevelopment
        };
        await _dataStore.AddPullRequestAsync(pullRequest);

        // Mock git diff showing only ROADMAP.json changed
        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.Contains("diff") && s.Contains("--name-only")), It.IsAny<string>()))
            .ReturnsAsync(new CommandResult { Success = true, Output = "ROADMAP.json" });

        // Act
        var result = await _service.IsPlanUpdateOnlyAsync(pullRequest.Id);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task PlanUpdate_MixedChanges_NotPlanUpdateOnly()
    {
        // Arrange
        var project = await CreateTestProject();
        var pullRequest = new TrackedPullRequest
        {
            ProjectId = project.Id,
            Title = "Mixed Changes",
            BranchName = "core/feature/mixed",
            Status = OpenPullRequestStatus.InDevelopment
        };
        await _dataStore.AddPullRequestAsync(pullRequest);

        // Mock git diff showing multiple files changed
        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.Contains("diff") && s.Contains("--name-only")), It.IsAny<string>()))
            .ReturnsAsync(new CommandResult { Success = true, Output = "ROADMAP.json\nsrc/SomeCode.cs" });

        // Act
        var result = await _service.IsPlanUpdateOnlyAsync(pullRequest.Id);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task PlanUpdate_ValidatesSchema_BeforePromotion()
    {
        // Arrange
        var project = await CreateTestProject();
        // Create invalid ROADMAP.json
        CreateRoadmapFile("""{ "version": "1.0", "changes": [{ "invalid": true }] }""");

        // Act & Assert
        var ex = Assert.ThrowsAsync<RoadmapValidationException>(
            async () => await _service.GetFutureChangesAsync(project.Id));
        Assert.That(ex!.Message, Does.Contain("id").Or.Contain("required"));
    }

    [Test]
    public async Task PlanUpdate_UsesPlanUpdateGroup_InBranchNaming()
    {
        // Arrange
        var project = await CreateTestProject();
        CreateRoadmapFile("""
        {
            "version": "1.0",
            "changes": []
        }
        """);

        // Act
        var branchName = _service.GeneratePlanUpdateBranchName("add-new-features");

        // Assert
        Assert.That(branchName, Does.StartWith("plan-update/"));
        Assert.That(branchName, Is.EqualTo("plan-update/chore/add-new-features"));
    }

    #endregion
}
