using Homespun.Features.PullRequests;

namespace Homespun.Tests.Features.PullRequests;

[TestFixture]
public class BranchNameParserTests
{
    #region ExtractIssueId Tests

    [Test]
    public void ExtractIssueId_WithValidBranchFormat_ReturnsIssueId()
    {
        // Arrange
        var branchName = "issues/feature/link-issues-to-prs+hsp-kca";

        // Act
        var result = BranchNameParser.ExtractIssueId(branchName);

        // Assert
        Assert.That(result, Is.EqualTo("hsp-kca"));
    }

    [Test]
    public void ExtractIssueId_WithBdPrefix_ReturnsIssueId()
    {
        // Arrange
        var branchName = "frontend/feature/update-page+bd-a3f8";

        // Act
        var result = BranchNameParser.ExtractIssueId(branchName);

        // Assert
        Assert.That(result, Is.EqualTo("bd-a3f8"));
    }

    [Test]
    public void ExtractIssueId_WithNoPlusSign_ReturnsNull()
    {
        // Arrange
        var branchName = "feature/some-feature";

        // Act
        var result = BranchNameParser.ExtractIssueId(branchName);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void ExtractIssueId_WithNullBranchName_ReturnsNull()
    {
        // Act
        var result = BranchNameParser.ExtractIssueId(null);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void ExtractIssueId_WithEmptyBranchName_ReturnsNull()
    {
        // Act
        var result = BranchNameParser.ExtractIssueId("");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void ExtractIssueId_WithWhitespaceBranchName_ReturnsNull()
    {
        // Act
        var result = BranchNameParser.ExtractIssueId("   ");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void ExtractIssueId_WithPlusButNoIssueId_ReturnsNull()
    {
        // Arrange - plus sign but nothing after
        var branchName = "feature/test+";

        // Act
        var result = BranchNameParser.ExtractIssueId(branchName);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void ExtractIssueId_WithMultiplePlusSigns_ReturnsLastSegment()
    {
        // Arrange - edge case with multiple plus signs
        var branchName = "feature/test+extra+hsp-123";

        // Act
        var result = BranchNameParser.ExtractIssueId(branchName);

        // Assert
        Assert.That(result, Is.EqualTo("hsp-123"));
    }

    [Test]
    public void ExtractIssueId_WithIssueIdContainingNumbers_ReturnsIssueId()
    {
        // Arrange
        var branchName = "backend/bugfix/fix-auth+hsp-12345";

        // Act
        var result = BranchNameParser.ExtractIssueId(branchName);

        // Assert
        Assert.That(result, Is.EqualTo("hsp-12345"));
    }

    [Test]
    public void ExtractIssueId_WithOnlyIssueIdAfterPlus_ReturnsIssueId()
    {
        // Arrange - minimal branch name
        var branchName = "main+hsp-abc";

        // Act
        var result = BranchNameParser.ExtractIssueId(branchName);

        // Assert
        Assert.That(result, Is.EqualTo("hsp-abc"));
    }

    #endregion

    #region GetPrLabel Tests

    [Test]
    public void GetPrLabel_WithValidPrNumber_ReturnsFormattedLabel()
    {
        // Act
        var result = BranchNameParser.GetPrLabel(123);

        // Assert
        Assert.That(result, Is.EqualTo("hsp:pr-123"));
    }

    [Test]
    public void GetPrLabel_WithSingleDigit_ReturnsFormattedLabel()
    {
        // Act
        var result = BranchNameParser.GetPrLabel(1);

        // Assert
        Assert.That(result, Is.EqualTo("hsp:pr-1"));
    }

    [Test]
    public void GetPrLabel_WithLargeNumber_ReturnsFormattedLabel()
    {
        // Act
        var result = BranchNameParser.GetPrLabel(999999);

        // Assert
        Assert.That(result, Is.EqualTo("hsp:pr-999999"));
    }

    #endregion

    #region TryParsePrNumber Tests

    [Test]
    public void TryParsePrNumber_WithValidLabel_ReturnsTrueAndPrNumber()
    {
        // Arrange
        var label = "hsp:pr-123";

        // Act
        var result = BranchNameParser.TryParsePrNumber(label, out var prNumber);

        // Assert
        Assert.That(result, Is.True);
        Assert.That(prNumber, Is.EqualTo(123));
    }

    [Test]
    public void TryParsePrNumber_WithInvalidLabel_ReturnsFalse()
    {
        // Arrange
        var label = "some-other-label";

        // Act
        var result = BranchNameParser.TryParsePrNumber(label, out var prNumber);

        // Assert
        Assert.That(result, Is.False);
        Assert.That(prNumber, Is.EqualTo(0));
    }

    [Test]
    public void TryParsePrNumber_WithNullLabel_ReturnsFalse()
    {
        // Act
        var result = BranchNameParser.TryParsePrNumber(null, out var prNumber);

        // Assert
        Assert.That(result, Is.False);
        Assert.That(prNumber, Is.EqualTo(0));
    }

    [Test]
    public void TryParsePrNumber_WithPartialMatch_ReturnsFalse()
    {
        // Arrange - has prefix but no number
        var label = "hsp:pr-abc";

        // Act
        var result = BranchNameParser.TryParsePrNumber(label, out var prNumber);

        // Assert
        Assert.That(result, Is.False);
        Assert.That(prNumber, Is.EqualTo(0));
    }

    [Test]
    public void TryParsePrNumber_WithExtraText_ReturnsFalse()
    {
        // Arrange - has number but extra text
        var label = "hsp:pr-123-extra";

        // Act
        var result = BranchNameParser.TryParsePrNumber(label, out var prNumber);

        // Assert
        Assert.That(result, Is.False);
        Assert.That(prNumber, Is.EqualTo(0));
    }

    #endregion

    #region IsPrLabel Tests

    [Test]
    public void IsPrLabel_WithValidPrLabel_ReturnsTrue()
    {
        // Arrange
        var label = "hsp:pr-123";

        // Act
        var result = BranchNameParser.IsPrLabel(label);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void IsPrLabel_WithOtherLabel_ReturnsFalse()
    {
        // Arrange
        var label = "awaiting-pr";

        // Act
        var result = BranchNameParser.IsPrLabel(label);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void IsPrLabel_WithNullLabel_ReturnsFalse()
    {
        // Act
        var result = BranchNameParser.IsPrLabel(null);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void IsPrLabel_WithEmptyLabel_ReturnsFalse()
    {
        // Act
        var result = BranchNameParser.IsPrLabel("");

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion

    #region HasPrLabel Tests

    [Test]
    public void HasPrLabel_WithPrLabelInList_ReturnsTrue()
    {
        // Arrange
        var labels = new List<string> { "feature", "hsp:pr-123", "urgent" };

        // Act
        var result = BranchNameParser.HasPrLabel(labels);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void HasPrLabel_WithoutPrLabelInList_ReturnsFalse()
    {
        // Arrange
        var labels = new List<string> { "feature", "bug", "urgent" };

        // Act
        var result = BranchNameParser.HasPrLabel(labels);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void HasPrLabel_WithEmptyList_ReturnsFalse()
    {
        // Arrange
        var labels = new List<string>();

        // Act
        var result = BranchNameParser.HasPrLabel(labels);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void HasPrLabel_WithNullList_ReturnsFalse()
    {
        // Act
        var result = BranchNameParser.HasPrLabel(null);

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion
}
