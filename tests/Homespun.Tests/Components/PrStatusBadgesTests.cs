using Bunit;
using Homespun.Components.Shared;
using Homespun.Features.GitHub;
using Homespun.Features.PullRequests;

namespace Homespun.Tests.Components;

/// <summary>
/// bUnit tests for the PrStatusBadges component.
/// </summary>
[TestFixture]
public class PrStatusBadgesTests : BunitTestContext
{
    [Test]
    public void PrStatusBadges_NullStatus_RendersNothing()
    {
        // Act
        var cut = Render<PrStatusBadges>(parameters =>
            parameters.Add(p => p.Status, null));

        // Assert
        Assert.That(cut.Markup, Is.Empty);
    }

    [Test]
    public void PrStatusBadges_WithStatus_RendersPrNumber()
    {
        // Arrange
        var status = CreateStatus(prNumber: 42, url: "https://github.com/test/repo/pull/42");

        // Act
        var cut = Render<PrStatusBadges>(parameters =>
            parameters.Add(p => p.Status, status));

        // Assert
        Assert.That(cut.Markup, Does.Contain("PR #42"));
    }

    [Test]
    public void PrStatusBadges_WithStatus_RendersPrLink()
    {
        // Arrange
        var status = CreateStatus(prNumber: 99, url: "https://github.com/owner/repo/pull/99");

        // Act
        var cut = Render<PrStatusBadges>(parameters =>
            parameters.Add(p => p.Status, status));

        // Assert
        var link = cut.Find("a[target='_blank']");
        Assert.That(link.GetAttribute("href"), Is.EqualTo("https://github.com/owner/repo/pull/99"));
    }

    [Test]
    public void PrStatusBadges_InProgressStatus_ShowsInProgressBadge()
    {
        // Arrange
        var status = CreateStatus(prStatus: PullRequestStatus.InProgress);

        // Act
        var cut = Render<PrStatusBadges>(parameters =>
            parameters.Add(p => p.Status, status));

        // Assert
        Assert.That(cut.Markup, Does.Contain("In Progress"));
        Assert.That(cut.Markup, Does.Contain("bg-primary"));
    }

    [Test]
    public void PrStatusBadges_ReadyForReviewStatus_ShowsReadyForReviewBadge()
    {
        // Arrange
        var status = CreateStatus(prStatus: PullRequestStatus.ReadyForReview);

        // Act
        var cut = Render<PrStatusBadges>(parameters =>
            parameters.Add(p => p.Status, status));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Ready for Review"));
        Assert.That(cut.Markup, Does.Contain("bg-warning"));
    }

    [Test]
    public void PrStatusBadges_ChecksFailingStatus_ShowsChecksFailing()
    {
        // Arrange
        var status = CreateStatus(prStatus: PullRequestStatus.ChecksFailing);

        // Act
        var cut = Render<PrStatusBadges>(parameters =>
            parameters.Add(p => p.Status, status));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Checks Failing"));
        Assert.That(cut.Markup, Does.Contain("bg-danger"));
    }

    [Test]
    public void PrStatusBadges_ReadyForMergingStatus_ShowsReadyToMerge()
    {
        // Arrange
        var status = CreateStatus(prStatus: PullRequestStatus.ReadyForMerging);

        // Act
        var cut = Render<PrStatusBadges>(parameters =>
            parameters.Add(p => p.Status, status));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Ready to Merge"));
        Assert.That(cut.Markup, Does.Contain("bg-success"));
    }

    [Test]
    public void PrStatusBadges_ChecksPassingTrue_ShowsChecksPassingBadge()
    {
        // Arrange
        var status = CreateStatus(checksPassing: true);

        // Act
        var cut = Render<PrStatusBadges>(parameters =>
            parameters.Add(p => p.Status, status));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Checks Passing"));
    }

    [Test]
    public void PrStatusBadges_ChecksPassingFalse_ShowsChecksFailingBadge()
    {
        // Arrange
        var status = CreateStatus(checksPassing: false);

        // Act
        var cut = Render<PrStatusBadges>(parameters =>
            parameters.Add(p => p.Status, status));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Checks Failing"));
    }

