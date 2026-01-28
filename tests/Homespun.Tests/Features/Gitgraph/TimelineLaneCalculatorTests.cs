using Homespun.Features.Gitgraph.Data;
using Homespun.Features.Gitgraph.Services;

namespace Homespun.Tests.Features.Gitgraph;

[TestFixture]
public class TimelineLaneCalculatorTests
{
    private TimelineLaneCalculator _calculator = null!;

    [SetUp]
    public void SetUp()
    {
        _calculator = new TimelineLaneCalculator();
    }

    #region Basic Layout Tests

    [Test]
    public void Calculate_EmptyNodes_ReturnsEmptyLayout()
    {
        // Act
        var layout = _calculator.Calculate([]);

        // Assert
        Assert.That(layout.LaneAssignments, Is.Empty);
        Assert.That(layout.MaxLanes, Is.EqualTo(1));
        Assert.That(layout.RowInfos, Is.Empty);
    }

    [Test]
    public void Calculate_SingleMainNode_UsesLaneZero()
    {
        // Arrange
        var nodes = new List<IGraphNode>
        {
            CreateNode("node-1", "main")
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert
        Assert.That(layout.LaneAssignments["node-1"], Is.EqualTo(0));
        Assert.That(layout.MaxLanes, Is.EqualTo(1));
        Assert.That(layout.RowInfos, Has.Count.EqualTo(1));
        Assert.That(layout.RowInfos[0].NodeLane, Is.EqualTo(0));
    }

    [Test]
    public void Calculate_LinearMainBranch_AllInLaneZero()
    {
        // Arrange - Three nodes in main branch
        var nodes = new List<IGraphNode>
        {
            CreateNode("node-1", "main"),
            CreateNode("node-2", "main", parentIds: ["node-1"]),
            CreateNode("node-3", "main", parentIds: ["node-2"])
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - All nodes should be in lane 0
        Assert.That(layout.LaneAssignments["node-1"], Is.EqualTo(0));
        Assert.That(layout.LaneAssignments["node-2"], Is.EqualTo(0));
        Assert.That(layout.LaneAssignments["node-3"], Is.EqualTo(0));
        Assert.That(layout.MaxLanes, Is.EqualTo(1));
    }

    #endregion

    #region Branch Tests

    [Test]
    public void Calculate_SingleBranch_UsesLaneOne()
    {
        // Arrange - Main branch + one side branch
        var nodes = new List<IGraphNode>
        {
            CreateNode("pr-1", "main"),
            CreateNode("pr-2", "feature/test", parentIds: ["pr-1"])
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert
        Assert.That(layout.LaneAssignments["pr-1"], Is.EqualTo(0));
        Assert.That(layout.LaneAssignments["pr-2"], Is.EqualTo(1));
        Assert.That(layout.MaxLanes, Is.EqualTo(2));
    }

    [Test]
    public void Calculate_MultipleBranches_UsesDifferentLanes()
    {
        // Arrange - Main branch + two side branches where one ends before the other starts
        // This tests lane reuse behavior: feature/a ends, then feature/b reuses its lane
        var nodes = new List<IGraphNode>
        {
            CreateNode("pr-1", "main"),
            CreateNode("pr-2", "feature/a", parentIds: ["pr-1"]),
            CreateNode("pr-2b", "feature/a", parentIds: ["pr-2"]),
            CreateNode("pr-3", "feature/b", parentIds: ["pr-1"]),
            CreateNode("pr-3b", "feature/b", parentIds: ["pr-3"])
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - feature/a uses lane 1, then ends, feature/b reuses lane 1
        Assert.That(layout.LaneAssignments["pr-1"], Is.EqualTo(0));
        Assert.That(layout.LaneAssignments["pr-2"], Is.EqualTo(1));
        Assert.That(layout.LaneAssignments["pr-2b"], Is.EqualTo(1));
        Assert.That(layout.LaneAssignments["pr-3"], Is.EqualTo(1)); // Reuses lane 1
        Assert.That(layout.LaneAssignments["pr-3b"], Is.EqualTo(1));
        Assert.That(layout.MaxLanes, Is.EqualTo(2)); // Only used 2 lanes total
    }

    [Test]
    public void Calculate_BranchWithMultipleNodes_StaysInSameLane()
    {
        // Arrange - Main + branch with multiple commits
        var nodes = new List<IGraphNode>
        {
            CreateNode("pr-1", "main"),
            CreateNode("issue-1", "issue-bd-001", parentIds: ["pr-1"]),
            CreateNode("issue-2", "issue-bd-001", parentIds: ["issue-1"])
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - Both issue nodes should be in the same lane
        Assert.That(layout.LaneAssignments["pr-1"], Is.EqualTo(0));
        Assert.That(layout.LaneAssignments["issue-1"], Is.EqualTo(1));
        Assert.That(layout.LaneAssignments["issue-2"], Is.EqualTo(1));
        Assert.That(layout.MaxLanes, Is.EqualTo(2));
    }

    #endregion

    #region Connector Tests

    [Test]
    public void Calculate_BranchStart_HasConnectorFromParentLane()
    {
        // Arrange
        var nodes = new List<IGraphNode>
        {
            CreateNode("pr-1", "main"),
            CreateNode("pr-2", "feature/test", parentIds: ["pr-1"])
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - Branch node should have connector from main (lane 0)
        var branchRowInfo = layout.RowInfos[1];
        Assert.That(branchRowInfo.ConnectorFromLane, Is.EqualTo(0));
        Assert.That(branchRowInfo.NodeLane, Is.EqualTo(1));
    }

    [Test]
    public void Calculate_MainBranchNodes_NoConnector()
    {
        // Arrange
        var nodes = new List<IGraphNode>
        {
            CreateNode("pr-1", "main"),
            CreateNode("pr-2", "main", parentIds: ["pr-1"])
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - Main branch nodes should not have connectors
        Assert.That(layout.RowInfos[0].ConnectorFromLane, Is.Null);
        Assert.That(layout.RowInfos[1].ConnectorFromLane, Is.Null);
    }

    [Test]
    public void Calculate_ContinuingBranch_NoConnector()
    {
        // Arrange - Branch with two nodes
        var nodes = new List<IGraphNode>
        {
            CreateNode("pr-1", "main"),
            CreateNode("issue-1", "issue-bd-001", parentIds: ["pr-1"]),
            CreateNode("issue-2", "issue-bd-001", parentIds: ["issue-1"])
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - Second issue should not have a connector (continues in same lane)
        Assert.That(layout.RowInfos[1].ConnectorFromLane, Is.EqualTo(0)); // First branch node
        Assert.That(layout.RowInfos[2].ConnectorFromLane, Is.Null); // Continuation
    }

    #endregion

    #region Active Lanes Tests

    [Test]
    public void Calculate_ActiveLanes_IncludesAllActiveBranches()
    {
        // Arrange - Main + two branches with interleaved nodes
        // to test that both lanes are active simultaneously
        var nodes = new List<IGraphNode>
        {
            CreateNode("pr-1", "main"),
            CreateNode("pr-2", "feature/a", parentIds: ["pr-1"]),
            CreateNode("pr-3", "feature/b", parentIds: ["pr-1"]), // Starts while feature/a still active
            CreateNode("pr-2b", "feature/a", parentIds: ["pr-2"]),
            CreateNode("pr-3b", "feature/b", parentIds: ["pr-3"])
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - Both feature branches should get different lanes since they overlap
        Assert.That(layout.LaneAssignments["pr-2"], Is.EqualTo(1)); // feature/a
        Assert.That(layout.LaneAssignments["pr-3"], Is.EqualTo(2)); // feature/b (can't reuse lane 1 since feature/a not done)

        // Assert - Row with pr-2b should have lanes 0, 1, 2 all active
        var row3Info = layout.RowInfos[3]; // pr-2b
        Assert.That(row3Info.ActiveLanes, Does.Contain(0)); // Main
        Assert.That(row3Info.ActiveLanes, Does.Contain(1)); // feature/a
        Assert.That(row3Info.ActiveLanes, Does.Contain(2)); // feature/b
    }

    [Test]
    public void Calculate_LaneReuse_ReleasedLaneCanBeReused()
    {
        // Arrange - Main + branch that ends, then new branch
        var nodes = new List<IGraphNode>
        {
            CreateNode("pr-1", "main"),
            CreateNode("issue-1", "branch-a", parentIds: ["pr-1"]), // Uses lane 1
            // branch-a ends here
            CreateNode("issue-2", "branch-b", parentIds: ["pr-1"]) // Should reuse lane 1
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - Lane 1 should be reused after branch-a ends
        Assert.That(layout.LaneAssignments["issue-1"], Is.EqualTo(1));
        Assert.That(layout.LaneAssignments["issue-2"], Is.EqualTo(1)); // Reuses lane 1
        Assert.That(layout.MaxLanes, Is.EqualTo(2));
    }

    #endregion

    #region Row Info Tests

    [Test]
    public void Calculate_RowInfos_MatchNodeOrder()
    {
        // Arrange
        var nodes = new List<IGraphNode>
        {
            CreateNode("node-1", "main"),
            CreateNode("node-2", "main", parentIds: ["node-1"]),
            CreateNode("node-3", "feature", parentIds: ["node-2"])
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - Row infos should match node order
        Assert.That(layout.RowInfos, Has.Count.EqualTo(3));
        Assert.That(layout.RowInfos[0].NodeId, Is.EqualTo("node-1"));
        Assert.That(layout.RowInfos[1].NodeId, Is.EqualTo("node-2"));
        Assert.That(layout.RowInfos[2].NodeId, Is.EqualTo("node-3"));
    }

    [Test]
    public void Calculate_RowInfos_ContainCorrectNodeLanes()
    {
        // Arrange
        var nodes = new List<IGraphNode>
        {
            CreateNode("pr-1", "main"),
            CreateNode("pr-2", "feature", parentIds: ["pr-1"])
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert
        Assert.That(layout.RowInfos[0].NodeLane, Is.EqualTo(0));
        Assert.That(layout.RowInfos[1].NodeLane, Is.EqualTo(1));
    }

    #endregion

    #region Issue Dependency Tests

    [Test]
    public void Calculate_IssueDependencyChain_FollowsParentLane()
    {
        // Arrange - Issue chain where each is a separate branch
        // bd-001 -> bd-002 -> bd-003 (each on different branch)
        // Since branches end after single node, lanes get reused
        var nodes = new List<IGraphNode>
        {
            CreateNode("pr-1", "main"),
            CreateNode("issue-bd-001", "issue-bd-001", parentIds: ["pr-1"]),
            CreateNode("issue-bd-002", "issue-bd-002", parentIds: ["issue-bd-001"]),
            CreateNode("issue-bd-003", "issue-bd-003", parentIds: ["issue-bd-002"])
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - First issue gets lane 1, subsequent issues reuse lane 1 (since previous branch ends)
        Assert.That(layout.LaneAssignments["issue-bd-001"], Is.EqualTo(1));
        // bd-002 reuses lane 1 because bd-001's branch ended
        Assert.That(layout.LaneAssignments["issue-bd-002"], Is.EqualTo(1));
        // bd-003 also reuses lane 1
        Assert.That(layout.LaneAssignments["issue-bd-003"], Is.EqualTo(1));

        // Verify connectors come from parent lanes
        Assert.That(layout.RowInfos[1].ConnectorFromLane, Is.EqualTo(0)); // From main
        Assert.That(layout.RowInfos[2].ConnectorFromLane, Is.EqualTo(1)); // From bd-001 (same lane, so connector from previous lane)
        Assert.That(layout.RowInfos[3].ConnectorFromLane, Is.EqualTo(1)); // From bd-002 (same lane)
    }

    [Test]
    public void Calculate_OrphanIssues_ChainedInSameBranch()
    {
        // Arrange - Orphan issues chained on same branch
        var nodes = new List<IGraphNode>
        {
            CreateNode("pr-1", "main"),
            CreateNode("issue-1", "orphan-issues", parentIds: ["pr-1"]),
            CreateNode("issue-2", "orphan-issues", parentIds: ["issue-1"]),
            CreateNode("issue-3", "orphan-issues", parentIds: ["issue-2"])
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - All orphans should be in the same lane
        Assert.That(layout.LaneAssignments["issue-1"], Is.EqualTo(1));
        Assert.That(layout.LaneAssignments["issue-2"], Is.EqualTo(1));
        Assert.That(layout.LaneAssignments["issue-3"], Is.EqualTo(1));
    }

    #endregion

    #region Helper Methods

    private static TestGraphNode CreateNode(
        string id,
        string branchName,
        List<string>? parentIds = null)
    {
        return new TestGraphNode
        {
            Id = id,
            BranchName = branchName,
            ParentIds = parentIds ?? []
        };
    }

    /// <summary>
    /// Test implementation of IGraphNode for testing purposes.
    /// </summary>
    private class TestGraphNode : IGraphNode
    {
        public required string Id { get; init; }
        public string Title => $"Test Node {Id}";
        public GraphNodeType NodeType => Id.StartsWith("issue-") ? GraphNodeType.Issue : GraphNodeType.MergedPullRequest;
        public GraphNodeStatus Status => GraphNodeStatus.Completed;
        public IReadOnlyList<string> ParentIds { get; init; } = [];
        public required string BranchName { get; init; }
        public DateTime SortDate => DateTime.UtcNow;
        public int TimeDimension => 0;
        public string? Url => null;
        public string? Color => "#6b7280";
        public string? Tag => null;
        public int? PullRequestNumber => Id.StartsWith("pr-") ? int.Parse(Id.Replace("pr-", "")) : null;
        public string? IssueId => Id.StartsWith("issue-") ? Id.Replace("issue-", "") : null;
    }

    #endregion
}
