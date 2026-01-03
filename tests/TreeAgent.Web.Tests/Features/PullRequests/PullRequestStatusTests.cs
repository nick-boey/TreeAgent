using TreeAgent.Web.Features.PullRequests;

namespace TreeAgent.Web.Tests.Features.PullRequests;

[TestFixture]
public class PullRequestStatusTests
{
    [Test]
    public void PullRequestStatus_AllStatusesHaveCorrectColors()
    {
        // Assert that all statuses have associated colors
        Assert.That(PullRequestStatusExtensions.GetColor(PullRequestStatus.InProgress), Is.EqualTo("yellow"));
        Assert.That(PullRequestStatusExtensions.GetColor(PullRequestStatus.ReadyForReview), Is.EqualTo("yellow-flashing"));
        Assert.That(PullRequestStatusExtensions.GetColor(PullRequestStatus.ChecksFailing), Is.EqualTo("red"));
        Assert.That(PullRequestStatusExtensions.GetColor(PullRequestStatus.Conflict), Is.EqualTo("orange"));
        Assert.That(PullRequestStatusExtensions.GetColor(PullRequestStatus.ReadyForMerging), Is.EqualTo("green"));
        Assert.That(PullRequestStatusExtensions.GetColor(PullRequestStatus.Merged), Is.EqualTo("purple"));
        Assert.That(PullRequestStatusExtensions.GetColor(PullRequestStatus.Closed), Is.EqualTo("red"));
    }

    [Test]
    public void PullRequestStatus_AllStatusesHaveDescriptions()
    {
        // Assert that all statuses have descriptions
        foreach (PullRequestStatus status in Enum.GetValues<PullRequestStatus>())
        {
            var description = PullRequestStatusExtensions.GetDescription(status);
            Assert.That(description, Is.Not.Null.And.Not.Empty, $"Status {status} should have a description");
        }
    }
}