using Fleece.Core.Models;
using Homespun.Features.Fleece.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.Fleece;

[TestFixture]
public class FleeceServiceTests
{
    private string _tempDir = null!;
    private Mock<ILogger<FleeceService>> _mockLogger = null!;
    private FleeceService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"fleece-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _mockLogger = new Mock<ILogger<FleeceService>>();
        _service = new FleeceService(_mockLogger.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _service.Dispose();

        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    #region CreateIssueAsync Tests

    [Test]
    public async Task CreateIssueAsync_WithTitleAndType_CreatesIssue()
    {
        // Arrange
        var title = "Test Issue";
        var type = IssueType.Feature;

        // Act
        var issue = await _service.CreateIssueAsync(_tempDir, title, type);

        // Assert
        Assert.That(issue, Is.Not.Null);
        Assert.That(issue.Title, Is.EqualTo(title));
        Assert.That(issue.Type, Is.EqualTo(type));
    }

    [Test]
    public async Task CreateIssueAsync_WithDescription_SetsDescription()
    {
        // Arrange
        var description = "This is a detailed description";

        // Act
        var issue = await _service.CreateIssueAsync(
            _tempDir,
            "Test Issue",
            IssueType.Task,
            description: description);

        // Assert
        Assert.That(issue.Description, Is.EqualTo(description));
    }

    [Test]
    public async Task CreateIssueAsync_WithPriority_SetsPriority()
    {
        // Arrange
        var priority = 3;

        // Act
        var issue = await _service.CreateIssueAsync(
            _tempDir,
            "Test Issue",
            IssueType.Bug,
            priority: priority);

        // Assert
        Assert.That(issue.Priority, Is.EqualTo(priority));
    }

    [Test]
    public async Task CreateIssueAsync_WithGroup_SetsGroup()
    {
        // Arrange
        var group = "backend";

        // Act
        var issue = await _service.CreateIssueAsync(
            _tempDir,
            "Test Issue",
            IssueType.Feature,
            group: group);

        // Assert
        Assert.That(issue.Group, Is.EqualTo(group));
    }

    [Test]
    public async Task CreateIssueAsync_WithNullGroup_GroupIsNull()
    {
        // Act
        var issue = await _service.CreateIssueAsync(
            _tempDir,
            "Test Issue",
            IssueType.Feature);

        // Assert
        Assert.That(issue.Group, Is.Null);
    }

    [Test]
    public async Task CreateIssueAsync_WithGroupAndAllOtherParams_SetsAllCorrectly()
    {
        // Arrange
        var title = "Full Feature Issue";
        var type = IssueType.Feature;
        var description = "A complete issue";
        var priority = 2;
        var group = "frontend";

        // Act
        var issue = await _service.CreateIssueAsync(
            _tempDir,
            title,
            type,
            description: description,
            priority: priority,
            group: group);

        // Assert
        Assert.That(issue.Title, Is.EqualTo(title));
        Assert.That(issue.Type, Is.EqualTo(type));
        Assert.That(issue.Description, Is.EqualTo(description));
        Assert.That(issue.Priority, Is.EqualTo(priority));
        Assert.That(issue.Group, Is.EqualTo(group));
    }

    [Test]
    public async Task CreateIssueAsync_IssueCanBeRetrieved()
    {
        // Arrange
        var group = "testing";
        var issue = await _service.CreateIssueAsync(
            _tempDir,
            "Retrievable Issue",
            IssueType.Task,
            group: group);

        // Act
        var retrieved = await _service.GetIssueAsync(_tempDir, issue.Id);

        // Assert
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved!.Group, Is.EqualTo(group));
    }

    #endregion
}
