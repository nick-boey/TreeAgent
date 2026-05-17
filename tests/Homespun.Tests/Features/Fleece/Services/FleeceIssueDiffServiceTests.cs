using NUnit.Framework;
using Moq;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Commands;
using Homespun.Shared.Models.Fleece;
using Microsoft.Extensions.Logging;
using Fleece.Core.Models;
using System.Text.Json;

namespace Homespun.Tests.Features.Fleece.Services;

[TestFixture]
public class FleeceIssueDiffServiceTests
{
    private Mock<ICommandRunner> _commandRunnerMock = null!;
    private Mock<ILogger<FleeceIssueDiffService>> _loggerMock = null!;
    private FleeceIssueDiffService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _commandRunnerMock = new Mock<ICommandRunner>();
        _loggerMock = new Mock<ILogger<FleeceIssueDiffService>>();
        _service = new FleeceIssueDiffService(_commandRunnerMock.Object, _loggerMock.Object);
    }

    [Test]
    public async Task GetIssueDiffsAsync_WithNoChanges_ReturnsEmptyList()
    {
        // Arrange
        var workingDirectory = "/test/repo";

        // Mock git diff to show no changes
        _commandRunnerMock.Setup(x => x.RunAsync(
            "git",
            "diff --name-status HEAD -- .fleece/issues/",
            workingDirectory))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = "",
                Error = ""
            });

        // Act
        var result = await _service.GetIssueDiffsAsync(workingDirectory);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetIssueDiffsAsync_WithNewIssue_ReturnsAddedDiff()
    {
        // Arrange
        var workingDirectory = "/test/repo";
        var newIssue = new Issue
        {
            Id = "test123",
            Title = "New Feature",
            Status = IssueStatus.Open,
            Type = IssueType.Feature,
            Description = "Test description",
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdate = DateTimeOffset.UtcNow
        };
        var issueJson = JsonSerializer.Serialize(newIssue);

        // Mock git diff showing new file
        _commandRunnerMock.Setup(x => x.RunAsync(
            "git",
            "diff --name-status HEAD -- .fleece/issues/",
            workingDirectory))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = "A\t.fleece/issues/test123.json",
                Error = ""
            });

        // Mock reading new file
        _commandRunnerMock.Setup(x => x.RunAsync(
            "cat",
            ".fleece/issues/test123.json",
            workingDirectory))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = issueJson,
                Error = ""
            });

        // Act
        var result = await _service.GetIssueDiffsAsync(workingDirectory);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        var diff = result[0];
        Assert.That(diff.IssueId, Is.EqualTo("test123"));
        Assert.That(diff.ChangeType, Is.EqualTo(IssueChangeType.Created));
        Assert.That(diff.OriginalIssue, Is.Null);
        Assert.That(diff.ModifiedIssue, Is.Not.Null);
        Assert.That(diff.ModifiedIssue!.Title, Is.EqualTo("New Feature"));
    }

    [Test]
    public async Task GetIssueDiffsAsync_WithModifiedIssue_ReturnsUpdatedDiff()
    {
        // Arrange
        var workingDirectory = "/test/repo";
        var originalIssue = new Issue
        {
            Id = "test123",
            Title = "Original Title",
            Status = IssueStatus.Open,
            Type = IssueType.Feature,
            Description = "Original description",
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdate = DateTimeOffset.UtcNow
        };
        var modifiedIssue = new Issue
        {
            Id = "test123",
            Title = "Modified Title",
            Status = IssueStatus.Progress,
            Type = IssueType.Feature,
            Description = "Modified description",
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdate = DateTimeOffset.UtcNow
        };
        var originalJson = JsonSerializer.Serialize(originalIssue);
        var modifiedJson = JsonSerializer.Serialize(modifiedIssue);

        // Mock git diff showing modified file
        _commandRunnerMock.Setup(x => x.RunAsync(
            "git",
            "diff --name-status HEAD -- .fleece/issues/",
            workingDirectory))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = "M\t.fleece/issues/test123.json",
                Error = ""
            });

        // Mock reading modified file from working tree
        _commandRunnerMock.Setup(x => x.RunAsync(
            "cat",
            ".fleece/issues/test123.json",
            workingDirectory))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = modifiedJson,
                Error = ""
            });

        // Mock reading original file from HEAD
        _commandRunnerMock.Setup(x => x.RunAsync(
            "git",
            "show HEAD:.fleece/issues/test123.json",
            workingDirectory))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = originalJson,
                Error = ""
            });

        // Act
        var result = await _service.GetIssueDiffsAsync(workingDirectory);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        var diff = result[0];
        Assert.That(diff.IssueId, Is.EqualTo("test123"));
        Assert.That(diff.ChangeType, Is.EqualTo(IssueChangeType.Updated));
        Assert.That(diff.OriginalIssue, Is.Not.Null);
        Assert.That(diff.ModifiedIssue, Is.Not.Null);
        Assert.That(diff.OriginalIssue!.Title, Is.EqualTo("Original Title"));
        Assert.That(diff.ModifiedIssue!.Title, Is.EqualTo("Modified Title"));
        Assert.That(diff.ChangedFields, Contains.Item("Title"));
        Assert.That(diff.ChangedFields, Contains.Item("Status"));
        Assert.That(diff.ChangedFields, Contains.Item("Description"));
    }

    [Test]
    public async Task GetIssueDiffsAsync_WithDeletedIssue_ReturnsDeletedDiff()
    {
        // Arrange
        var workingDirectory = "/test/repo";
        var deletedIssue = new Issue
        {
            Id = "test123",
            Title = "Deleted Issue",
            Status = IssueStatus.Complete,
            Type = IssueType.Task,
            Description = "This will be deleted",
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdate = DateTimeOffset.UtcNow
        };
        var issueJson = JsonSerializer.Serialize(deletedIssue);

        // Mock git diff showing deleted file
        _commandRunnerMock.Setup(x => x.RunAsync(
            "git",
            "diff --name-status HEAD -- .fleece/issues/",
            workingDirectory))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = "D\t.fleece/issues/test123.json",
                Error = ""
            });

        // Mock reading original file from HEAD
        _commandRunnerMock.Setup(x => x.RunAsync(
            "git",
            "show HEAD:.fleece/issues/test123.json",
            workingDirectory))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = issueJson,
                Error = ""
            });

        // Act
        var result = await _service.GetIssueDiffsAsync(workingDirectory);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        var diff = result[0];
        Assert.That(diff.IssueId, Is.EqualTo("test123"));
        Assert.That(diff.ChangeType, Is.EqualTo(IssueChangeType.Deleted));
        Assert.That(diff.OriginalIssue, Is.Not.Null);
        Assert.That(diff.ModifiedIssue, Is.Null);
        Assert.That(diff.OriginalIssue!.Title, Is.EqualTo("Deleted Issue"));
    }

    [Test]
    public async Task GetIssueDiffsAsync_WithInvalidJsonInWorkingTree_IgnoresEntry()
    {
        // Arrange
        var workingDirectory = "/test/repo";

        // Mock git diff showing modified file
        _commandRunnerMock.Setup(x => x.RunAsync(
            "git",
            "diff --name-status HEAD -- .fleece/issues/",
            workingDirectory))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = "M\t.fleece/issues/test123.json",
                Error = ""
            });

        // Mock reading modified file with invalid JSON
        _commandRunnerMock.Setup(x => x.RunAsync(
            "cat",
            ".fleece/issues/test123.json",
            workingDirectory))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = "{ invalid json",
                Error = ""
            });

        // Mock reading original file (valid JSON)
        _commandRunnerMock.Setup(x => x.RunAsync(
            "git",
            "show HEAD:.fleece/issues/test123.json",
            workingDirectory))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = JsonSerializer.Serialize(new Issue { Id = "test123", Title = "Valid Issue", Status = IssueStatus.Open, Type = IssueType.Task, CreatedAt = DateTimeOffset.UtcNow, LastUpdate = DateTimeOffset.UtcNow }),
                Error = ""
            });

        // Act
        var result = await _service.GetIssueDiffsAsync(workingDirectory);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetIssueDiffsAsync_WithMultipleChanges_ReturnsAllDiffs()
    {
        // Arrange
        var workingDirectory = "/test/repo";
        var issue1 = new Issue { Id = "test1", Title = "Issue 1", Status = IssueStatus.Open, Type = IssueType.Task, CreatedAt = DateTimeOffset.UtcNow, LastUpdate = DateTimeOffset.UtcNow };
        var issue2 = new Issue { Id = "test2", Title = "Issue 2", Status = IssueStatus.Open, Type = IssueType.Task, CreatedAt = DateTimeOffset.UtcNow, LastUpdate = DateTimeOffset.UtcNow };
        var issue3 = new Issue { Id = "test3", Title = "Issue 3", Status = IssueStatus.Open, Type = IssueType.Task, CreatedAt = DateTimeOffset.UtcNow, LastUpdate = DateTimeOffset.UtcNow };

        // Mock git diff showing multiple changes
        _commandRunnerMock.Setup(x => x.RunAsync(
            "git",
            "diff --name-status HEAD -- .fleece/issues/",
            workingDirectory))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = "A\t.fleece/issues/test1.json\nM\t.fleece/issues/test2.json\nD\t.fleece/issues/test3.json",
                Error = ""
            });

        // Mock reading new file
        _commandRunnerMock.Setup(x => x.RunAsync(
            "cat",
            ".fleece/issues/test1.json",
            workingDirectory))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = JsonSerializer.Serialize(issue1),
                Error = ""
            });

        // Mock reading modified file
        _commandRunnerMock.Setup(x => x.RunAsync(
            "cat",
            ".fleece/issues/test2.json",
            workingDirectory))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = JsonSerializer.Serialize(issue2),
                Error = ""
            });

        // Mock reading original for modified file
        _commandRunnerMock.Setup(x => x.RunAsync(
            "git",
            "show HEAD:.fleece/issues/test2.json",
            workingDirectory))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = JsonSerializer.Serialize(new Issue { Id = "test2", Title = "Original Issue 2", Status = IssueStatus.Open, Type = IssueType.Task, CreatedAt = DateTimeOffset.UtcNow, LastUpdate = DateTimeOffset.UtcNow }),
                Error = ""
            });

        // Mock reading deleted file from HEAD
        _commandRunnerMock.Setup(x => x.RunAsync(
            "git",
            "show HEAD:.fleece/issues/test3.json",
            workingDirectory))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = JsonSerializer.Serialize(issue3),
                Error = ""
            });

        // Act
        var result = await _service.GetIssueDiffsAsync(workingDirectory);

        // Assert
        Assert.That(result, Has.Count.EqualTo(3));
        Assert.That(result.Select(d => d.IssueId), Is.EquivalentTo(new[] { "test1", "test2", "test3" }));
        Assert.That(result[0].ChangeType, Is.EqualTo(IssueChangeType.Created));
        Assert.That(result[1].ChangeType, Is.EqualTo(IssueChangeType.Updated));
        Assert.That(result[2].ChangeType, Is.EqualTo(IssueChangeType.Deleted));
    }
}