# Gitgraph Integration

This folder contains the vendored Gitgraph.js library and its integration with Homespun for visualizing Pull Requests and Issues as a Git-like DAG (directed acyclic graph).

## Architecture Overview

The graph visualization consists of:

1. **Data Layer** (`Features/Gitgraph/Data/`)
   - `IGraphNode` - Common interface for all graph nodes
   - `PullRequestNode` - Adapts `PullRequestInfo` to `IGraphNode`
   - `BeadsIssueNode` - Adapts `BeadsIssue` to `IGraphNode`
   - `Graph` - Container with ordered nodes and branch metadata
   - `GraphBranch` - Branch information (name, color, parent)
   - `GraphNodeEnums` - Node types and statuses

2. **Service Layer** (`Features/Gitgraph/Services/`)
   - `GraphBuilder` - Ordering logic for nodes (TDD with 21 tests)
   - `GraphService` - Fetches data and builds the graph
   - `GitgraphApiMapper` - Converts Graph to JSON for JS visualization

3. **UI Layer** (`Components/Shared/`)
   - `GitgraphVisualization.razor` - Blazor component wrapper
   - `GitgraphVisualization.razor.js` - JS module using Gitgraph.js

## Node Ordering Rules

The `GraphBuilder` implements these ordering rules (see `GraphBuilderTests.cs`):

1. **Closed/Merged PRs first** - Ordered by close/merge date, oldest first
2. **Open PRs next** - Ordered by creation date, branching based on target
3. **Issues via DFS** - Depth-first traversal respecting dependencies
4. **Orphan issues last** - Chained off main branch, oldest to newest

## Node Styling

| Node Type | Shape | Default Color |
|-----------|-------|---------------|
| Pull Request | Circle | Based on status |
| Issue | Diamond | Based on type/status |

### Status Colors (from CSS variables)

| Status | CSS Variable |
|--------|--------------|
| Merged PR | `--status-merged` |
| Open PR | `--color-ocean` |
| Closed PR | `--text-muted` |
| In-progress Issue | `--color-lagoon` |
| Open Issue | `--text-secondary` |
| Blocked Issue | `--status-conflict` |

## Gitgraph.js Usage

The integration uses Gitgraph.js loaded from CDN:
```
https://cdn.jsdelivr.net/npm/@gitgraph/js@1.4.0/lib/gitgraph.umd.min.js
```

### Known Issue: Hash Length

GitgraphJS has a rendering bug with long hash values. When hashes like `issue-hsp-xxx` are used, the text positioning breaks and appears misaligned. Short hashes like `pr-35` or sequential numbers work correctly.

**Workaround**: The `GitgraphApiMapper` converts issue IDs to sequential numbers (starting at 100) for the hash field. The original issue ID is preserved in the `IssueId` property for click handling.

### Custom Rendering

The JS module (`GitgraphVisualization.razor.js`) provides custom `renderDot` functions:

- **PRs**: Standard circles with click handlers and tooltips
- **Issues**: Diamond shapes via custom SVG path with tooltips

```javascript
// Diamond path for issues
const d = `M ${size} 0 L ${size * 2} ${size} L ${size} ${size * 2} L 0 ${size} Z`;

// Text alignment fix for vertical positioning
text.setAttribute('dominant-baseline', 'middle');
text.setAttribute('dy', '0.35em');
```

### Template Settings

```javascript
branch: { lineWidth: 1.5, spacing: 15 }
commit: { spacing: 25, dot: { size: 5 } }
```

## Vendored Library

The `packages/` folder contains the vendored Gitgraph.js source. However, due to outdated dependencies, we load the library from CDN rather than building locally.

### If Building Locally is Needed

1. Update dependencies in `packages/gitgraph-js/package.json`
2. Run `npm install` and `npm run build`
3. Copy output to `wwwroot/lib/gitgraph/`
4. Update the JS module to load from local file instead of CDN

## Integration with ProjectDetail

The `GitgraphVisualization` component replaced the old timeline in `ProjectDetail.razor`:

```razor
<GitgraphVisualization
    ProjectId="@Id"
    OnPullRequestClick="@SelectPullRequestByNumber"
    OnIssueClick="@SelectBeadsIssue"
    SelectedNodeId="@GetSelectedNodeId()" />
```

### Event Handling

- **PR click**: Invokes `OnPullRequestClick` with PR number (int)
- **Issue click**: Invokes `OnIssueClick` with issue ID (string)
- **Selection**: Pass `SelectedNodeId` as `pr-{number}` or `issue-{id}`

## Testing

GraphBuilder has comprehensive TDD tests in:
```
tests/Homespun.Tests/Features/Gitgraph/GraphBuilderTests.cs
```

Run tests: `dotnet test --filter "FullyQualifiedName~Gitgraph"`

### Manual Test Page

A standalone HTML/JS test page exists for testing the visualization:
```
tests/gitgraph/index.html
tests/gitgraph/gitgraph-test.js
```

Open `index.html` directly in a browser to test GitgraphJS rendering with example data from the Homespun repository.
