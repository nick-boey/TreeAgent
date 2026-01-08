using Homespun.Features.Beads.Data;

namespace Homespun.Tests.Features.Beads;

[TestFixture]
public class BeadsUpdateOptionsTests
{
    [Test]
    public void BeadsUpdateOptions_HasAllRequiredProperties()
    {
        // Arrange & Act
        var options = new BeadsUpdateOptions
        {
            Title = "New Title",
            Description = "New Description",
            Type = BeadsIssueType.Bug,
            Status = BeadsIssueStatus.InProgress,
            Priority = 1,
            Assignee = "developer",
            ParentId = "bd-1234",
            LabelsToAdd = new List<string> { "label1", "label2" },
            LabelsToRemove = new List<string> { "old-label" }
        };

        // Assert
        Assert.That(options.Title, Is.EqualTo("New Title"));
        Assert.That(options.Description, Is.EqualTo("New Description"));
        Assert.That(options.Type, Is.EqualTo(BeadsIssueType.Bug));
        Assert.That(options.Status, Is.EqualTo(BeadsIssueStatus.InProgress));
        Assert.That(options.Priority, Is.EqualTo(1));
        Assert.That(options.Assignee, Is.EqualTo("developer"));
        Assert.That(options.ParentId, Is.EqualTo("bd-1234"));
        Assert.That(options.LabelsToAdd, Has.Count.EqualTo(2));
        Assert.That(options.LabelsToRemove, Has.Count.EqualTo(1));
    }

    [Test]
    public void BeadsUpdateOptions_AllPropertiesNullable()
    {
        // Arrange & Act
        var options = new BeadsUpdateOptions();

        // Assert - all properties should be null by default
        Assert.That(options.Title, Is.Null);
        Assert.That(options.Description, Is.Null);
        Assert.That(options.Type, Is.Null);
        Assert.That(options.Status, Is.Null);
        Assert.That(options.Priority, Is.Null);
        Assert.That(options.Assignee, Is.Null);
        Assert.That(options.ParentId, Is.Null);
        Assert.That(options.LabelsToAdd, Is.Null);
        Assert.That(options.LabelsToRemove, Is.Null);
    }

    [Test]
    public void BeadsUpdateOptions_SupportsPartialUpdate()
    {
        // Arrange & Act - only set Title and Type
        var options = new BeadsUpdateOptions
        {
            Title = "Just Title",
            Type = BeadsIssueType.Feature
        };

        // Assert
        Assert.That(options.Title, Is.EqualTo("Just Title"));
        Assert.That(options.Type, Is.EqualTo(BeadsIssueType.Feature));
        Assert.That(options.Description, Is.Null);
        Assert.That(options.Status, Is.Null);
        Assert.That(options.Priority, Is.Null);
        Assert.That(options.LabelsToAdd, Is.Null);
        Assert.That(options.LabelsToRemove, Is.Null);
    }
}
