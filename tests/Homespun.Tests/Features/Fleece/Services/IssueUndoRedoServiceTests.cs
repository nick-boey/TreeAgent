using Fleece.Core.Models;
using Homespun.Features.Fleece.Services;
using Homespun.Shared.Requests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Homespun.Tests.Features.Fleece.Services;

/// <summary>
/// Unit and integration tests for the in-memory per-project undo/redo stack
/// service. Uses a real <see cref="ProjectFleeceService"/> backed by a temp
/// directory so the inverse-event appends actually land in
/// <c>.fleece/changes/</c> and the cache + snapshot are mutated for real.
/// </summary>
[TestFixture]
public sealed class IssueUndoRedoServiceTests
{
    private string _tempDir = null!;
    private ServiceProvider _serviceProvider = null!;
    private ProjectFleeceService _fleeceService = null!;
    private IIssueUndoRedoService _undoRedo = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"fleece-undo-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var queueMock = new Mock<IIssueSerializationQueue>();
        queueMock
            .Setup(q => q.EnqueueAsync(It.IsAny<IssueWriteOperation>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton<IIssueSerializationQueue>(queueMock.Object);
        services.AddSingleton<global::Fleece.Core.Services.Interfaces.IGraphLayoutService,
            global::Fleece.Core.Services.GraphLayout.GraphLayoutService>();
        services.AddSingleton<global::Fleece.Core.Services.Interfaces.IIssueLayoutService,
            global::Fleece.Core.Services.GraphLayout.IssueLayoutService>();
        services.AddSingleton<ILogger<ProjectFleeceService>>(NullLogger<ProjectFleeceService>.Instance);
        services.AddSingleton<ILogger<IssueUndoRedoService>>(NullLogger<IssueUndoRedoService>.Instance);
        services.AddSingleton<IProjectFleeceService, ProjectFleeceService>();
        services.AddSingleton<IIssueUndoRedoService, IssueUndoRedoService>();
        _serviceProvider = services.BuildServiceProvider();

        _fleeceService = (ProjectFleeceService)_serviceProvider.GetRequiredService<IProjectFleeceService>();
        _undoRedo = _serviceProvider.GetRequiredService<IIssueUndoRedoService>();
    }

    [TearDown]
    public void TearDown()
    {
        _fleeceService.Dispose();
        _serviceProvider.Dispose();
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Test]
    public async Task GetStateAsync_EmptyStacks_ReportsAllZero()
    {
        var state = await _undoRedo.GetStateAsync(_tempDir);

        Assert.That(state.CanUndo, Is.False);
        Assert.That(state.CanRedo, Is.False);
        Assert.That(state.UndoCount, Is.EqualTo(0));
        Assert.That(state.RedoCount, Is.EqualTo(0));
    }

    [Test]
    public async Task UndoAsync_EmptyStack_ReturnsFalse()
    {
        var result = await _undoRedo.UndoAsync(_tempDir, CancellationToken.None);
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task RedoAsync_EmptyStack_ReturnsFalse()
    {
        var result = await _undoRedo.RedoAsync(_tempDir, CancellationToken.None);
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task UpdateThenUndo_RestoresPriorStatus()
    {
        var issue = await _fleeceService.CreateIssueAsync(_tempDir, "Issue A", IssueType.Task, recordUndo: false);

        // User-driven update: status Open → Progress
        await _fleeceService.UpdateIssueAsync(_tempDir, issue.Id, status: IssueStatus.Progress);

        var stateAfterEdit = await _undoRedo.GetStateAsync(_tempDir);
        Assert.That(stateAfterEdit.CanUndo, Is.True);
        Assert.That(stateAfterEdit.UndoCount, Is.EqualTo(1));

        // Undo
        var ok = await _undoRedo.UndoAsync(_tempDir, CancellationToken.None);
        Assert.That(ok, Is.True);

        var restored = await _fleeceService.GetIssueAsync(_tempDir, issue.Id);
        Assert.That(restored!.Status, Is.EqualTo(IssueStatus.Open));

        var stateAfterUndo = await _undoRedo.GetStateAsync(_tempDir);
        Assert.That(stateAfterUndo.CanUndo, Is.False);
        Assert.That(stateAfterUndo.CanRedo, Is.True);
    }

    [Test]
    public async Task RedoAfterUndo_ReapliesForwardEdit()
    {
        var issue = await _fleeceService.CreateIssueAsync(_tempDir, "Issue A", IssueType.Task, recordUndo: false);

        await _fleeceService.UpdateIssueAsync(_tempDir, issue.Id, status: IssueStatus.Progress);
        await _undoRedo.UndoAsync(_tempDir, CancellationToken.None);

        var ok = await _undoRedo.RedoAsync(_tempDir, CancellationToken.None);
        Assert.That(ok, Is.True);

        var reapplied = await _fleeceService.GetIssueAsync(_tempDir, issue.Id);
        Assert.That(reapplied!.Status, Is.EqualTo(IssueStatus.Progress));
    }

    [Test]
    public async Task CreateThenUndo_SoftDeletesIssue()
    {
        var issue = await _fleeceService.CreateIssueAsync(_tempDir, "Issue A", IssueType.Task);

        var ok = await _undoRedo.UndoAsync(_tempDir, CancellationToken.None);
        Assert.That(ok, Is.True);

        var afterUndo = await _fleeceService.GetIssueAsync(_tempDir, issue.Id);
        Assert.That(afterUndo, Is.Not.Null);
        Assert.That(afterUndo!.Status, Is.EqualTo(IssueStatus.Deleted),
            "undo-of-create must soft-delete via Set(status, Deleted), not hard-delete via tombstone");
    }

    [Test]
    public async Task DeleteThenUndo_RestoresPriorStatus()
    {
        var issue = await _fleeceService.CreateIssueAsync(_tempDir, "Issue A", IssueType.Task, recordUndo: false);

        await _fleeceService.DeleteIssueAsync(_tempDir, issue.Id);
        var state = await _undoRedo.GetStateAsync(_tempDir);
        Assert.That(state.UndoCount, Is.EqualTo(1));

        await _undoRedo.UndoAsync(_tempDir, CancellationToken.None);

        var restored = await _fleeceService.GetIssueAsync(_tempDir, issue.Id);
        Assert.That(restored!.Status, Is.EqualTo(IssueStatus.Open));
    }

    [Test]
    public async Task NewMutationAfterUndo_TruncatesRedoStack()
    {
        var issue = await _fleeceService.CreateIssueAsync(_tempDir, "A", IssueType.Task, recordUndo: false);
        await _fleeceService.UpdateIssueAsync(_tempDir, issue.Id, status: IssueStatus.Progress);
        await _fleeceService.UpdateIssueAsync(_tempDir, issue.Id, status: IssueStatus.Review);
        await _undoRedo.UndoAsync(_tempDir, CancellationToken.None);
        await _undoRedo.UndoAsync(_tempDir, CancellationToken.None);

        var stateBefore = await _undoRedo.GetStateAsync(_tempDir);
        Assert.That(stateBefore.RedoCount, Is.EqualTo(2));

        await _fleeceService.UpdateIssueAsync(_tempDir, issue.Id, status: IssueStatus.Complete);

        var stateAfter = await _undoRedo.GetStateAsync(_tempDir);
        Assert.That(stateAfter.RedoCount, Is.EqualTo(0));
        Assert.That(stateAfter.UndoCount, Is.EqualTo(1));
    }

    [Test]
    public async Task RecordUndoFalse_SkipsPush()
    {
        var issue = await _fleeceService.CreateIssueAsync(_tempDir, "A", IssueType.Task, recordUndo: false);
        await _fleeceService.UpdateIssueAsync(_tempDir, issue.Id, status: IssueStatus.Progress, recordUndo: false);

        var state = await _undoRedo.GetStateAsync(_tempDir);
        Assert.That(state.UndoCount, Is.EqualTo(0));
    }

    [Test]
    public async Task BoundedStack_EvictsOldestAt100()
    {
        var issue = await _fleeceService.CreateIssueAsync(_tempDir, "A", IssueType.Task, recordUndo: false);

        // Push 102 single-step entries.
        for (int i = 0; i < IssueUndoRedoConstants.MaxStackDepth + 2; i++)
        {
            var title = $"title-{i}";
            await _fleeceService.UpdateIssueAsync(_tempDir, issue.Id, title: title);
        }

        var state = await _undoRedo.GetStateAsync(_tempDir);
        Assert.That(state.UndoCount, Is.EqualTo(IssueUndoRedoConstants.MaxStackDepth));
    }

    [Test]
    public async Task BatchScope_MergesMultipleWritesIntoOneEntry()
    {
        Issue issue;
        using (_undoRedo.BeginBatch(_tempDir))
        {
            issue = await _fleeceService.CreateIssueAsync(_tempDir, "A", IssueType.Task);
            await _fleeceService.UpdateIssueAsync(_tempDir, issue.Id, status: IssueStatus.Progress);
        }

        var state = await _undoRedo.GetStateAsync(_tempDir);
        Assert.That(state.UndoCount, Is.EqualTo(1),
            "all writes inside BeginBatch must collapse to one stack entry");

        // Undo should reverse BOTH steps in one go.
        await _undoRedo.UndoAsync(_tempDir, CancellationToken.None);

        var afterUndo = await _fleeceService.GetIssueAsync(_tempDir, issue.Id);
        // Create-undo → soft-delete; status-change-undo is masked by the create-soft-delete
        // because the undo state overlay applies the issue state captured BEFORE the create
        // (which puts status=Deleted on the issue).
        Assert.That(afterUndo!.Status, Is.EqualTo(IssueStatus.Deleted));
    }

    [Test]
    public async Task MoveSeriesSibling_UndoRestoresAllSortOrders()
    {
        var parent = await _fleeceService.CreateIssueAsync(_tempDir, "Parent", IssueType.Task, recordUndo: false);
        var a = await _fleeceService.CreateIssueAsync(_tempDir, "A", IssueType.Task, recordUndo: false);
        var b = await _fleeceService.CreateIssueAsync(_tempDir, "B", IssueType.Task, recordUndo: false);
        var c = await _fleeceService.CreateIssueAsync(_tempDir, "C", IssueType.Task, recordUndo: false);
        await _fleeceService.AddParentAsync(_tempDir, a.Id, parent.Id, recordUndo: false);
        await _fleeceService.AddParentAsync(_tempDir, b.Id, parent.Id, recordUndo: false);
        await _fleeceService.AddParentAsync(_tempDir, c.Id, parent.Id, recordUndo: false);

        // Snapshot sortOrders before the move (each child has one parent ref).
        async Task<Dictionary<string, string>> ReadSortOrders()
        {
            var dict = new Dictionary<string, string>();
            foreach (var id in new[] { a.Id, b.Id, c.Id })
            {
                var fresh = await _fleeceService.GetIssueAsync(_tempDir, id);
                dict[id] = fresh!.ParentIssues.Single(p => p.ParentIssue == parent.Id).SortOrder ?? "";
            }
            return dict;
        }

        var beforeMove = await ReadSortOrders();

        // Move B down.
        await _fleeceService.MoveSeriesSiblingAsync(_tempDir, b.Id, MoveDirection.Down);

        var afterMove = await ReadSortOrders();
        Assert.That(afterMove[b.Id], Is.Not.EqualTo(beforeMove[b.Id]),
            "the move must have shifted B's sortOrder");

        // Undo the move.
        await _undoRedo.UndoAsync(_tempDir, CancellationToken.None);

        var afterUndo = await ReadSortOrders();
        Assert.That(afterUndo[a.Id], Is.EqualTo(beforeMove[a.Id]));
        Assert.That(afterUndo[b.Id], Is.EqualTo(beforeMove[b.Id]));
        Assert.That(afterUndo[c.Id], Is.EqualTo(beforeMove[c.Id]));
    }

    [Test]
    public async Task ConcurrentPushFromMultipleThreads_DoesNotCorruptStack()
    {
        var issue = await _fleeceService.CreateIssueAsync(_tempDir, "A", IssueType.Task, recordUndo: false);

        await Task.WhenAll(Enumerable.Range(0, 20).Select(async i =>
        {
            await _fleeceService.UpdateIssueAsync(_tempDir, issue.Id, title: $"title-{i}");
        }));

        var state = await _undoRedo.GetStateAsync(_tempDir);
        Assert.That(state.UndoCount, Is.EqualTo(20));
    }

    [Test]
    public async Task UndoAppendsInverseEventsToChangeFile()
    {
        var issue = await _fleeceService.CreateIssueAsync(_tempDir, "A", IssueType.Task, recordUndo: false);
        await _fleeceService.UpdateIssueAsync(_tempDir, issue.Id, status: IssueStatus.Progress);
        await _undoRedo.UndoAsync(_tempDir, CancellationToken.None);

        var changesDir = Path.Combine(_tempDir, ".fleece", "changes");
        Assert.That(Directory.Exists(changesDir), Is.True);
        var files = Directory.GetFiles(changesDir, "change_*.jsonl");
        Assert.That(files, Is.Not.Empty);

        // The active change file should contain at least one inverse `set` line
        // restoring status to "Open".
        var content = await File.ReadAllTextAsync(files[0]);
        Assert.That(content, Does.Contain("\"property\":\"status\"").And.Contain("Open"));
    }
}
