using Homespun.Features.Beads.Data;
using Homespun.Features.Gitgraph.Data;
using Homespun.Features.PullRequests;

namespace Homespun.Features.Gitgraph.Services;

/// <summary>
/// Builds a Graph following the specified ordering rules:
/// 1. Closed/merged PRs first (ordered by close/merge date, oldest first)
/// 2. Open PRs next (branching based on target branch, ordered by open date)
/// 3. Issues last (branching from latest merged PR, with root issues found via DFS)
/// 4. Orphan issues at the end in a chain off main
/// </summary>
public class GraphBuilder
{
    private readonly string _mainBranchName;

    public GraphBuilder(string mainBranchName = "main")
    {
        _mainBranchName = mainBranchName;
    }

    /// <summary>
    /// Builds a graph from PRs and issues following the ordering rules.
    /// </summary>
    /// <param name="pullRequests">All pull requests to include in the graph.</param>
    /// <param name="issues">All issues to include in the graph.</param>
    /// <param name="dependencies">All dependencies between issues.</param>
    /// <param name="maxPastPRs">Maximum number of past (closed/merged) PRs to show. If null, shows all.</param>
    public Graph Build(
        IEnumerable<PullRequestInfo> pullRequests,
        IEnumerable<BeadsIssue> issues,
        IEnumerable<BeadsDependency> dependencies,
        int? maxPastPRs = null)
    {
        var nodes = new List<IGraphNode>();
        var branches = new Dictionary<string, GraphBranch>();

        var prList = pullRequests.ToList();
        var issueList = issues.ToList();
        var depList = dependencies.ToList();

        // Build dependency lookup (issue -> list of issues that block it)
        var blockingDependencies = BuildDependencyLookup(depList);

        // Add main branch
        branches[_mainBranchName] = new GraphBranch
        {
            Name = _mainBranchName,
            Color = "#6b7280"  // Gray
        };

        // Phase 1: Add closed/merged PRs (oldest first by merge/close date)
        var (closedPrNodes, totalPastPRs, hasMorePastPRs) = AddClosedPullRequests(prList, nodes, branches, maxPastPRs);

        // Phase 2: Add open PRs (ordered by created date)
        AddOpenPullRequests(prList, nodes, branches, closedPrNodes.LastOrDefault());

        // Phase 3: Add issues with dependencies (depth-first from roots)
        var (rootIssues, orphanIssues, dependentIssues) = ClassifyIssues(issueList, blockingDependencies);
        AddIssuesDepthFirst(rootIssues, dependentIssues, blockingDependencies, nodes, branches, closedPrNodes.LastOrDefault());

        // Phase 4: Add orphan issues in a chain
        AddOrphanIssues(orphanIssues, nodes, branches, closedPrNodes.LastOrDefault());

        return new Graph(nodes, branches, _mainBranchName, hasMorePastPRs, closedPrNodes.Count);
    }

    /// <summary>
    /// Builds a lookup from issue ID to list of issue IDs that block it.
    /// </summary>
    private static Dictionary<string, List<string>> BuildDependencyLookup(List<BeadsDependency> dependencies)
    {
        var lookup = new Dictionary<string, List<string>>();

        foreach (var dep in dependencies.Where(d => d.Type == BeadsDependencyType.Blocks))
        {
            // FromIssueId is blocked BY ToIssueId
            if (!lookup.TryGetValue(dep.FromIssueId, out var blockers))
            {
                blockers = [];
                lookup[dep.FromIssueId] = blockers;
            }
            blockers.Add(dep.ToIssueId);
        }

        return lookup;
    }

    /// <summary>
    /// Adds closed/merged PRs to the graph in chronological order.
    /// </summary>
    /// <returns>Tuple of (nodes added, total past PRs available, has more PRs to load)</returns>
    private (List<PullRequestNode> nodes, int totalPastPRs, bool hasMorePastPRs) AddClosedPullRequests(
        List<PullRequestInfo> prs,
        List<IGraphNode> nodes,
        Dictionary<string, GraphBranch> branches,
        int? maxPastPRs)
    {
        var allClosedPrs = prs
            .Where(pr => pr.Status == PullRequestStatus.Merged || pr.Status == PullRequestStatus.Closed)
            .OrderBy(pr => GetCloseDate(pr))
            .ToList();

        var totalPastPRs = allClosedPrs.Count;

        // Apply limit if specified, taking the most recent ones (from the end of the ordered list)
        var closedPrs = maxPastPRs.HasValue && maxPastPRs.Value < allClosedPrs.Count
            ? allClosedPrs.Skip(allClosedPrs.Count - maxPastPRs.Value).ToList()
            : allClosedPrs;

        var hasMorePastPRs = maxPastPRs.HasValue && allClosedPrs.Count > maxPastPRs.Value;

        var resultNodes = new List<PullRequestNode>();

        // Calculate time dimensions (negative for past, most recent = 0)
        var timeDimension = -closedPrs.Count + 1;

        foreach (var pr in closedPrs)
        {
            var node = new PullRequestNode(pr, timeDimension++);
            nodes.Add(node);
            resultNodes.Add(node);
        }

        return (resultNodes, totalPastPRs, hasMorePastPRs);
    }

