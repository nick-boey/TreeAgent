using Fleece.Core.Models;
using Fleece.Core.Search;
using Fleece.Core.Services;
using Fleece.Core.Services.Interfaces;

namespace Homespun.Features.Testing.Services;

/// <summary>
/// Adapter that wraps a list of in-memory issues into a Fleece.Core v2 IFleeceService.
/// Uses FleeceService.ForFile with a temp directory seeded with the provided issues.
/// </summary>
internal class MockIssueServiceAdapter : IFleeceService
{
    private readonly IFleeceService _innerService;

    public MockIssueServiceAdapter(IReadOnlyList<Issue> issues)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"fleece-mock-{Guid.NewGuid():N}");
        var fleeceDir = Path.Combine(tempDir, ".fleece");
        Directory.CreateDirectory(fleeceDir);

        // Use stable filename — hash-patterned names (issues_*.jsonl) trigger
        // internal rename-on-save behavior that breaks subsequent reads
        var filePath = Path.Combine(fleeceDir, "issues.jsonl");
        if (issues.Count > 0)
        {
            var jsonOptions = new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            };
            var lines = issues.Select(i => System.Text.Json.JsonSerializer.Serialize(i, jsonOptions));
            File.WriteAllLines(filePath, lines);
        }
        else
        {
            File.WriteAllText(filePath, "");
        }

        var settingsService = new SettingsService(tempDir);
        var gitConfigService = new GitConfigService(settingsService);
        _innerService = FleeceService.ForFile(filePath, settingsService, gitConfigService);
    }

    // All methods delegate to _innerService
    public Task<Issue> CreateAsync(string title, IssueType type, string? description = null,
        IssueStatus status = IssueStatus.Open, int? priority = null, int? linkedPr = null,
        IReadOnlyList<string>? linkedIssues = null, IReadOnlyList<ParentIssueRef>? parentIssues = null,
        string? assignedTo = null, IReadOnlyList<string>? tags = null, string? workingBranchId = null,
        ExecutionMode? executionMode = null, CancellationToken cancellationToken = default)
        => _innerService.CreateAsync(title, type, description, status, priority, linkedPr, linkedIssues, parentIssues, assignedTo, tags, workingBranchId, executionMode, cancellationToken);

    public Task<Issue> UpdateAsync(string id, string? title = null, string? description = null,
        IssueStatus? status = null, IssueType? type = null, int? priority = null, int? linkedPr = null,
        IReadOnlyList<string>? linkedIssues = null, IReadOnlyList<ParentIssueRef>? parentIssues = null,
        string? assignedTo = null, IReadOnlyList<string>? tags = null, string? workingBranchId = null,
        ExecutionMode? executionMode = null, CancellationToken cancellationToken = default)
        => _innerService.UpdateAsync(id, title, description, status, type, priority, linkedPr, linkedIssues, parentIssues, assignedTo, tags, workingBranchId, executionMode, cancellationToken);

    public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
        => _innerService.DeleteAsync(id, cancellationToken);

    public Task<Issue?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
        => _innerService.GetByIdAsync(id, cancellationToken);

    public Task<IReadOnlyList<Issue>> GetAllAsync(CancellationToken cancellationToken = default)
        => _innerService.GetAllAsync(cancellationToken);

    public Task<IReadOnlyList<Issue>> ResolveByPartialIdAsync(string partialId, CancellationToken cancellationToken = default)
        => _innerService.ResolveByPartialIdAsync(partialId, cancellationToken);

    public Task<IReadOnlyList<Issue>> FilterAsync(IssueStatus? status = null, IssueType? type = null,
        int? priority = null, string? assignedTo = null, IReadOnlyList<string>? tags = null,
        int? linkedPr = null, bool includeTerminal = false, CancellationToken cancellationToken = default)
        => _innerService.FilterAsync(status, type, priority, assignedTo, tags, linkedPr, includeTerminal, cancellationToken);

    public Task<IReadOnlyList<Issue>> SearchAsync(string query, CancellationToken cancellationToken = default)
        => _innerService.SearchAsync(query, cancellationToken);

    public SearchQuery ParseSearchQuery(string? query)
        => _innerService.ParseSearchQuery(query);

    public Task<IReadOnlyList<Issue>> SearchWithFiltersAsync(SearchQuery query, IssueStatus? status = null,
        IssueType? type = null, int? priority = null, string? assignedTo = null, IReadOnlyList<string>? tags = null,
        int? linkedPr = null, bool includeTerminal = false, CancellationToken cancellationToken = default)
        => _innerService.SearchWithFiltersAsync(query, status, type, priority, assignedTo, tags, linkedPr, includeTerminal, cancellationToken);

    public Task<SearchResult> SearchWithContextAsync(SearchQuery query, IssueStatus? status = null,
        IssueType? type = null, int? priority = null, string? assignedTo = null, IReadOnlyList<string>? tags = null,
        int? linkedPr = null, bool includeTerminal = false, CancellationToken cancellationToken = default)
        => _innerService.SearchWithContextAsync(query, status, type, priority, assignedTo, tags, linkedPr, includeTerminal, cancellationToken);

    public Task<IssueGraph> BuildGraphAsync(CancellationToken cancellationToken = default)
        => _innerService.BuildGraphAsync(cancellationToken);

    public Task<IssueGraph> QueryGraphAsync(GraphQuery query, GraphSortConfig? sortConfig = null, CancellationToken cancellationToken = default)
        => _innerService.QueryGraphAsync(query, sortConfig, cancellationToken);

    public Task<IReadOnlyList<Issue>> GetNextIssuesAsync(string? parentId = null, GraphSortConfig? sortConfig = null, CancellationToken cancellationToken = default)
        => _innerService.GetNextIssuesAsync(parentId, sortConfig, cancellationToken);

    public Task<IReadOnlyList<Issue>> GetIssueHierarchyAsync(string issueId, bool includeParents = true,
        bool includeChildren = true, CancellationToken cancellationToken = default)
        => _innerService.GetIssueHierarchyAsync(issueId, includeParents, includeChildren, cancellationToken);

    public Task<Issue> AddDependencyAsync(string parentId, string childId, DependencyPosition? position = null,
        bool replaceExisting = false, bool makePrimary = false, CancellationToken cancellationToken = default)
        => _innerService.AddDependencyAsync(parentId, childId, position, replaceExisting, makePrimary, cancellationToken);

    public Task<Issue> RemoveDependencyAsync(string parentId, string childId, CancellationToken cancellationToken = default)
        => _innerService.RemoveDependencyAsync(parentId, childId, cancellationToken);

    public Task<MoveResult> MoveUpAsync(string parentId, string childId, CancellationToken cancellationToken = default)
        => _innerService.MoveUpAsync(parentId, childId, cancellationToken);

    public Task<MoveResult> MoveDownAsync(string parentId, string childId, CancellationToken cancellationToken = default)
        => _innerService.MoveDownAsync(parentId, childId, cancellationToken);

    public Task<DependencyValidationResult> ValidateDependenciesAsync(CancellationToken cancellationToken = default)
        => _innerService.ValidateDependenciesAsync(cancellationToken);

    public Task<int> MergeAsync(bool dryRun = false, CancellationToken cancellationToken = default)
        => _innerService.MergeAsync(dryRun, cancellationToken);

    public Task<(bool HasMultiple, string Message)> HasMultipleUnmergedFilesAsync(CancellationToken cancellationToken = default)
        => _innerService.HasMultipleUnmergedFilesAsync(cancellationToken);

    public Task<CleanResult> CleanAsync(bool includeComplete = true, bool includeClosed = true,
        bool includeArchived = true, bool stripReferences = true, bool dryRun = false, CancellationToken cancellationToken = default)
        => _innerService.CleanAsync(includeComplete, includeClosed, includeArchived, stripReferences, dryRun, cancellationToken);

    public Task<LoadIssuesResult> LoadIssuesWithDiagnosticsAsync(CancellationToken cancellationToken = default)
        => _innerService.LoadIssuesWithDiagnosticsAsync(cancellationToken);

    public Task<IReadOnlyDictionary<string, SyncStatus>> GetSyncStatusesAsync(CancellationToken cancellationToken = default)
        => _innerService.GetSyncStatusesAsync(cancellationToken);
}
