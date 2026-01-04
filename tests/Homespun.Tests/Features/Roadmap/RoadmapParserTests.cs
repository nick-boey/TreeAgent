using Homespun.Features.Roadmap;

namespace Homespun.Tests.Features.Roadmap;

[TestFixture]
public class RoadmapParserTests
{
    [Test]
    public void RoadmapParser_ValidJson_ParsesAllChanges()
    {
        // Arrange
        var json = """
        {
            "version": "1.0",
            "lastUpdated": "2024-01-15T10:00:00Z",
            "changes": [
                {
                    "id": "feature-one",
                    "group": "core",
                    "type": "feature",
                    "title": "First Feature"
                },
                {
                    "id": "feature-two",
                    "group": "web",
                    "type": "bug",
                    "title": "Bug Fix"
                }
            ]
        }
        """;

        // Act
        var result = RoadmapParser.Parse(json);

        // Assert
        Assert.That(result.Version, Is.EqualTo("1.0"));
        Assert.That(result.Changes, Has.Count.EqualTo(2));
        Assert.That(result.Changes[0].Id, Is.EqualTo("feature-one"));
        Assert.That(result.Changes[0].Group, Is.EqualTo("core"));
        Assert.That(result.Changes[0].Type, Is.EqualTo(ChangeType.Feature));
        Assert.That(result.Changes[0].Title, Is.EqualTo("First Feature"));
        Assert.That(result.Changes[1].Id, Is.EqualTo("feature-two"));
        Assert.That(result.Changes[1].Group, Is.EqualTo("web"));
        Assert.That(result.Changes[1].Type, Is.EqualTo(ChangeType.Bug));
    }

    [Test]
    public void RoadmapParser_InvalidJson_ThrowsValidationException()
    {
        // Arrange
        var invalidJson = "{ this is not valid json }";

        // Act & Assert
        Assert.Throws<RoadmapValidationException>(() => RoadmapParser.Parse(invalidJson));
    }

    [Test]
    public void RoadmapParser_MissingRequiredFields_ThrowsValidationException()
    {
        // Arrange - Missing 'id' field
        var json = """
        {
            "version": "1.0",
            "changes": [
                {
                    "group": "core",
                    "type": "feature",
                    "title": "Missing ID"
                }
            ]
        }
        """;

        // Act & Assert
        var ex = Assert.Throws<RoadmapValidationException>(() => RoadmapParser.Parse(json));
        Assert.That(ex.Message, Does.Contain("id"));
    }

    [Test]
    public void RoadmapParser_MissingVersion_ThrowsValidationException()
    {
        // Arrange
        var json = """
        {
            "changes": [
                {
                    "id": "test",
                    "group": "core",
                    "type": "feature",
                    "title": "Test"
                }
            ]
        }
        """;

        // Act & Assert
        var ex = Assert.Throws<RoadmapValidationException>(() => RoadmapParser.Parse(json));
        Assert.That(ex.Message, Does.Contain("version"));
    }

    [Test]
    public void RoadmapParser_ParsesNestedChildren_AsTree()
    {
        // Arrange
        var json = """
        {
            "version": "1.0",
            "changes": [
                {
                    "id": "parent",
                    "group": "core",
                    "type": "feature",
                    "title": "Parent Feature",
                    "children": [
                        {
                            "id": "child-1",
                            "group": "core",
                            "type": "feature",
                            "title": "Child 1",
                            "children": [
                                {
                                    "id": "grandchild",
                                    "group": "core",
                                    "type": "refactor",
                                    "title": "Grandchild"
                                }
                            ]
                        },
                        {
                            "id": "child-2",
                            "group": "core",
                            "type": "bug",
                            "title": "Child 2"
                        }
                    ]
                }
            ]
        }
        """;

        // Act
        var result = RoadmapParser.Parse(json);

        // Assert
        Assert.That(result.Changes, Has.Count.EqualTo(1));

        var parent = result.Changes[0];
        Assert.That(parent.Id, Is.EqualTo("parent"));
        Assert.That(parent.Children, Has.Count.EqualTo(2));

        var child1 = parent.Children[0];
        Assert.That(child1.Id, Is.EqualTo("child-1"));
        Assert.That(child1.Children, Has.Count.EqualTo(1));

        var grandchild = child1.Children[0];
        Assert.That(grandchild.Id, Is.EqualTo("grandchild"));
        Assert.That(grandchild.Type, Is.EqualTo(ChangeType.Refactor));

        var child2 = parent.Children[1];
        Assert.That(child2.Id, Is.EqualTo("child-2"));
        Assert.That(child2.Children, Is.Empty);
    }

