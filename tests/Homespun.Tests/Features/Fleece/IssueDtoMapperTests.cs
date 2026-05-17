using Fleece.Core.Models;
using Homespun.Features.Fleece;
using Homespun.Shared.Models.Fleece;

namespace Homespun.Tests.Features.Fleece;

[TestFixture]
public class IssueDtoMapperTests
{
    [Test]
    public void ToResponse_MapsAllProperties()
    {
        // Arrange
        var issue = new Issue
        {
            Id = "abc123",
            Title = "Test issue",
            Description = "A description",
            Status = IssueStatus.Progress,
            Type = IssueType.Bug,
            Priority = 2,
            LinkedIssues = ["linked1", "linked2"],
            ParentIssues = [new ParentIssueRef { ParentIssue = "parent1", SortOrder = "a0" }],
            Tags = ["tag1", "hsp-linked-pr=42"],
            WorkingBranchId = "fix/test",
            ExecutionMode = ExecutionMode.Parallel,
            CreatedBy = "testuser",
            AssignedTo = "dev1",
            LastUpdate = new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero),
            CreatedAt = new DateTimeOffset(2026, 1, 10, 8, 0, 0, TimeSpan.Zero)
        };

        // Act
        var response = issue.ToResponse();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(response.Id, Is.EqualTo("abc123"));
            Assert.That(response.Title, Is.EqualTo("Test issue"));
            Assert.That(response.Description, Is.EqualTo("A description"));
            Assert.That(response.Status, Is.EqualTo(IssueStatus.Progress));
            Assert.That(response.Type, Is.EqualTo(IssueType.Bug));
            Assert.That(response.Priority, Is.EqualTo(2));
            Assert.That(response.LinkedPRs, Is.EqualTo(new[] { 42 }));
            Assert.That(response.LinkedIssues, Is.EqualTo(new[] { "linked1", "linked2" }));
            Assert.That(response.ParentIssues, Has.Count.EqualTo(1));
            Assert.That(response.ParentIssues[0].ParentIssue, Is.EqualTo("parent1"));
            Assert.That(response.ParentIssues[0].SortOrder, Is.EqualTo("a0"));
            // Tags include all tags including keyed tags; LinkedPRs are extracted from hsp-linked-pr=N keyed tags
            Assert.That(response.Tags, Is.EqualTo(new[] { "tag1", "hsp-linked-pr=42" }));
            Assert.That(response.WorkingBranchId, Is.EqualTo("fix/test"));
            Assert.That(response.ExecutionMode, Is.EqualTo(ExecutionMode.Parallel));
            Assert.That(response.CreatedBy, Is.EqualTo("testuser"));
            Assert.That(response.AssignedTo, Is.EqualTo("dev1"));
            Assert.That(response.LastUpdate, Is.EqualTo(new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero)));
            Assert.That(response.CreatedAt, Is.EqualTo(new DateTimeOffset(2026, 1, 10, 8, 0, 0, TimeSpan.Zero)));
        });
    }

    [Test]
    public void ToResponse_HandlesNullOptionalProperties()
    {
        // Arrange
        var issue = new Issue
        {
            Id = "def456",
            Title = "Minimal issue",
            Status = IssueStatus.Open,
            Type = IssueType.Task,
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdate = DateTimeOffset.UtcNow
        };

        // Act
        var response = issue.ToResponse();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(response.Id, Is.EqualTo("def456"));
            Assert.That(response.Description, Is.Null);
            Assert.That(response.Priority, Is.Null);
            Assert.That(response.LinkedPRs, Is.Empty);
            Assert.That(response.WorkingBranchId, Is.Null);
            Assert.That(response.CreatedBy, Is.Null);
            Assert.That(response.AssignedTo, Is.Null);
            Assert.That(response.LinkedIssues, Is.Empty);
            Assert.That(response.ParentIssues, Is.Empty);
            Assert.That(response.Tags, Is.Empty);
        });
    }

    [Test]
    public void ToResponseList_MapsMultipleIssues()
    {
        // Arrange
        var issues = new[]
        {
            new Issue { Id = "a", Title = "First", Status = IssueStatus.Open, Type = IssueType.Task, CreatedAt = DateTimeOffset.UtcNow, LastUpdate = DateTimeOffset.UtcNow },
            new Issue { Id = "b", Title = "Second", Status = IssueStatus.Progress, Type = IssueType.Bug, CreatedAt = DateTimeOffset.UtcNow, LastUpdate = DateTimeOffset.UtcNow }
        };

        // Act
        var responses = issues.ToResponseList();

        // Assert
        Assert.That(responses, Has.Count.EqualTo(2));
        Assert.That(responses[0].Id, Is.EqualTo("a"));
        Assert.That(responses[1].Id, Is.EqualTo("b"));
    }

    [Test]
    public void IssueResponse_CanBeJsonSerialized_AndDeserialized()
    {
        // This test verifies the DTO can round-trip through JSON serialization
        // with web defaults (camelCase), which is the core fix for the Blazor WASM bug.
        var response = new IssueResponse
        {
            Id = "test123",
            Title = "Serialization test",
            Status = IssueStatus.Review,
            Type = IssueType.Feature,
            Priority = 3,
            ParentIssues = [new ParentIssueRefResponse { ParentIssue = "p1", SortOrder = "a0" }]
        };

        var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        var json = System.Text.Json.JsonSerializer.Serialize(response, options);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<IssueResponse>(json, options);

        Assert.That(deserialized, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(deserialized!.Id, Is.EqualTo("test123"));
            Assert.That(deserialized.Title, Is.EqualTo("Serialization test"));
            Assert.That(deserialized.Status, Is.EqualTo(IssueStatus.Review));
            Assert.That(deserialized.Type, Is.EqualTo(IssueType.Feature));
            Assert.That(deserialized.Priority, Is.EqualTo(3));
            Assert.That(deserialized.ParentIssues, Has.Count.EqualTo(1));
            Assert.That(deserialized.ParentIssues[0].ParentIssue, Is.EqualTo("p1"));
        });
    }
}
