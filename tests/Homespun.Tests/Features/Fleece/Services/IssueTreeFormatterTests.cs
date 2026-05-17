using NUnit.Framework;
using Fleece.Core.Models;
using Homespun.Features.Fleece.Services;

namespace Homespun.Tests.Features.Fleece.Services;

[TestFixture]
public class IssueTreeFormatterTests
{
    [Test]
    public void FormatTree_SingleIssueNoParentsNoChildren_ReturnsJustIssue()
    {
        // Arrange
        var issue = CreateIssue("abc123", "Test Issue", IssueType.Task, IssueStatus.Open);
        var allIssues = new List<Issue> { issue };

        // Act
        var result = IssueTreeFormatter.FormatIssueTree(issue, allIssues);

        // Assert
        Assert.That(result, Is.EqualTo("- abc123 [task] [open] Test Issue"));
    }

    [Test]
    public void FormatTree_IssueWithOneParent_ShowsParentThenIssue()
    {
        // Arrange
        var parent = CreateIssue("parent1", "Parent Issue", IssueType.Feature, IssueStatus.Open);
        var child = CreateIssue("child1", "Child Issue", IssueType.Task, IssueStatus.Progress, parentId: "parent1");
        var allIssues = new List<Issue> { parent, child };

        // Act
        var result = IssueTreeFormatter.FormatIssueTree(child, allIssues);

        // Assert
        var expected = """
            - parent1 [feature] [open] Parent Issue
              - child1 [task] [progress] Child Issue
            """;
        Assert.That(result, Is.EqualTo(expected.TrimEnd()));
    }

    [Test]
    public void FormatTree_IssueWithAncestorChain_ShowsAllAncestorsInOrder()
    {
        // Arrange - grandparent -> parent -> current
        var grandparent = CreateIssue("gp1", "Grandparent", IssueType.Feature, IssueStatus.Open);
        var parent = CreateIssue("p1", "Parent", IssueType.Feature, IssueStatus.Open, parentId: "gp1");
        var current = CreateIssue("c1", "Current", IssueType.Task, IssueStatus.Progress, parentId: "p1");
        var allIssues = new List<Issue> { grandparent, parent, current };

        // Act
        var result = IssueTreeFormatter.FormatIssueTree(current, allIssues);

        // Assert
        var expected = """
            - gp1 [feature] [open] Grandparent
              - p1 [feature] [open] Parent
                - c1 [task] [progress] Current
            """;
        Assert.That(result, Is.EqualTo(expected.TrimEnd()));
    }

    [Test]
    public void FormatTree_IssueWithDirectChildren_ShowsChildrenAfterIssue()
    {
        // Arrange
        var current = CreateIssue("curr1", "Current Issue", IssueType.Feature, IssueStatus.Progress);
        var child1 = CreateIssue("ch1", "Child One", IssueType.Bug, IssueStatus.Complete, parentId: "curr1");
        var child2 = CreateIssue("ch2", "Child Two", IssueType.Task, IssueStatus.Open, parentId: "curr1");
        var allIssues = new List<Issue> { current, child1, child2 };

        // Act
        var result = IssueTreeFormatter.FormatIssueTree(current, allIssues);

        // Assert - children should appear after current issue, indented one more level
        var expected = """
            - curr1 [feature] [progress] Current Issue
              - ch1 [bug] [complete] Child One
              - ch2 [task] [open] Child Two
            """;
        Assert.That(result, Is.EqualTo(expected.TrimEnd()));
    }

    [Test]
    public void FormatTree_IssueWithGrandchildren_ExcludesGrandchildren()
    {
        // Arrange
        var current = CreateIssue("curr1", "Current Issue", IssueType.Feature, IssueStatus.Progress);
        var child = CreateIssue("ch1", "Child Issue", IssueType.Task, IssueStatus.Open, parentId: "curr1");
        var grandchild = CreateIssue("gc1", "Grandchild Issue", IssueType.Bug, IssueStatus.Open, parentId: "ch1");
        var allIssues = new List<Issue> { current, child, grandchild };

        // Act
        var result = IssueTreeFormatter.FormatIssueTree(current, allIssues);

        // Assert - grandchild should NOT appear
        var expected = """
            - curr1 [feature] [progress] Current Issue
              - ch1 [task] [open] Child Issue
            """;
        Assert.That(result, Is.EqualTo(expected.TrimEnd()));
        Assert.That(result, Does.Not.Contain("gc1"));
        Assert.That(result, Does.Not.Contain("Grandchild"));
    }