    [Test]
    public void RoadmapParser_CalculatesTimeFromTreeDepth()
    {
        // Arrange
        var json = """
        {
            "version": "1.0",
            "changes": [
                {
                    "id": "root",
                    "group": "core",
                    "type": "feature",
                    "title": "Root",
                    "children": [
                        {
                            "id": "child",
                            "group": "core",
                            "type": "feature",
                            "title": "Child",
                            "children": [
                                {
                                    "id": "grandchild",
                                    "group": "core",
                                    "type": "feature",
                                    "title": "Grandchild"
                                }
                            ]
                        }
                    ]
                }
            ]
        }
        """;

        // Act
        var result = RoadmapParser.Parse(json);
        var flatChanges = result.GetAllChangesWithTime();

        // Assert - Root at depth 0 -> t=2, child at depth 1 -> t=3, grandchild at depth 2 -> t=4
        Assert.That(flatChanges.First(c => c.Change.Id == "root").Time, Is.EqualTo(2));
        Assert.That(flatChanges.First(c => c.Change.Id == "child").Time, Is.EqualTo(3));
        Assert.That(flatChanges.First(c => c.Change.Id == "grandchild").Time, Is.EqualTo(4));
    }

    [Test]
    public void RoadmapParser_ParsesOptionalFields()
    {
        // Arrange
        var json = """
        {
            "version": "1.0",
            "changes": [
                {
                    "id": "full-feature",
                    "group": "core",
                    "type": "feature",
                    "title": "Full Feature",
                    "description": "A detailed description",
                    "instructions": "Implementation instructions for the agent",
                    "priority": "high",
                    "estimatedComplexity": "large"
                }
            ]
        }
        """;

        // Act
        var result = RoadmapParser.Parse(json);
        var change = result.Changes[0];

        // Assert
        Assert.That(change.Description, Is.EqualTo("A detailed description"));
        Assert.That(change.Instructions, Is.EqualTo("Implementation instructions for the agent"));
        Assert.That(change.Priority, Is.EqualTo(Priority.High));
        Assert.That(change.EstimatedComplexity, Is.EqualTo(Complexity.Large));
    }

    [Test]
    public void RoadmapParser_InvalidIdPattern_ThrowsValidationException()
    {
        // Arrange - ID with invalid characters (should be lowercase alphanumeric with hyphens)
        var json = """
        {
            "version": "1.0",
            "changes": [
                {
                    "id": "Invalid_ID",
                    "group": "core",
                    "type": "feature",
                    "title": "Test"
                }
            ]
        }
        """;

        // Act & Assert
        var ex = Assert.Throws<RoadmapValidationException>(() => RoadmapParser.Parse(json));
        Assert.That(ex.Message, Does.Contain("id").Or.Contain("pattern"));
    }

    [Test]
    public void RoadmapParser_InvalidType_ThrowsValidationException()
    {
        // Arrange
        var json = """
        {
            "version": "1.0",
            "changes": [
                {
                    "id": "test",
                    "group": "core",
                    "type": "invalid-type",
                    "title": "Test"
                }
            ]
        }
        """;

        // Act & Assert
        var ex = Assert.Throws<RoadmapValidationException>(() => RoadmapParser.Parse(json));
        Assert.That(ex.Message, Does.Contain("type"));
    }

    [Test]
    public void RoadmapParser_EmptyChanges_IsValid()
    {
        // Arrange
        var json = """
        {
            "version": "1.0",
            "changes": []
        }
        """;

        // Act
        var result = RoadmapParser.Parse(json);

        // Assert
        Assert.That(result.Changes, Is.Empty);
    }

    [Test]
    public void RoadmapParser_GeneratesBranchName()
    {
        // Arrange
        var json = """
        {
            "version": "1.0",
            "changes": [
                {
                    "id": "pr-time-dimension",
                    "group": "core",
                    "type": "feature",
                    "title": "PR Time Dimension"
                }
            ]
        }
        """;

        // Act
        var result = RoadmapParser.Parse(json);
        var change = result.Changes[0];

        // Assert
        Assert.That(change.GetBranchName(), Is.EqualTo("core/feature/pr-time-dimension"));
    }

    [Test]
    public void RoadmapParser_Serialize_RoundTrips()
    {
        // Arrange
        var roadmap = new Homespun.Features.Roadmap.Roadmap
        {
            Version = "1.0",
            LastUpdated = DateTime.UtcNow,
            Changes =
            [
                new RoadmapChange
                {
                    Id = "test-feature",
                    Group = "core",
                    Type = ChangeType.Feature,
                    Title = "Test Feature",
                    Description = "Description",
                    Priority = Priority.High,
                    Children =
                    [
                        new RoadmapChange
                        {
                            Id = "child-feature",
                            Group = "core",
                            Type = ChangeType.Bug,
                            Title = "Child Feature"
                        }
                    ]
                }
            ]
        };

        // Act
        var json = RoadmapParser.Serialize(roadmap);
        var parsed = RoadmapParser.Parse(json);

        // Assert
        Assert.That(parsed.Version, Is.EqualTo(roadmap.Version));
        Assert.That(parsed.Changes, Has.Count.EqualTo(1));
        Assert.That(parsed.Changes[0].Id, Is.EqualTo("test-feature"));
        Assert.That(parsed.Changes[0].Children, Has.Count.EqualTo(1));
        Assert.That(parsed.Changes[0].Children[0].Id, Is.EqualTo("child-feature"));
    }
}