    /// <summary>
    /// Adds open PRs to the graph.
    /// </summary>
    private void AddOpenPullRequests(
        List<PullRequestInfo> prs,
        List<IGraphNode> nodes,
        Dictionary<string, GraphBranch> branches,
        PullRequestNode? latestMergedPr)
    {
        var openPrs = prs
            .Where(pr => pr.Status != PullRequestStatus.Merged && pr.Status != PullRequestStatus.Closed)
            .OrderBy(pr => pr.CreatedAt)
            .ToList();

        foreach (var pr in openPrs)
        {
            var branchName = pr.BranchName ?? $"pr-{pr.Number}";

            if (!branches.ContainsKey(branchName))
            {
                branches[branchName] = new GraphBranch
                {
                    Name = branchName,
                    Color = GetPrStatusColor(pr.Status),
                    ParentBranch = _mainBranchName,
                    ParentCommitId = latestMergedPr?.Id
                };
            }

            var parentIds = latestMergedPr != null
                ? new List<string> { latestMergedPr.Id }
                : new List<string>();

            var node = new PullRequestNode(pr, timeDimension: 1, parentIds);
            nodes.Add(node);
        }
    }

    /// <summary>
    /// Classifies issues into root issues (have children but no parents),
    /// orphan issues (no dependencies), and dependent issues (have parents).
    /// </summary>
    private static (List<BeadsIssue> roots, List<BeadsIssue> orphans, List<BeadsIssue> dependent) ClassifyIssues(
        List<BeadsIssue> issues,
        Dictionary<string, List<string>> blockingDependencies)
    {
        var issueIds = issues.Select(i => i.Id).ToHashSet();
        var hasBlockers = blockingDependencies.Keys.ToHashSet();

        // Build reverse lookup: what issues does this issue block?
        var blocksLookup = new Dictionary<string, HashSet<string>>();
        foreach (var (blockedId, blockerIds) in blockingDependencies)
        {
            foreach (var blockerId in blockerIds.Where(id => issueIds.Contains(id)))
            {
                if (!blocksLookup.TryGetValue(blockerId, out var blocked))
                {
                    blocked = [];
                    blocksLookup[blockerId] = blocked;
                }
                blocked.Add(blockedId);
            }
        }

        var isBlockingSomething = blocksLookup.Keys.ToHashSet();

        // Root issues: either block something and have no blockers, or have blockers outside the issue set
        var roots = new List<BeadsIssue>();
        var dependent = new List<BeadsIssue>();
        var orphans = new List<BeadsIssue>();

        foreach (var issue in issues)
        {
            var blocksOthers = isBlockingSomething.Contains(issue.Id);
            var hasParentsInSet = hasBlockers.Contains(issue.Id) &&
                                  blockingDependencies[issue.Id].Any(b => issueIds.Contains(b));

            if (!blocksOthers && !hasParentsInSet)
            {
                // No dependencies at all - orphan
                orphans.Add(issue);
            }
            else if (blocksOthers && !hasParentsInSet)
            {
                // Blocks others but has no parents - root
                roots.Add(issue);
            }
            else
            {
                // Has parents in the set - dependent
                dependent.Add(issue);
            }
        }

        return (roots, orphans, dependent);
    }

