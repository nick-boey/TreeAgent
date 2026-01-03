using TreeAgent.Web.Features.PullRequests;

namespace TreeAgent.Web.Tests.Models;

[TestFixture]
public class PullRequestInfoTests
{
    [Test]
    public void PullRequestInfo_CanCalculateTimeFromMergeOrder()
    {
        // Arrange - Create list of merged PRs ordered by merge time
        var now = DateTime.UtcNow;
        var mergedPrs = new List<PullRequestInfo>
        {
            new() { Number = 1, Title = "First", Status = PullRequestStatus.Merged, MergedAt = now.AddDays(-3) },
            new() { Number = 2, Title = "Second", Status = PullRequestStatus.Merged, MergedAt = now.AddDays(-2) },
            new() { Number = 3, Title = "Third", Status = PullRequestStatus.Merged, MergedAt = now.AddDays(-1) },
            new() { Number = 4, Title = "Most Recent", Status = PullRequestStatus.Merged, MergedAt = now }
        };

        // Act
        var times = PullRequestTimeCalculator.CalculateTimesForMergedPRs(mergedPrs);

        // Assert - Most recent has t=0, older have negative values
        Assert.That(times[4], Is.EqualTo(0));  // Most recent merge
        Assert.That(times[3], Is.EqualTo(-1));
        Assert.That(times[2], Is.EqualTo(-2));
        Assert.That(times[1], Is.EqualTo(-3)); // Oldest merge
    }

    [Test]
    public void PullRequestInfo_OpenPRsAlwaysHaveTimeOne()
    {
        // Arrange
        var openPrs = new List<PullRequestInfo>
        {
            new() { Number = 1, Title = "PR 1", Status = PullRequestStatus.InProgress },
            new() { Number = 2, Title = "PR 2", Status = PullRequestStatus.ReadyForReview },
            new() { Number = 3, Title = "PR 3", Status = PullRequestStatus.ChecksFailing },
            new() { Number = 4, Title = "PR 4", Status = PullRequestStatus.ReadyForMerging }
        };

        // Act & Assert - All open PRs have t=1
        foreach (var pr in openPrs)
        {
            var time = PullRequestTimeCalculator.CalculateTimeForOpenPR(pr);
            Assert.That(time, Is.EqualTo(1), $"Open PR with status {pr.Status} should have t=1");
        }
    }

    [Test]
    public void PullRequestInfo_ClosedNotMergedPRsHaveNegativeTime()
    {
        // Arrange - A closed PR that was not merged
        var closedPr = new PullRequestInfo
        {
            Number = 1,
            Title = "Closed PR",
            Status = PullRequestStatus.Closed,
            ClosedAt = DateTime.UtcNow.AddDays(-1)
        };

        // When calculating time for a closed PR, it should be placed
        // relative to merged PRs based on when it was closed
        var mergedPrs = new List<PullRequestInfo>
        {
            new() { Number = 2, Title = "Merged Before", Status = PullRequestStatus.Merged, MergedAt = DateTime.UtcNow.AddDays(-2) },
            new() { Number = 3, Title = "Merged After", Status = PullRequestStatus.Merged, MergedAt = DateTime.UtcNow }
        };

        // Act
        var time = PullRequestTimeCalculator.CalculateTimeForClosedPR(closedPr, mergedPrs);

        // Assert - Should be placed between -2 and 0 (around -1)
        Assert.That(time, Is.LessThan(0));
    }

    [Test]
    public void PullRequestInfo_MergedStatusRequiresMergedAt()
    {
        // Arrange
        var pr = new PullRequestInfo
        {
            Number = 1,
            Title = "Test",
            Status = PullRequestStatus.Merged,
            MergedAt = null
        };

        // Act & Assert
        Assert.That(pr.IsValid(), Is.False, "Merged PR without MergedAt should be invalid");
    }

    [Test]
    public void PullRequestInfo_OpenStatusShouldNotHaveMergedAt()
    {
        // Arrange
        var pr = new PullRequestInfo
        {
            Number = 1,
            Title = "Test",
            Status = PullRequestStatus.InProgress,
            MergedAt = DateTime.UtcNow // This is invalid for an open PR
        };

        // Act & Assert
        Assert.That(pr.IsValid(), Is.False, "Open PR with MergedAt should be invalid");
    }

    [Test]
    public void PullRequestInfo_ValidOpenPR()
    {
        // Arrange
        var pr = new PullRequestInfo
        {
            Number = 1,
            Title = "Test",
            Status = PullRequestStatus.InProgress,
            CreatedAt = DateTime.UtcNow
        };

        // Act & Assert
        Assert.That(pr.IsValid(), Is.True);
    }

    [Test]
    public void PullRequestInfo_ValidMergedPR()
    {
        // Arrange
        var pr = new PullRequestInfo
        {
            Number = 1,
            Title = "Test",
            Status = PullRequestStatus.Merged,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            MergedAt = DateTime.UtcNow
        };

        // Act & Assert
        Assert.That(pr.IsValid(), Is.True);
    }
}