using Fleece.Core.Models;
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
    /// Uses Issue.ParentIssues for dependency information.
    /// </summary>
    /// <param name="pullRequests">All pull requests to include in the graph.</param>
    /// <param name="issues">All issues to include in the graph.</param>
    /// <param name="maxPastPRs">Maximum number of past (closed/merged) PRs to show. If null, shows all.</param>
    /// <param name="issuePrStatuses">Optional dictionary mapping issue IDs to their linked PR statuses.</param>
    public Graph Build(
        IEnumerable<PullRequestInfo> pullRequests,
        IEnumerable<Issue> issues,
        int? maxPastPRs = null,
        IReadOnlyDictionary<string, PullRequestStatus>? issuePrStatuses = null)
    {
        var nodes = new List<IGraphNode>();
        var branches = new Dictionary<string, GraphBranch>();

        var prList = pullRequests.ToList();
        var issueList = issues.ToList();
        var prStatusLookup = issuePrStatuses ?? new Dictionary<string, PullRequestStatus>();

        // Build dependency lookup from ParentIssues (issue -> list of issues that block it)
        var blockingDependencies = BuildDependencyLookup(issueList);

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
        AddIssuesDepthFirst(rootIssues, dependentIssues, blockingDependencies, nodes, branches, closedPrNodes.LastOrDefault(), prStatusLookup);

        // Phase 4: Add orphan issues in a chain
        AddOrphanIssues(orphanIssues, nodes, branches, closedPrNodes.LastOrDefault(), prStatusLookup);

        return new Graph(nodes, branches, _mainBranchName, hasMorePastPRs, closedPrNodes.Count);
    }

    /// <summary>
    /// Builds a lookup from issue ID to list of issue IDs that block it.
    /// Uses Issue.ParentIssues property.
    /// </summary>
    private static Dictionary<string, List<string>> BuildDependencyLookup(List<Issue> issues)
    {
        var lookup = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var issueIds = issues.Select(i => i.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var issue in issues)
        {
            if (issue.ParentIssues.Count > 0)
            {
                // Filter to only parent issues that exist in our issue list
                var validParents = issue.ParentIssues
                    .Where(p => issueIds.Contains(p))
                    .ToList();

                if (validParents.Count > 0)
                {
                    lookup[issue.Id] = validParents;
                }
            }
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
    private static (List<Issue> roots, List<Issue> orphans, List<Issue> dependent) ClassifyIssues(
        List<Issue> issues,
        Dictionary<string, List<string>> blockingDependencies)
    {
        var issueIds = issues.Select(i => i.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hasBlockers = blockingDependencies.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Build reverse lookup: what issues does this issue block?
        var blocksLookup = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
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

        var isBlockingSomething = blocksLookup.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Root issues: either block something and have no blockers, or have blockers outside the issue set
        var roots = new List<Issue>();
        var dependent = new List<Issue>();
        var orphans = new List<Issue>();

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
        List<Issue> rootIssues,
        List<Issue> dependentIssues,
        Dictionary<string, List<string>> blockingDependencies,
        List<IGraphNode> nodes,
        Dictionary<string, GraphBranch> branches,
        PullRequestNode? latestMergedPr,
        IReadOnlyDictionary<string, PullRequestStatus> prStatusLookup)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allIssues = rootIssues.Concat(dependentIssues).ToDictionary(i => i.Id, StringComparer.OrdinalIgnoreCase);

        // Build reverse lookup: what issues does this issue block?
        var blocksLookup = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
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

        void VisitIssue(Issue issue, int depth)
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
            // Check if this issue has a linked PR status
            prStatusLookup.TryGetValue(issue.Id, out var issuePrStatus);
            var branchColor = issuePrStatus != default ? GetPrStatusColor(issuePrStatus) : GetIssueTypeColor(issue.Type);

            branches[branchName] = new GraphBranch
            {
                Name = branchName,
                Color = branchColor,
                ParentBranch = parentIds.Count > 0 && parentIds[0].StartsWith("issue-")
                    ? parentIds[0].Replace("issue-", "issue-")
                    : _mainBranchName,
                ParentCommitId = parentIds.FirstOrDefault()
            };

            var node = new IssueNode(issue, parentIds, currentTimeDimension + depth, isOrphan: false, prStatus: issuePrStatus != default ? issuePrStatus : null);
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
    /// Adds orphan issues (no dependencies) grouped by their group property.
    /// Each group gets its own branch, and groups are sorted alphabetically.
    /// </summary>
    private void AddOrphanIssues(
        List<Issue> orphanIssues,
        List<IGraphNode> nodes,
        Dictionary<string, GraphBranch> branches,
        PullRequestNode? latestMergedPr,
        IReadOnlyDictionary<string, PullRequestStatus> prStatusLookup)
    {
        if (orphanIssues.Count == 0) return;

        // Group orphan issues by their Group property
        var issuesByGroup = new Dictionary<string, List<Issue>>(StringComparer.OrdinalIgnoreCase);
        var issuesWithoutGroup = new List<Issue>();

        foreach (var issue in orphanIssues)
        {
            if (!string.IsNullOrWhiteSpace(issue.Group))
            {
                var group = issue.Group;
                if (!issuesByGroup.TryGetValue(group, out var groupIssues))
                {
                    groupIssues = [];
                    issuesByGroup[group] = groupIssues;
                }
                groupIssues.Add(issue);
            }
            else
            {
                issuesWithoutGroup.Add(issue);
            }
        }

        // Sort groups alphabetically
        var sortedGroups = issuesByGroup.Keys.OrderBy(g => g, StringComparer.OrdinalIgnoreCase).ToList();

        var timeDimension = 2;

        // Process each group separately
        foreach (var group in sortedGroups)
        {
            var groupIssues = issuesByGroup[group];

            // Create a branch for this group
            var orphanBranchName = $"orphan-issues-{group}";
            branches[orphanBranchName] = new GraphBranch
            {
                Name = orphanBranchName,
                Color = "#6b7280",  // Gray
                ParentBranch = _mainBranchName,
                ParentCommitId = latestMergedPr?.Id
            };

            // Sort issues within group by priority then creation date
            var sortedGroupIssues = groupIssues
                .OrderBy(i => i.Priority ?? int.MaxValue)
                .ThenBy(i => i.CreatedAt)
                .ToList();

            string? previousId = latestMergedPr?.Id;

            foreach (var issue in sortedGroupIssues)
            {
                var parentIds = previousId != null
                    ? new List<string> { previousId }
                    : new List<string>();

                prStatusLookup.TryGetValue(issue.Id, out var issuePrStatus);
                var node = new IssueNode(issue, parentIds, timeDimension, isOrphan: true, customBranchName: orphanBranchName, prStatus: issuePrStatus != default ? issuePrStatus : null);
                nodes.Add(node);

                previousId = node.Id;
            }

            timeDimension++;
        }

        // Handle issues without a group (fallback to original behavior)
        if (issuesWithoutGroup.Count > 0)
        {
            const string orphanBranchName = "orphan-issues";
            branches[orphanBranchName] = new GraphBranch
            {
                Name = orphanBranchName,
                Color = "#6b7280",  // Gray
                ParentBranch = _mainBranchName,
                ParentCommitId = latestMergedPr?.Id
            };

            // Sort by priority then creation date
            var sortedOrphans = issuesWithoutGroup
                .OrderBy(i => i.Priority ?? int.MaxValue)
                .ThenBy(i => i.CreatedAt)
                .ToList();

            string? previousId = latestMergedPr?.Id;

            foreach (var issue in sortedOrphans)
            {
                var parentIds = previousId != null
                    ? new List<string> { previousId }
                    : new List<string>();

                prStatusLookup.TryGetValue(issue.Id, out var issuePrStatus);
                var node = new IssueNode(issue, parentIds, timeDimension, isOrphan: true, prStatus: issuePrStatus != default ? issuePrStatus : null);
                nodes.Add(node);

                previousId = node.Id;
            }
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

    private static string GetIssueTypeColor(IssueType type) => type switch
    {
        IssueType.Bug => "#ef4444",      // Red
        IssueType.Feature => "#a855f7",  // Purple
        IssueType.Task => "#3b82f6",     // Blue
        IssueType.Chore => "#6b7280",    // Gray
        _ => "#6b7280"                   // Gray
    };
}