    [Test]
    public void PrStatusBadges_Approved_ShowsApprovedBadgeWithCount()
    {
        // Arrange
        var status = CreateStatus(isApproved: true, approvalCount: 2);

        // Act
        var cut = Render<PrStatusBadges>(parameters =>
            parameters.Add(p => p.Status, status));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Approved (2)"));
    }

    [Test]
    public void PrStatusBadges_ChangesRequested_ShowsChangesRequestedBadge()
    {
        // Arrange
        var status = CreateStatus(changesRequestedCount: 1);

        // Act
        var cut = Render<PrStatusBadges>(parameters =>
            parameters.Add(p => p.Status, status));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Changes Requested (1)"));
    }

    [Test]
    public void PrStatusBadges_Mergeable_ShowsReadyToMergeBadge()
    {
        // Arrange
        var status = CreateStatus(prStatus: PullRequestStatus.ReadyForMerging);

        // Act
        var cut = Render<PrStatusBadges>(parameters =>
            parameters.Add(p => p.Status, status));

        // Assert
        // IsMergeable is true when Status == ReadyForMerging
        Assert.That(cut.Markup, Does.Contain("Ready to Merge"));
    }

    [Test]
    public void PrStatusBadges_HasConflicts_ShowsMergeConflictsBadge()
    {
        // Arrange
        var status = CreateStatus(prStatus: PullRequestStatus.Conflict);

        // Act
        var cut = Render<PrStatusBadges>(parameters =>
            parameters.Add(p => p.Status, status));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Merge Conflicts"));
    }

    [Test]
    public void PrStatusBadges_ChecksRunning_ShowsSpinner()
    {
        // Arrange - ChecksRunning is true when ChecksPassing is null and Status is InProgress
        var status = CreateStatus(prStatus: PullRequestStatus.InProgress, checksPassing: null);

        // Act
        var cut = Render<PrStatusBadges>(parameters =>
            parameters.Add(p => p.Status, status));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Checks Running"));
        Assert.That(cut.Markup, Does.Contain("spinner-border"));
    }

    [Test]
    public void PrStatusBadges_WithCssClass_AppliesClass()
    {
        // Arrange
        var status = CreateStatus();

        // Act
        var cut = Render<PrStatusBadges>(parameters =>
            parameters
                .Add(p => p.Status, status)
                .Add(p => p.CssClass, "mb-3 custom-class"));

        // Assert
        var container = cut.Find(".pr-status-section");
        Assert.That(container.ClassList, Does.Contain("mb-3"));
        Assert.That(container.ClassList, Does.Contain("custom-class"));
    }

    [Test]
    public void PrStatusBadges_MergedStatus_ShowsMergedBadge()
    {
        // Arrange
        var status = CreateStatus(prStatus: PullRequestStatus.Merged);

        // Act
        var cut = Render<PrStatusBadges>(parameters =>
            parameters.Add(p => p.Status, status));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Merged"));
        Assert.That(cut.Markup, Does.Contain("bg-purple"));
    }

    [Test]
    public void PrStatusBadges_ClosedStatus_ShowsClosedBadge()
    {
        // Arrange
        var status = CreateStatus(prStatus: PullRequestStatus.Closed);

        // Act
        var cut = Render<PrStatusBadges>(parameters =>
            parameters.Add(p => p.Status, status));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Closed"));
        Assert.That(cut.Markup, Does.Contain("bg-secondary"));
    }

    [Test]
    public void PrStatusBadges_ContainsPullRequestHeader()
    {
        // Arrange
        var status = CreateStatus();

        // Act
        var cut = Render<PrStatusBadges>(parameters =>
            parameters.Add(p => p.Status, status));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Pull Request"));
    }

    #region Helper Methods

    private static IssuePullRequestStatus CreateStatus(
        int prNumber = 1,
        string url = "https://github.com/test/repo/pull/1",
        PullRequestStatus prStatus = PullRequestStatus.InProgress,
        bool? checksPassing = null,
        bool? isApproved = null,
        int approvalCount = 0,
        int changesRequestedCount = 0)
    {
        return new IssuePullRequestStatus
        {
            PrNumber = prNumber,
            PrUrl = url,
            Status = prStatus,
            ChecksPassing = checksPassing,
            IsApproved = isApproved,
            ApprovalCount = approvalCount,
            ChangesRequestedCount = changesRequestedCount
        };
    }

    #endregion
}
