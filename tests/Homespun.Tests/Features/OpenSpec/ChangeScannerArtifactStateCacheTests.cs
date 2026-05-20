using Homespun.Features.Commands;
using Homespun.Features.Fleece.Services;
using Homespun.Features.OpenSpec.Services;
using Homespun.Shared.Models.Commands;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Homespun.Tests.Features.OpenSpec;

/// <summary>
/// Tier 4 — mtime-keyed micro-cache around <see cref="ChangeScannerService.GetArtifactStateAsync"/>.
/// </summary>
[TestFixture]
public class ChangeScannerArtifactStateCacheTests
{
    private string _clonePath = null!;
    private string _changeDir = null!;
    private const string ChangeName = "my-change";

    [SetUp]
    public void SetUp()
    {
        _clonePath = Path.Combine(Path.GetTempPath(), $"scanner-cache-{Guid.NewGuid():N}");
        _changeDir = Path.Combine(_clonePath, "openspec", "changes", ChangeName);
        Directory.CreateDirectory(_changeDir);
        Directory.CreateDirectory(Path.Combine(_changeDir, "specs"));
        File.WriteAllText(Path.Combine(_changeDir, "proposal.md"), "# proposal\n");
        File.WriteAllText(Path.Combine(_changeDir, "tasks.md"), "- [ ] 1.1 go\n");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_clonePath))
        {
            try { Directory.Delete(_clonePath, recursive: true); } catch { }
        }
    }

    [Test]
    public async Task Second_Call_With_No_Changes_Skips_Subprocess()
    {
        var (scanner, commandRunner) = BuildScanner(okJson: true);

        var first = await scanner.GetArtifactStateAsync(_clonePath, ChangeName);
        var second = await scanner.GetArtifactStateAsync(_clonePath, ChangeName);

        Assert.That(first, Is.Not.Null);
        Assert.That(second, Is.Not.Null);
        commandRunner.Verify(
            c => c.RunAsync("openspec", It.IsAny<string>(), _clonePath),
            Times.Once,
            "openspec status subprocess must only run on cache miss");
    }

    [Test]
    public async Task Modifying_Tasks_File_Busts_Cache_Entry()
    {
        var (scanner, commandRunner) = BuildScanner(okJson: true);

        await scanner.GetArtifactStateAsync(_clonePath, ChangeName);

        // Bump mtime — use a future timestamp so it definitely differs from
        // the initial creation time even on filesystems with coarse-grained
        // mtime resolution.
        File.SetLastWriteTimeUtc(
            Path.Combine(_changeDir, "tasks.md"),
            DateTime.UtcNow.AddMinutes(1));

        await scanner.GetArtifactStateAsync(_clonePath, ChangeName);

        commandRunner.Verify(
            c => c.RunAsync("openspec", It.IsAny<string>(), _clonePath),
            Times.Exactly(2),
            "tasks.md mtime change must invalidate the cache entry");
    }

    [Test]
    public async Task Missing_Change_Directory_Falls_Back_To_Uncached_Path()
    {
        var (scanner, commandRunner) = BuildScanner(okJson: true);

        // Point the scanner at a change name that does not exist on disk — the
        // cache path returns null (no mtime), so the subprocess is invoked and
        // the result is NOT cached (nothing to key on).
        await scanner.GetArtifactStateAsync(_clonePath, "missing-change");
        await scanner.GetArtifactStateAsync(_clonePath, "missing-change");

        commandRunner.Verify(
            c => c.RunAsync("openspec", It.IsAny<string>(), _clonePath),
            Times.Exactly(2),
            "Missing change directory must bypass the cache — no mtime key is available");
    }

    private (ChangeScannerService, Mock<ICommandRunner>) BuildScanner(bool okJson)
    {
        var commandRunner = new Mock<ICommandRunner>();
        commandRunner
            .Setup(c => c.RunAsync("openspec", It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new CommandResult
            {
                Success = okJson,
                Output = okJson
                    ? "{\"changeName\":\"my-change\",\"schemaName\":\"spec-driven\",\"isComplete\":true}"
                    : string.Empty,
                ExitCode = okJson ? 0 : 1
            });

        var scanner = new ChangeScannerService(
            new Mock<IProjectFleeceService>().Object,
            commandRunner.Object,
            NullLogger<ChangeScannerService>.Instance);

        return (scanner, commandRunner);
    }
}
