using Homespun.Features.OpenSpec.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Homespun.Tests.Features.OpenSpec;

[TestFixture]
public class PhaseDispatchGuardTests
{
    private PhaseDispatchGuard _guard = null!;
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _guard = new PhaseDispatchGuard(NullLogger<PhaseDispatchGuard>.Instance);
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string WriteTasksMd(string changeName, string content)
    {
        var changeDir = Path.Combine(_tempDir, "openspec", "changes", changeName);
        Directory.CreateDirectory(changeDir);
        var path = Path.Combine(changeDir, "tasks.md");
        File.WriteAllText(path, content);
        return path;
    }

    [Test]
    public async Task GetBlockingPhases_ReturnsEmpty_WhenTasksMdMissing()
    {
        var result = await _guard.GetBlockingPhasesAsync(_tempDir, "my-change", "Phase 2");

        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetBlockingPhases_ReturnsEmpty_WhenTasksMdHasNoPhases()
    {
        WriteTasksMd("my-change", "No phase headings here.\n- [x] just a task\n");

        var result = await _guard.GetBlockingPhasesAsync(_tempDir, "my-change", "Phase 2");

        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetBlockingPhases_ReturnsEmpty_WhenPhaseIsFirst()
    {
        WriteTasksMd("my-change", """
            ## Phase 1

            - [ ] Task A
            - [ ] Task B

            ## Phase 2

            - [ ] Task C
            """);

        var result = await _guard.GetBlockingPhasesAsync(_tempDir, "my-change", "Phase 1");

        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetBlockingPhases_ReturnsEmpty_WhenPhaseNotFound()
    {
        WriteTasksMd("my-change", """
            ## Phase 1

            - [x] Task A

            ## Phase 2

            - [ ] Task C
            """);

        var result = await _guard.GetBlockingPhasesAsync(_tempDir, "my-change", "Phase 99");

        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetBlockingPhases_ReturnsEmpty_WhenAllPriorPhasesComplete()
    {
        WriteTasksMd("my-change", """
            ## Phase 1

            - [x] Task A
            - [x] Task B

            ## Phase 2

            - [x] Task C

            ## Phase 3

            - [ ] Task D
            """);

        var result = await _guard.GetBlockingPhasesAsync(_tempDir, "my-change", "Phase 3");

        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetBlockingPhases_ReturnsBlockingPhase_WhenImmediatePriorIncomplete()
    {
        WriteTasksMd("my-change", """
            ## Phase 1

            - [x] Task A
            - [ ] Task B

            ## Phase 2

            - [ ] Task C
            """);

        var result = await _guard.GetBlockingPhasesAsync(_tempDir, "my-change", "Phase 2");

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0], Is.EqualTo("Phase 1"));
    }

    [Test]
    public async Task GetBlockingPhases_ReturnsMultipleBlockingPhases_WhenSeveralPriorIncomplete()
    {
        WriteTasksMd("my-change", """
            ## Phase 1

            - [ ] Task A

            ## Phase 2

            - [ ] Task B

            ## Phase 3

            - [x] Task C

            ## Phase 4

            - [ ] Task D
            """);

        var result = await _guard.GetBlockingPhasesAsync(_tempDir, "my-change", "Phase 4");

        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result, Contains.Item("Phase 1"));
        Assert.That(result, Contains.Item("Phase 2"));
        Assert.That(result, Does.Not.Contain("Phase 3"));
    }

    [Test]
    public async Task GetBlockingPhases_IsCaseInsensitiveOnPhaseName()
    {
        WriteTasksMd("my-change", """
            ## Phase 1

            - [x] Task A

            ## PHASE 2

            - [ ] Task B
            """);

        var result = await _guard.GetBlockingPhasesAsync(_tempDir, "my-change", "phase 2");

        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetBlockingPhases_OnlyBlocksIncompletePhases_SkipsCompleteOnes()
    {
        WriteTasksMd("my-change", """
            ## Phase 1

            - [x] Task A

            ## Phase 2

            - [ ] Task B

            ## Phase 3

            - [x] Task C

            ## Phase 4

            - [ ] Task D
            """);

        var result = await _guard.GetBlockingPhasesAsync(_tempDir, "my-change", "Phase 4");

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0], Is.EqualTo("Phase 2"));
    }
}
