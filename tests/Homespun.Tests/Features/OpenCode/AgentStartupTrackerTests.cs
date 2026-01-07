using Homespun.Features.OpenCode.Services;

namespace Homespun.Tests.Features.OpenCode;

[TestFixture]
public class AgentStartupTrackerTests
{
    private AgentStartupTracker _tracker = null!;

    [SetUp]
    public void SetUp()
    {
        _tracker = new AgentStartupTracker();
    }

    #region GetState Tests

    [Test]
    public void GetState_ReturnsNotStarted_WhenEntityNotTracked()
    {
        var state = _tracker.GetState("unknown-entity");

        Assert.That(state.State, Is.EqualTo(AgentStartupState.NotStarted));
        Assert.That(state.EntityId, Is.EqualTo("unknown-entity"));
        Assert.That(state.ErrorMessage, Is.Null);
    }

    [Test]
    public void GetState_ReturnsStarting_AfterMarkAsStarting()
    {
        _tracker.MarkAsStarting("entity-1");

        var state = _tracker.GetState("entity-1");

        Assert.That(state.State, Is.EqualTo(AgentStartupState.Starting));
        Assert.That(state.EntityId, Is.EqualTo("entity-1"));
    }

    [Test]
    public void GetState_ReturnsStarted_AfterMarkAsStarted()
    {
        _tracker.MarkAsStarting("entity-1");
        _tracker.MarkAsStarted("entity-1");

        var state = _tracker.GetState("entity-1");

        Assert.That(state.State, Is.EqualTo(AgentStartupState.Started));
    }

    [Test]
    public void GetState_ReturnsFailed_AfterMarkAsFailed()
    {
        _tracker.MarkAsStarting("entity-1");
        _tracker.MarkAsFailed("entity-1", "Connection timeout");

        var state = _tracker.GetState("entity-1");

        Assert.That(state.State, Is.EqualTo(AgentStartupState.Failed));
        Assert.That(state.ErrorMessage, Is.EqualTo("Connection timeout"));
    }

    #endregion

    #region State Transition Tests

    [Test]
    public void MarkAsStarting_SetsStateToStarting()
    {
        _tracker.MarkAsStarting("entity-1");

        var state = _tracker.GetState("entity-1");
        Assert.That(state.State, Is.EqualTo(AgentStartupState.Starting));
    }

    [Test]
    public void MarkAsStarted_SetsStateToStarted()
    {
        _tracker.MarkAsStarting("entity-1");
        _tracker.MarkAsStarted("entity-1");

        var state = _tracker.GetState("entity-1");
        Assert.That(state.State, Is.EqualTo(AgentStartupState.Started));
    }

    [Test]
    public void MarkAsFailed_SetsStateToFailed_WithErrorMessage()
    {
        _tracker.MarkAsStarting("entity-1");
        _tracker.MarkAsFailed("entity-1", "Server crashed");

        var state = _tracker.GetState("entity-1");
        Assert.That(state.State, Is.EqualTo(AgentStartupState.Failed));
        Assert.That(state.ErrorMessage, Is.EqualTo("Server crashed"));
    }

    [Test]
    public void ClearState_RemovesEntity()
    {
        _tracker.MarkAsStarting("entity-1");
        _tracker.ClearState("entity-1");

        var state = _tracker.GetState("entity-1");
        Assert.That(state.State, Is.EqualTo(AgentStartupState.NotStarted));
    }

    #endregion

    #region Event Tests

    [Test]
    public void StateChanged_FiresEvent_OnMarkAsStarting()
    {
        AgentStartupInfo? receivedInfo = null;
        _tracker.StateChanged += info => receivedInfo = info;

        _tracker.MarkAsStarting("entity-1");

        Assert.That(receivedInfo, Is.Not.Null);
        Assert.That(receivedInfo!.EntityId, Is.EqualTo("entity-1"));
        Assert.That(receivedInfo.State, Is.EqualTo(AgentStartupState.Starting));
    }

    [Test]
    public void StateChanged_FiresEvent_OnMarkAsStarted()
    {
        _tracker.MarkAsStarting("entity-1");

        AgentStartupInfo? receivedInfo = null;
        _tracker.StateChanged += info => receivedInfo = info;

        _tracker.MarkAsStarted("entity-1");

        Assert.That(receivedInfo, Is.Not.Null);
        Assert.That(receivedInfo!.State, Is.EqualTo(AgentStartupState.Started));
    }

    [Test]
    public void StateChanged_FiresEvent_OnMarkAsFailed()
    {
        _tracker.MarkAsStarting("entity-1");

        AgentStartupInfo? receivedInfo = null;
        _tracker.StateChanged += info => receivedInfo = info;

        _tracker.MarkAsFailed("entity-1", "Error occurred");

        Assert.That(receivedInfo, Is.Not.Null);
        Assert.That(receivedInfo!.State, Is.EqualTo(AgentStartupState.Failed));
        Assert.That(receivedInfo.ErrorMessage, Is.EqualTo("Error occurred"));
    }

