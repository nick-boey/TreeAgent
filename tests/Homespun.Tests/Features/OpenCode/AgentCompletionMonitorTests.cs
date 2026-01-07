using Homespun.Features.GitHub;
using Homespun.Features.OpenCode;
using Homespun.Features.OpenCode.Models;
using Homespun.Features.OpenCode.Services;
using Homespun.Features.PullRequests;
using Homespun.Features.PullRequests.Data;
using Homespun.Features.PullRequests.Data.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Homespun.Tests.Features.OpenCode;

[TestFixture]
public class AgentCompletionMonitorTests
{
    private Mock<IOpenCodeClient> _mockClient = null!;
    private Mock<IGitHubService> _mockGitHubService = null!;
    private Mock<ILogger<AgentCompletionMonitor>> _mockLogger = null!;
    private IOptions<AgentCompletionOptions> _options = null!;

    [SetUp]
    public void SetUp()
    {
        _mockClient = new Mock<IOpenCodeClient>();
        _mockGitHubService = new Mock<IGitHubService>();
        _mockLogger = new Mock<ILogger<AgentCompletionMonitor>>();
        _options = Options.Create(new AgentCompletionOptions
        {
            PrDetectionRetryCount = 3,
            PrDetectionRetryDelayMs = 100,
            PrDetectionTimeoutMs = 5000
        });
    }

    #region PR Detection Tests

    [Test]
    public void IsPrCreationEvent_ReturnsFalse_ForNonToolEvent()
    {
        var evt = new OpenCodeEvent
        {
            Type = OpenCodeEventTypes.MessageUpdated,
            Properties = new OpenCodeEventProperties { Content = "some content" }
        };

        var result = AgentCompletionMonitor.IsPrCreationEvent(evt);

        Assert.That(result, Is.False);
    }

    [Test]
    public void IsPrCreationEvent_ReturnsFalse_ForToolEventWithoutGhPrCreate()
    {
        var evt = new OpenCodeEvent
        {
            Type = OpenCodeEventTypes.ToolComplete,
            Properties = new OpenCodeEventProperties
            {
                ToolName = "bash",
                Content = "git commit -m 'test'"
            }
        };

        var result = AgentCompletionMonitor.IsPrCreationEvent(evt);

        Assert.That(result, Is.False);
    }

    [Test]
    public void IsPrCreationEvent_ReturnsTrue_ForBashToolWithGhPrCreate()
    {
        var evt = new OpenCodeEvent
        {
            Type = OpenCodeEventTypes.ToolComplete,
            Properties = new OpenCodeEventProperties
            {
                ToolName = "bash",
                Content = "gh pr create --base main --title 'Add feature'"
            }
        };

        var result = AgentCompletionMonitor.IsPrCreationEvent(evt);

        Assert.That(result, Is.True);
    }

    [Test]
    public void IsPrCreationEvent_ReturnsTrue_ForBashToolWithGhPrCreateCaseInsensitive()
    {
        var evt = new OpenCodeEvent
        {
            Type = OpenCodeEventTypes.ToolComplete,
            Properties = new OpenCodeEventProperties
            {
                ToolName = "Bash",
                Content = "GH PR CREATE --base main --title 'Add feature'"
            }
        };

        var result = AgentCompletionMonitor.IsPrCreationEvent(evt);

        Assert.That(result, Is.True);
    }

    [Test]
    public void IsPrCreationEvent_ReturnsFalse_WhenPropertiesNull()
    {
        var evt = new OpenCodeEvent
        {
            Type = OpenCodeEventTypes.ToolComplete,
            Properties = null
        };

        var result = AgentCompletionMonitor.IsPrCreationEvent(evt);

        Assert.That(result, Is.False);
    }

    #endregion

    #region ParsePrUrl Tests

    [Test]
    public void ParsePrUrl_ExtractsPrNumber_FromGitHubUrl()
    {
        var content = """
            Creating pull request...
            https://github.com/owner/repo/pull/42
            Done!
            """;

        var result = AgentCompletionMonitor.ParsePrUrl(content);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.PrNumber, Is.EqualTo(42));
        Assert.That(result.Url, Is.EqualTo("https://github.com/owner/repo/pull/42"));
    }

    [Test]
    public void ParsePrUrl_ReturnsNull_WhenNoPrUrl()
    {
        var content = "Some output without PR URL";

        var result = AgentCompletionMonitor.ParsePrUrl(content);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void ParsePrUrl_ExtractsPrNumber_FromMultipleLines()
    {
        var content = """
            Step 1: Commit changes
            Step 2: Push to remote
            Step 3: Create PR
            ? Title My Feature
            ? Body Implements feature
            https://github.com/myorg/myrepo/pull/123
            """;

        var result = AgentCompletionMonitor.ParsePrUrl(content);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.PrNumber, Is.EqualTo(123));
    }

    #endregion

    #region AgentCompletionResult Tests

    [Test]
    public void AgentCompletionResult_HasCorrectProperties()
    {
        var result = new AgentCompletionResult
        {
            Success = true,
            PrNumber = 42,
            PrUrl = "https://github.com/owner/repo/pull/42",
            BranchName = "feature/add-auth"
        };

        Assert.That(result.Success, Is.True);
        Assert.That(result.PrNumber, Is.EqualTo(42));
        Assert.That(result.PrUrl, Is.EqualTo("https://github.com/owner/repo/pull/42"));
        Assert.That(result.BranchName, Is.EqualTo("feature/add-auth"));
    }

    [Test]
    public void AgentCompletionResult_CanRepresentFailure()
    {
        var result = new AgentCompletionResult
        {
            Success = false,
            Error = "PR not found after retries"
        };

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.EqualTo("PR not found after retries"));
        Assert.That(result.PrNumber, Is.Null);
    }

    #endregion

    #region AgentCompletionOptions Tests

    [Test]
    public void AgentCompletionOptions_HasCorrectDefaults()
    {
        var options = new AgentCompletionOptions();

        Assert.That(options.PrDetectionRetryCount, Is.EqualTo(3));
        Assert.That(options.PrDetectionRetryDelayMs, Is.EqualTo(5000));
        Assert.That(options.PrDetectionTimeoutMs, Is.EqualTo(60000));
    }

    #endregion
}