    [Test]
    public void FormatTree_CompleteHierarchy_FormatsCorrectly()
    {
        // Arrange - full hierarchy: grandparent -> parent -> current -> children
        var grandparent = CreateIssue("v9n23k", "Parent 1", IssueType.Feature, IssueStatus.Open);
        var parent = CreateIssue("fk28rh", "Parent 2", IssueType.Feature, IssueStatus.Open, parentId: "v9n23k");
        var current = CreateIssue("pzk048", "This issue", IssueType.Task, IssueStatus.Progress, parentId: "fk28rh");
        var child1 = CreateIssue("ruw192", "Child 1", IssueType.Bug, IssueStatus.Complete, parentId: "pzk048", sortOrder: "aaa");
        var child2 = CreateIssue("bfh293", "Child 2", IssueType.Task, IssueStatus.Complete, parentId: "pzk048", sortOrder: "bbb");
        var allIssues = new List<Issue> { grandparent, parent, current, child1, child2 };

        // Act
        var result = IssueTreeFormatter.FormatIssueTree(current, allIssues);

        // Assert - matches the example from the issue description
        var expected = """
            - v9n23k [feature] [open] Parent 1
              - fk28rh [feature] [open] Parent 2
                - pzk048 [task] [progress] This issue
                  - ruw192 [bug] [complete] Child 1
                  - bfh293 [task] [complete] Child 2
            """;
        Assert.That(result, Is.EqualTo(expected.TrimEnd()));
    }

    [Test]
    public void FormatTree_MissingParentInList_HandlesGracefully()
    {
        // Arrange - child references parent that doesn't exist in allIssues
        var child = CreateIssue("ch1", "Child Issue", IssueType.Task, IssueStatus.Progress, parentId: "missing123");
        var allIssues = new List<Issue> { child };

        // Act
        var result = IssueTreeFormatter.FormatIssueTree(child, allIssues);

        // Assert - should just show the child, ignoring missing parent
        Assert.That(result, Is.EqualTo("- ch1 [task] [progress] Child Issue"));
    }

    [Test]
    public void FormatTree_MultipleParents_FollowsFirstParentOnly()
    {
        // Arrange - issue has multiple parents, should only follow first
        var parent1 = CreateIssue("p1", "Primary Parent", IssueType.Feature, IssueStatus.Open);
        var parent2 = CreateIssue("p2", "Secondary Parent", IssueType.Feature, IssueStatus.Open);
        var child = CreateIssueWithMultipleParents("c1", "Child", IssueType.Task, IssueStatus.Progress,
            new[] { ("p1", "0"), ("p2", "1") });
        var allIssues = new List<Issue> { parent1, parent2, child };

        // Act
        var result = IssueTreeFormatter.FormatIssueTree(child, allIssues);

        // Assert - only p1 should appear (first parent)
        var expected = """
            - p1 [feature] [open] Primary Parent
              - c1 [task] [progress] Child
            """;
        Assert.That(result, Is.EqualTo(expected.TrimEnd()));
        Assert.That(result, Does.Not.Contain("p2"));
        Assert.That(result, Does.Not.Contain("Secondary"));
    }

    [Test]
    public void FormatTree_MultipleChildrenWithSortOrder_SortedBySortOrder()
    {
        // Arrange
        var current = CreateIssue("curr1", "Current", IssueType.Feature, IssueStatus.Progress);
        var childA = CreateIssue("chA", "Alpha Child", IssueType.Task, IssueStatus.Open, parentId: "curr1", sortOrder: "bbb");
        var childB = CreateIssue("chB", "Beta Child", IssueType.Task, IssueStatus.Open, parentId: "curr1", sortOrder: "aaa");
        var allIssues = new List<Issue> { current, childA, childB };

        // Act
        var result = IssueTreeFormatter.FormatIssueTree(current, allIssues);

        // Assert - childB (sortOrder "aaa") should come before childA (sortOrder "bbb")
        var lines = result.Split('\n');
        var childBIndex = Array.FindIndex(lines, l => l.Contains("chB"));
        var childAIndex = Array.FindIndex(lines, l => l.Contains("chA"));
        Assert.That(childBIndex, Is.LessThan(childAIndex), "Child with lower sort order should come first");
    }