    [Test]
    public void StateChanged_FiresEvent_OnClearState()
    {
        _tracker.MarkAsStarting("entity-1");

        AgentStartupInfo? receivedInfo = null;
        _tracker.StateChanged += info => receivedInfo = info;

        _tracker.ClearState("entity-1");

        Assert.That(receivedInfo, Is.Not.Null);
        Assert.That(receivedInfo!.State, Is.EqualTo(AgentStartupState.NotStarted));
    }

    #endregion

    #region GetAllStates Tests

    [Test]
    public void GetAllStates_ReturnsEmpty_WhenNoEntitiesTracked()
    {
        var states = _tracker.GetAllStates();

        Assert.That(states, Is.Empty);
    }

    [Test]
    public void GetAllStates_ReturnsAllTrackedEntities()
    {
        _tracker.MarkAsStarting("entity-1");
        _tracker.MarkAsStarting("entity-2");
        _tracker.MarkAsStarted("entity-2");

        var states = _tracker.GetAllStates();

        Assert.That(states, Has.Count.EqualTo(2));
        Assert.That(states.Any(s => s.EntityId == "entity-1" && s.State == AgentStartupState.Starting), Is.True);
        Assert.That(states.Any(s => s.EntityId == "entity-2" && s.State == AgentStartupState.Started), Is.True);
    }

    [Test]
    public void GetAllStates_ExcludesClearedEntities()
    {
        _tracker.MarkAsStarting("entity-1");
        _tracker.MarkAsStarting("entity-2");
        _tracker.ClearState("entity-1");

        var states = _tracker.GetAllStates();

        Assert.That(states, Has.Count.EqualTo(1));
        Assert.That(states[0].EntityId, Is.EqualTo("entity-2"));
    }

    #endregion

    #region Concurrent Access Tests

    [Test]
    public async Task ConcurrentAccess_IsThreadSafe()
    {
        var tasks = new List<Task>();
        var entityIds = Enumerable.Range(1, 100).Select(i => $"entity-{i}").ToList();

        // Start multiple entities concurrently
        foreach (var entityId in entityIds)
        {
            tasks.Add(Task.Run(() => _tracker.MarkAsStarting(entityId)));
        }

        await Task.WhenAll(tasks);

        // All should be in Starting state
        var states = _tracker.GetAllStates();
        Assert.That(states, Has.Count.EqualTo(100));
        Assert.That(states.All(s => s.State == AgentStartupState.Starting), Is.True);
    }

    [Test]
    public async Task ConcurrentStateTransitions_AreThreadSafe()
    {
        const int entityCount = 50;
        var entityIds = Enumerable.Range(1, entityCount).Select(i => $"entity-{i}").ToList();

        // First, mark all as starting
        foreach (var entityId in entityIds)
        {
            _tracker.MarkAsStarting(entityId);
        }

        // Concurrently transition half to started, half to failed
        var tasks = new List<Task>();
        for (var i = 0; i < entityCount; i++)
        {
            var entityId = entityIds[i];
            var index = i;
            tasks.Add(Task.Run(() =>
            {
                if (index % 2 == 0)
                    _tracker.MarkAsStarted(entityId);
                else
                    _tracker.MarkAsFailed(entityId, $"Error {index}");
            }));
        }

        await Task.WhenAll(tasks);

        var states = _tracker.GetAllStates();
        Assert.That(states, Has.Count.EqualTo(entityCount));

        var startedCount = states.Count(s => s.State == AgentStartupState.Started);
        var failedCount = states.Count(s => s.State == AgentStartupState.Failed);

        Assert.That(startedCount, Is.EqualTo(entityCount / 2));
        Assert.That(failedCount, Is.EqualTo(entityCount / 2));
    }

    #endregion

    #region IsStarting Helper Tests

    [Test]
    public void IsStarting_ReturnsTrue_WhenStateIsStarting()
    {
        _tracker.MarkAsStarting("entity-1");

        Assert.That(_tracker.IsStarting("entity-1"), Is.True);
    }

    [Test]
    public void IsStarting_ReturnsFalse_WhenStateIsNotStarting()
    {
        _tracker.MarkAsStarting("entity-1");
        _tracker.MarkAsStarted("entity-1");

        Assert.That(_tracker.IsStarting("entity-1"), Is.False);
    }

    [Test]
    public void IsStarting_ReturnsFalse_WhenEntityNotTracked()
    {
        Assert.That(_tracker.IsStarting("unknown"), Is.False);
    }

    #endregion

    #region HasFailed Helper Tests

    [Test]
    public void HasFailed_ReturnsTrue_WhenStateIsFailed()
    {
        _tracker.MarkAsStarting("entity-1");
        _tracker.MarkAsFailed("entity-1", "Error");

        Assert.That(_tracker.HasFailed("entity-1"), Is.True);
    }

    [Test]
    public void HasFailed_ReturnsFalse_WhenStateIsNotFailed()
    {
        _tracker.MarkAsStarting("entity-1");

        Assert.That(_tracker.HasFailed("entity-1"), Is.False);
    }

    #endregion
}
