using Homespun.Features.ClaudeCode.Data;
using Homespun.Tests.Helpers;

namespace Homespun.Tests.Features.ClaudeCode;

[TestFixture]
public class AgentPromptDataTests
{
    private TestDataStore _dataStore = null!;

    [SetUp]
    public void SetUp()
    {
        _dataStore = new TestDataStore();
    }

    [Test]
    public void AgentPrompts_InitiallyEmpty()
    {
        Assert.That(_dataStore.AgentPrompts, Is.Empty);
    }

    [Test]
    public async Task AddAgentPromptAsync_AddsPromptToStore()
    {
        var prompt = new AgentPrompt
        {
            Id = "test1",
            Name = "Test Prompt",
            InitialMessage = "Test message",
            Mode = SessionMode.Build
        };

        await _dataStore.AddAgentPromptAsync(prompt);

        Assert.Multiple(() =>
        {
            Assert.That(_dataStore.AgentPrompts, Has.Count.EqualTo(1));
            Assert.That(_dataStore.AgentPrompts[0].Id, Is.EqualTo("test1"));
            Assert.That(_dataStore.AgentPrompts[0].Name, Is.EqualTo("Test Prompt"));
        });
    }

    [Test]
    public async Task GetAgentPrompt_ReturnsPromptById()
    {
        var prompt = new AgentPrompt
        {
            Id = "test1",
            Name = "Test Prompt"
        };

        await _dataStore.AddAgentPromptAsync(prompt);

        var result = _dataStore.GetAgentPrompt("test1");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Name, Is.EqualTo("Test Prompt"));
    }

    [Test]
    public void GetAgentPrompt_ReturnsNullWhenNotFound()
    {
        var result = _dataStore.GetAgentPrompt("nonexistent");

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task UpdateAgentPromptAsync_UpdatesExistingPrompt()
    {
        var prompt = new AgentPrompt
        {
            Id = "test1",
            Name = "Original Name"
        };

        await _dataStore.AddAgentPromptAsync(prompt);

        prompt.Name = "Updated Name";
        await _dataStore.UpdateAgentPromptAsync(prompt);

        var result = _dataStore.GetAgentPrompt("test1");
        Assert.That(result!.Name, Is.EqualTo("Updated Name"));
    }

    [Test]
    public async Task RemoveAgentPromptAsync_RemovesPromptFromStore()
    {
        var prompt = new AgentPrompt
        {
            Id = "test1",
            Name = "Test Prompt"
        };

        await _dataStore.AddAgentPromptAsync(prompt);
        await _dataStore.RemoveAgentPromptAsync("test1");

        Assert.Multiple(() =>
        {
            Assert.That(_dataStore.AgentPrompts, Is.Empty);
            Assert.That(_dataStore.GetAgentPrompt("test1"), Is.Null);
        });
    }

    [Test]
    public async Task AddAgentPromptAsync_CanAddMultiplePrompts()
    {
        var prompt1 = new AgentPrompt { Id = "plan", Name = "Plan", Mode = SessionMode.Plan };
        var prompt2 = new AgentPrompt { Id = "build", Name = "Build", Mode = SessionMode.Build };

        await _dataStore.AddAgentPromptAsync(prompt1);
        await _dataStore.AddAgentPromptAsync(prompt2);

        Assert.Multiple(() =>
        {
            Assert.That(_dataStore.AgentPrompts, Has.Count.EqualTo(2));
            Assert.That(_dataStore.GetAgentPrompt("plan")!.Mode, Is.EqualTo(SessionMode.Plan));
            Assert.That(_dataStore.GetAgentPrompt("build")!.Mode, Is.EqualTo(SessionMode.Build));
        });
    }
}