    [Test]
    public void FormatTree_CircularReference_DoesNotInfiniteLoop()
    {
        // Arrange - create a circular reference: A -> B -> A
        var issueA = CreateIssue("issA", "Issue A", IssueType.Task, IssueStatus.Open, parentId: "issB");
        var issueB = CreateIssue("issB", "Issue B", IssueType.Task, IssueStatus.Open, parentId: "issA");
        var allIssues = new List<Issue> { issueA, issueB };

        // Act - should not hang or throw
        var result = IssueTreeFormatter.FormatIssueTree(issueA, allIssues);

        // Assert - should produce some output without infinite loop
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("issA"));
    }

    [Test]
    public void FormatTree_AllIssueTypesFormatCorrectly()
    {
        // Arrange
        var feature = CreateIssue("f1", "Feature", IssueType.Feature, IssueStatus.Open);
        var task = CreateIssue("t1", "Task", IssueType.Task, IssueStatus.Progress);
        var bug = CreateIssue("b1", "Bug", IssueType.Bug, IssueStatus.Review);
        var chore = CreateIssue("c1", "Chore", IssueType.Chore, IssueStatus.Complete);

        // Act & Assert
        Assert.That(IssueTreeFormatter.FormatIssueTree(feature, new[] { feature }), Does.Contain("[feature]"));
        Assert.That(IssueTreeFormatter.FormatIssueTree(task, new[] { task }), Does.Contain("[task]"));
        Assert.That(IssueTreeFormatter.FormatIssueTree(bug, new[] { bug }), Does.Contain("[bug]"));
        Assert.That(IssueTreeFormatter.FormatIssueTree(chore, new[] { chore }), Does.Contain("[chore]"));
    }

    [Test]
    public void FormatTree_AllStatusesFormatCorrectly()
    {
        // Arrange & Act & Assert
        var statuses = new Dictionary<IssueStatus, string>
        {
            { IssueStatus.Open, "open" },
            { IssueStatus.Progress, "progress" },
            { IssueStatus.Review, "review" },
            { IssueStatus.Complete, "complete" },
            { IssueStatus.Archived, "archived" },
            { IssueStatus.Closed, "closed" }
        };

        foreach (var (status, expected) in statuses)
        {
            var issue = CreateIssue("id1", "Test", IssueType.Task, status);
            var result = IssueTreeFormatter.FormatIssueTree(issue, new[] { issue });
            Assert.That(result, Does.Contain($"[{expected}]"), $"Status {status} should format as [{expected}]");
        }
    }

    // Helper method to create issues for testing
    private static Issue CreateIssue(
        string id,
        string title,
        IssueType type,
        IssueStatus status,
        string? parentId = null,
        string? sortOrder = null)
    {
        var parentIssues = parentId != null
            ? new List<ParentIssueRef>
            {
                new() { ParentIssue = parentId, SortOrder = sortOrder ?? "0" }
            }
            : new List<ParentIssueRef>();

        return new Issue
        {
            Id = id,
            Title = title,
            Type = type,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdate = DateTimeOffset.UtcNow,
            ParentIssues = parentIssues
        };
    }

    // Helper method to create issues with multiple parents
    private static Issue CreateIssueWithMultipleParents(
        string id,
        string title,
        IssueType type,
        IssueStatus status,
        (string parentId, string sortOrder)[] parents)
    {
        return new Issue
        {
            Id = id,
            Title = title,
            Type = type,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdate = DateTimeOffset.UtcNow,
            ParentIssues = parents.Select(p => new ParentIssueRef
            {
                ParentIssue = p.parentId,
                SortOrder = p.sortOrder
            }).ToList()
        };
    }
}
