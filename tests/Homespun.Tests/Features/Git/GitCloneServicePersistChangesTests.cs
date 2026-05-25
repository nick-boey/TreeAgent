using System.Text.Json;
using Homespun.Tests.Helpers;

namespace Homespun.Tests.Features.Git;

/// <summary>
/// Integration tests covering the OpenSpec `persist-pull-changes` clone-bootstrap
/// invariants: a new clone must not inherit main's `.active-change` or
/// `.replay-cache`, and must bootstrap a fresh `change_&lt;guid&gt;.jsonl` with a
/// `meta.follows` pointer to main's most-recent change at clone time.
/// </summary>
[TestFixture]
[Category("Integration")]
public class GitCloneServicePersistChangesTests
{
    private TempGitRepositoryFixture _fixture = null!;
    private GitCloneService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new TempGitRepositoryFixture();
        _service = new GitCloneService();
    }

    [TearDown]
    public void TearDown()
    {
        _fixture.Dispose();
    }

    /// <summary>
    /// Seeds the source repo's `.fleece/` directory with a known `.active-change`
    /// JSON object, an optional `.replay-cache`, and the supplied change file
    /// contents. Returns the fleece directory path.
    /// </summary>
    private string SeedFleece(
        string activeChangeGuid,
        bool seedReplayCache,
        IReadOnlyDictionary<string, string>? changeFiles = null)
    {
        var fleeceDir = Path.Combine(_fixture.RepositoryPath, ".fleece");
        var changesDir = Path.Combine(fleeceDir, "changes");
        Directory.CreateDirectory(changesDir);

        File.WriteAllText(
            Path.Combine(fleeceDir, ".active-change"),
            $"{{\"guid\":\"{activeChangeGuid}\"}}");

        if (seedReplayCache)
        {
            File.WriteAllText(
                Path.Combine(fleeceDir, ".replay-cache"),
                "{\"cache\":\"placeholder\"}");
        }

        if (changeFiles != null)
        {
            foreach (var (id, contents) in changeFiles)
            {
                File.WriteAllText(Path.Combine(changesDir, $"change_{id}.jsonl"), contents);
            }
        }

        return fleeceDir;
    }

    private static string ReadCloneActiveChangeGuid(string clonePath)
    {
        var activeChangePath = Path.Combine(clonePath, ".fleece", ".active-change");
        Assert.That(File.Exists(activeChangePath), Is.True,
            $"Expected clone to have a bootstrapped .active-change at {activeChangePath}");
        var json = File.ReadAllText(activeChangePath);
        using var doc = JsonDocument.Parse(json);
        var guid = doc.RootElement.GetProperty("guid").GetString();
        Assert.That(guid, Is.Not.Null.And.Not.Empty, ".active-change must contain a non-empty guid");
        return guid!;
    }

    private static (string Id, string FirstLine) ReadBootstrappedChangeFile(string clonePath, string excludeId)
    {
        var changesDir = Path.Combine(clonePath, ".fleece", "changes");
        var bootstrapped = Directory
            .GetFiles(changesDir, "change_*.jsonl")
            .Select(p => new { Path = p, Id = Path.GetFileNameWithoutExtension(p)["change_".Length..] })
            .Where(x => !string.Equals(x.Id, excludeId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.That(bootstrapped, Has.Count.EqualTo(1),
            "Expected exactly one bootstrapped change file alongside any inherited ones.");

        var firstLine = File.ReadAllLines(bootstrapped[0].Path).First();
        return (bootstrapped[0].Id, firstLine);
    }

    [Test]
    public async Task GitCloneService_DoesNotInheritActiveChange()
    {
        // Arrange — seed main's .fleece with a known active-change pointer.
        var mainGuid = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        SeedFleece(
            activeChangeGuid: mainGuid,
            seedReplayCache: false,
            changeFiles: new Dictionary<string, string>
            {
                [mainGuid] = "{\"kind\":\"meta\"}\n"
            });
        _fixture.RunGit("add .fleece/changes");
        _fixture.RunGit("commit -m \"Seed fleece state\"");

        var branchName = "feature/active-change-inherit";

        // Act
        var clonePath = await _service.CreateCloneAsync(
            _fixture.RepositoryPath, branchName, createBranch: true);

        // Assert — clone has its own active-change with a different guid.
        Assert.That(clonePath, Is.Not.Null);
        var cloneGuid = ReadCloneActiveChangeGuid(clonePath!);
        Assert.That(cloneGuid, Is.Not.EqualTo(mainGuid),
            "Clone's .active-change must not inherit main's guid.");
        Assert.That(cloneGuid, Has.Length.EqualTo(32),
            "Bootstrapped guid must be a 32-character lowercase hex GUID (Guid.NewGuid().ToString(\"N\")).");
        Assert.That(cloneGuid, Does.Match("^[0-9a-f]{32}$"),
            "Bootstrapped guid must be 32 lowercase hex characters with no dashes.");
    }

    [Test]
    public async Task GitCloneService_DoesNotInheritReplayCache()
    {
        // Arrange — seed main with a .replay-cache.
        var mainGuid = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        SeedFleece(
            activeChangeGuid: mainGuid,
            seedReplayCache: true,
            changeFiles: new Dictionary<string, string>
            {
                [mainGuid] = "{\"kind\":\"meta\"}\n"
            });
        _fixture.RunGit("add .fleece/changes");
        _fixture.RunGit("commit -m \"Seed fleece state\"");

        var branchName = "feature/replay-cache-inherit";

        // Act
        var clonePath = await _service.CreateCloneAsync(
            _fixture.RepositoryPath, branchName, createBranch: true);

        // Assert — clone has no .replay-cache.
        Assert.That(clonePath, Is.Not.Null);
        var cloneReplayCachePath = Path.Combine(clonePath!, ".fleece", ".replay-cache");
        Assert.That(File.Exists(cloneReplayCachePath), Is.False,
            "Clone must not inherit main's .replay-cache.");
    }

    [Test]
    public async Task GitCloneService_BootstrapsFreshChangeFileWithFollowsPointer()
    {
        // Arrange — two existing change files in main, BBBB has the most recent mtime.
        var olderId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var newerId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        SeedFleece(
            activeChangeGuid: newerId,
            seedReplayCache: false,
            changeFiles: new Dictionary<string, string>
            {
                [olderId] = "{\"kind\":\"meta\"}\n",
                [newerId] = "{\"kind\":\"meta\",\"follows\":\"" + olderId + "\"}\n"
            });

        // Force a deterministic mtime ordering: older < newer.
        var olderPath = Path.Combine(_fixture.RepositoryPath, ".fleece", "changes", $"change_{olderId}.jsonl");
        var newerPath = Path.Combine(_fixture.RepositoryPath, ".fleece", "changes", $"change_{newerId}.jsonl");
        var baseTime = DateTime.UtcNow.AddMinutes(-10);
        File.SetLastWriteTimeUtc(olderPath, baseTime);
        File.SetLastWriteTimeUtc(newerPath, baseTime.AddMinutes(5));

        _fixture.RunGit("add .fleece/changes");
        _fixture.RunGit("commit -m \"Seed fleece state\"");

        var branchName = "feature/follows-pointer";

        // Act
        var clonePath = await _service.CreateCloneAsync(
            _fixture.RepositoryPath, branchName, createBranch: true);
        Assert.That(clonePath, Is.Not.Null);

        var cloneChangesDir = Path.Combine(clonePath!, ".fleece", "changes");
        // The bootstrapped change is the one whose id is NOT one of the seeded ids.
        var allChangeFiles = Directory.GetFiles(cloneChangesDir, "change_*.jsonl")
            .Select(p => Path.GetFileNameWithoutExtension(p)["change_".Length..])
            .ToList();
        Assert.That(allChangeFiles, Has.Member(olderId), "Older inherited change file should still exist in the clone.");
        Assert.That(allChangeFiles, Has.Member(newerId), "Newer inherited change file should still exist in the clone.");

        var bootstrappedIds = allChangeFiles.Where(id => id != olderId && id != newerId).ToList();
        Assert.That(bootstrappedIds, Has.Count.EqualTo(1),
            "Exactly one fresh bootstrapped change file should be present.");
        var newId = bootstrappedIds[0];
        Assert.That(newId, Does.Match("^[0-9a-f]{32}$"),
            "Bootstrapped change file id must be a 32-char lowercase hex guid.");

        var bootstrappedPath = Path.Combine(cloneChangesDir, $"change_{newId}.jsonl");
        var firstLine = File.ReadAllLines(bootstrappedPath).First();
        using var doc = JsonDocument.Parse(firstLine);
        Assert.That(doc.RootElement.GetProperty("kind").GetString(), Is.EqualTo("meta"));
        Assert.That(doc.RootElement.GetProperty("follows").GetString(), Is.EqualTo(newerId),
            "follows should point to the change file with the most recent mtime (BBBB).");

        // Active-change file points to the bootstrapped id.
        var cloneGuid = ReadCloneActiveChangeGuid(clonePath!);
        Assert.That(cloneGuid, Is.EqualTo(newId),
            "Clone .active-change guid must match the bootstrapped change file id.");
    }

    [Test]
    public async Task GitCloneService_BootstrapsRootMetaWhenChangesDirEmpty()
    {
        // Arrange — fleece dir exists but `changes/` is empty.
        var fleeceDir = Path.Combine(_fixture.RepositoryPath, ".fleece");
        Directory.CreateDirectory(Path.Combine(fleeceDir, "changes"));
        // Keep an .active-change for completeness — it must still be excluded from the copy.
        File.WriteAllText(
            Path.Combine(fleeceDir, ".active-change"),
            "{\"guid\":\"cccccccccccccccccccccccccccccccc\"}");
        // Create a placeholder file in changes/ so git tracks the directory, then delete it
        // so the directory is genuinely empty when CreateCloneAsync runs.
        var placeholder = Path.Combine(fleeceDir, "changes", ".gitkeep");
        File.WriteAllText(placeholder, "");
        _fixture.RunGit("add .fleece/changes/.gitkeep");
        _fixture.RunGit("commit -m \"Seed empty fleece state\"");
        File.Delete(placeholder);

        var branchName = "feature/empty-changes-dir";

        // Act
        var clonePath = await _service.CreateCloneAsync(
            _fixture.RepositoryPath, branchName, createBranch: true);
        Assert.That(clonePath, Is.Not.Null);

        // Remove any inherited .gitkeep file so the bootstrapped change is the only entry.
        var clonePlaceholder = Path.Combine(clonePath!, ".fleece", "changes", ".gitkeep");
        if (File.Exists(clonePlaceholder))
        {
            File.Delete(clonePlaceholder);
        }

        // Assert — bootstrapped file's first line is `{"kind":"meta"}` with no `follows` field.
        var cloneChangesDir = Path.Combine(clonePath!, ".fleece", "changes");
        var bootstrappedFiles = Directory.GetFiles(cloneChangesDir, "change_*.jsonl");
        Assert.That(bootstrappedFiles, Has.Length.EqualTo(1),
            "Expected exactly one bootstrapped change file when source had no existing change files.");

        var firstLine = File.ReadAllLines(bootstrappedFiles[0]).First();
        using var doc = JsonDocument.Parse(firstLine);
        Assert.That(doc.RootElement.GetProperty("kind").GetString(), Is.EqualTo("meta"));
        Assert.That(doc.RootElement.TryGetProperty("follows", out _), Is.False,
            "follows field must be omitted when the source changes directory is empty.");
    }

    [Test]
    public async Task GitCloneService_TwoClonesFromSameMainBootstrapDistinctChanges()
    {
        // Arrange — single existing change in main.
        var parentId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        SeedFleece(
            activeChangeGuid: parentId,
            seedReplayCache: false,
            changeFiles: new Dictionary<string, string>
            {
                [parentId] = "{\"kind\":\"meta\"}\n"
            });
        _fixture.RunGit("add .fleece/changes");
        _fixture.RunGit("commit -m \"Seed fleece state\"");

        // Act — create two distinct clones in sequence from the same main.
        var cloneXPath = await _service.CreateCloneAsync(
            _fixture.RepositoryPath, "feature/clone-x", createBranch: true);
        var cloneYPath = await _service.CreateCloneAsync(
            _fixture.RepositoryPath, "feature/clone-y", createBranch: true);
        Assert.That(cloneXPath, Is.Not.Null);
        Assert.That(cloneYPath, Is.Not.Null);

        var (xId, xFirstLine) = ReadBootstrappedChangeFile(cloneXPath!, excludeId: parentId);
        var (yId, yFirstLine) = ReadBootstrappedChangeFile(cloneYPath!, excludeId: parentId);

        // Assert — distinct bootstrapped guids, both with `follows: parentId`.
        Assert.That(xId, Is.Not.EqualTo(yId),
            "Each clone must have its own unique bootstrapped change guid.");
        Assert.That(xId, Does.Match("^[0-9a-f]{32}$"));
        Assert.That(yId, Does.Match("^[0-9a-f]{32}$"));

        using var xDoc = JsonDocument.Parse(xFirstLine);
        using var yDoc = JsonDocument.Parse(yFirstLine);
        Assert.That(xDoc.RootElement.GetProperty("follows").GetString(), Is.EqualTo(parentId),
            "Clone X's bootstrap meta should follow the same parent.");
        Assert.That(yDoc.RootElement.GetProperty("follows").GetString(), Is.EqualTo(parentId),
            "Clone Y's bootstrap meta should follow the same parent (multiple children of one DAG node are valid).");
    }
}