    /// <summary>
    /// Adds issues with dependencies using depth-first traversal.
    /// </summary>
    private void AddIssuesDepthFirst(
        List<BeadsIssue> rootIssues,
        List<BeadsIssue> dependentIssues,
        Dictionary<string, List<string>> blockingDependencies,
        List<IGraphNode> nodes,
        Dictionary<string, GraphBranch> branches,
        PullRequestNode? latestMergedPr)
    {
        var visited = new HashSet<string>();
        var allIssues = rootIssues.Concat(dependentIssues).ToDictionary(i => i.Id);

        // Build reverse lookup: what issues does this issue block?
        var blocksLookup = new Dictionary<string, List<string>>();
        foreach (var (blockedId, blockerIds) in blockingDependencies)
        {
            foreach (var blockerId in blockerIds.Where(id => allIssues.ContainsKey(id)))
            {
                if (!blocksLookup.TryGetValue(blockerId, out var blocked))
                {
                    blocked = [];
                    blocksLookup[blockerId] = blocked;
                }
                blocked.Add(blockedId);
            }
        }

        var currentTimeDimension = 2;

        void VisitIssue(BeadsIssue issue, int depth)
        {
            if (visited.Contains(issue.Id)) return;
            visited.Add(issue.Id);

            // Calculate parent IDs
            List<string> parentIds;
            if (blockingDependencies.TryGetValue(issue.Id, out var blockerIds))
            {
                parentIds = blockerIds
                    .Where(id => allIssues.ContainsKey(id))
                    .Select(id => $"issue-{id}")
                    .ToList();
            }
            else
            {
                parentIds = latestMergedPr != null
                    ? [latestMergedPr.Id]
                    : [];
            }

            var branchName = $"issue-{issue.Id}";
            branches[branchName] = new GraphBranch
            {
                Name = branchName,
                Color = GetIssueTypeColor(issue.Type),
                ParentBranch = parentIds.Count > 0 && parentIds[0].StartsWith("issue-")
                    ? parentIds[0].Replace("issue-", "issue-")
                    : _mainBranchName,
                ParentCommitId = parentIds.FirstOrDefault()
            };

            var node = new BeadsIssueNode(issue, parentIds, currentTimeDimension + depth, isOrphan: false);
            nodes.Add(node);

            // Visit children (issues that this issue blocks)
            if (blocksLookup.TryGetValue(issue.Id, out var blockedIssueIds))
            {
                foreach (var blockedId in blockedIssueIds)
                {
                    if (allIssues.TryGetValue(blockedId, out var blockedIssue))
                    {
                        // Only visit if all blockers have been visited
                        var allBlockersVisited = true;
                        if (blockingDependencies.TryGetValue(blockedId, out var allBlockers))
                        {
                            allBlockersVisited = allBlockers
                                .Where(id => allIssues.ContainsKey(id))
                                .All(id => visited.Contains(id));
                        }

                        if (allBlockersVisited)
                        {
                            VisitIssue(blockedIssue, depth + 1);
                        }
                    }
                }
            }
        }

        // Sort roots by priority then creation date
        var sortedRoots = rootIssues
            .OrderBy(i => i.Priority ?? int.MaxValue)
            .ThenBy(i => i.CreatedAt)
            .ToList();

        foreach (var root in sortedRoots)
        {
            VisitIssue(root, 0);
        }

        // Handle any dependent issues that weren't visited (due to complex dependency graphs)
        foreach (var issue in dependentIssues.Where(i => !visited.Contains(i.Id)))
        {
            VisitIssue(issue, 0);
        }
    }

    /// <summary>
    /// Adds orphan issues (no dependencies) in a chain.
    /// </summary>
    private void AddOrphanIssues(
        List<BeadsIssue> orphanIssues,
        List<IGraphNode> nodes,
        Dictionary<string, GraphBranch> branches,
        PullRequestNode? latestMergedPr)
    {
        if (orphanIssues.Count == 0) return;

        // Create orphan branch
        const string orphanBranchName = "orphan-issues";
        branches[orphanBranchName] = new GraphBranch
        {
            Name = orphanBranchName,
            Color = "#6b7280",  // Gray
            ParentBranch = _mainBranchName,
            ParentCommitId = latestMergedPr?.Id
        };

        // Sort by priority then creation date
        var sortedOrphans = orphanIssues
            .OrderBy(i => i.Priority ?? int.MaxValue)
            .ThenBy(i => i.CreatedAt)
            .ToList();

        string? previousId = latestMergedPr?.Id;
        var timeDimension = 2;

        foreach (var issue in sortedOrphans)
        {
            var parentIds = previousId != null
                ? new List<string> { previousId }
                : new List<string>();

            var node = new BeadsIssueNode(issue, parentIds, timeDimension++, isOrphan: true);
            nodes.Add(node);

            previousId = node.Id;
        }
    }

    private static DateTime GetCloseDate(PullRequestInfo pr) => pr.Status switch
    {
        PullRequestStatus.Merged => pr.MergedAt ?? pr.UpdatedAt,
        PullRequestStatus.Closed => pr.ClosedAt ?? pr.UpdatedAt,
        _ => pr.UpdatedAt
    };

    private static string GetPrStatusColor(PullRequestStatus status) => status switch
    {
        PullRequestStatus.InProgress => "#3b82f6",     // Blue
        PullRequestStatus.ReadyForReview => "#eab308", // Yellow
        PullRequestStatus.ReadyForMerging => "#22c55e", // Green
        PullRequestStatus.ChecksFailing => "#ef4444",  // Red
        PullRequestStatus.Conflict => "#f97316",       // Orange
        _ => "#6b7280"                                 // Gray
    };

    private static string GetIssueTypeColor(BeadsIssueType type) => type switch
    {
        BeadsIssueType.Bug => "#ef4444",      // Red
        BeadsIssueType.Feature => "#a855f7",  // Purple
        BeadsIssueType.Task => "#3b82f6",     // Blue
        BeadsIssueType.Epic => "#f97316",     // Orange
        BeadsIssueType.Chore => "#6b7280",    // Gray
        _ => "#6b7280"                        // Gray
    };
}
