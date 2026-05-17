using System.Text.Json;
using System.Text.Json.Serialization;
using Fleece.Core.Models;
using Homespun.Features.Testing.Services;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Moq;

namespace Homespun.Tests.Features.Testing;

[TestFixture]
public class FleeceIssueSeederTests
{
    private Mock<ILogger<FleeceIssueSeeder>> _loggerMock = null!;
    private FleeceIssueSeeder _seeder = null!;
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _loggerMock = new Mock<ILogger<FleeceIssueSeeder>>();
        _seeder = new FleeceIssueSeeder(_loggerMock.Object);
        _tempDir = Path.Combine(Path.GetTempPath(), $"fleece-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Test]
    public async Task SeedIssuesAsync_CreatesFleeceDirectory()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var issues = new List<Issue>
        {
            new() { Id = "TEST-001", Title = "Test Issue", Type = IssueType.Task, Status = IssueStatus.Open, CreatedAt = now, LastUpdate = now }
        };

        // Act
        await _seeder.SeedIssuesAsync(_tempDir, issues);

        // Assert
        var fleeceDir = Path.Combine(_tempDir, ".fleece");
        Assert.That(Directory.Exists(fleeceDir), Is.True);
    }

    [Test]
    public async Task SeedIssuesAsync_CreatesJsonlFile()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var issues = new List<Issue>
        {
            new() { Id = "TEST-001", Title = "Test Issue", Type = IssueType.Task, Status = IssueStatus.Open, CreatedAt = now, LastUpdate = now }
        };

        // Act
        await _seeder.SeedIssuesAsync(_tempDir, issues);

        // Assert — seeder writes the v3.1 lean snapshot at the stable path.
        var fleeceDir = Path.Combine(_tempDir, ".fleece");
        var snapshotPath = Path.Combine(fleeceDir, "issues.jsonl");
        Assert.That(File.Exists(snapshotPath), Is.True);
    }

    [Test]
    public async Task SeedIssuesAsync_WritesIssuesAsJsonl()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var issues = new List<Issue>
        {
            new() { Id = "TEST-001", Title = "First Issue", Type = IssueType.Task, Status = IssueStatus.Open, CreatedAt = now, LastUpdate = now },
            new() { Id = "TEST-002", Title = "Second Issue", Type = IssueType.Bug, Status = IssueStatus.Progress, CreatedAt = now, LastUpdate = now }
        };

        // Act
        await _seeder.SeedIssuesAsync(_tempDir, issues);

        // Assert — seeder writes the v3.1 lean snapshot at the stable path.
        var fleeceDir = Path.Combine(_tempDir, ".fleece");
        var jsonlFile = Path.Combine(fleeceDir, "issues.jsonl");
        var lines = await File.ReadAllLinesAsync(jsonlFile);

        Assert.That(lines.Length, Is.EqualTo(2));

        // Verify first issue
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };
        var issue1 = JsonSerializer.Deserialize<Issue>(lines[0], jsonOptions);
        Assert.That(issue1?.Id, Is.EqualTo("TEST-001"));
        Assert.That(issue1?.Title, Is.EqualTo("First Issue"));

        // Verify second issue
        var issue2 = JsonSerializer.Deserialize<Issue>(lines[1], jsonOptions);
        Assert.That(issue2?.Id, Is.EqualTo("TEST-002"));
        Assert.That(issue2?.Title, Is.EqualTo("Second Issue"));
    }

    [Test]
    public async Task SeedIssuesAsync_EmptyIssueList_DoesNotCreateFile()
    {
        // Arrange
        var issues = new List<Issue>();

        // Act
        await _seeder.SeedIssuesAsync(_tempDir, issues);

        // Assert
        var fleeceDir = Path.Combine(_tempDir, ".fleece");
        Assert.That(Directory.Exists(fleeceDir), Is.False);
    }

    [Test]
    public void CreateMinimalProjectStructure_CreatesGitignore()
    {
        // Arrange
        var projectPath = Path.Combine(_tempDir, "test-project");

        // Act
        _seeder.CreateMinimalProjectStructure(projectPath, "Test Project");

        // Assert
        var gitignorePath = Path.Combine(projectPath, ".gitignore");
        Assert.That(File.Exists(gitignorePath), Is.True);
    }

    [Test]
    public void CreateMinimalProjectStructure_CreatesReadme()
    {
        // Arrange
        var projectPath = Path.Combine(_tempDir, "test-project");

        // Act
        _seeder.CreateMinimalProjectStructure(projectPath, "Test Project");

        // Assert
        var readmePath = Path.Combine(projectPath, "README.md");
        Assert.That(File.Exists(readmePath), Is.True);

        var content = File.ReadAllText(readmePath);
        Assert.That(content, Does.Contain("Test Project"));
    }

    [Test]
    public void CreateMinimalProjectStructure_DoesNotOverwriteExistingFiles()
    {
        // Arrange
        var projectPath = Path.Combine(_tempDir, "test-project");
        Directory.CreateDirectory(projectPath);

        var gitignorePath = Path.Combine(projectPath, ".gitignore");
        File.WriteAllText(gitignorePath, "original content");

        // Act
        _seeder.CreateMinimalProjectStructure(projectPath, "Test Project");

        // Assert
        var content = File.ReadAllText(gitignorePath);
        Assert.That(content, Is.EqualTo("original content"));
    }
}
