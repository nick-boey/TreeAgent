using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Tests.Helpers;

namespace Homespun.Tests.Features.ClaudeCode;

[TestFixture]
public class AgentPromptServiceTests
{
    private TestDataStore _dataStore = null!;
    private AgentPromptService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _dataStore = new TestDataStore();
        _service = new AgentPromptService(_dataStore);
    }

    [Test]
    public void GetAllPrompts_ReturnsEmptyListWhenNoPrompts()
    {
        var result = _service.GetAllPrompts();

        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetAllPrompts_ReturnsAllPrompts()
    {
        await _dataStore.AddAgentPromptAsync(new AgentPrompt { Id = "p1", Name = "Plan" });
        await _dataStore.AddAgentPromptAsync(new AgentPrompt { Id = "p2", Name = "Build" });

        var result = _service.GetAllPrompts();

        Assert.That(result, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task GetPrompt_ReturnsPromptById()
    {
        await _dataStore.AddAgentPromptAsync(new AgentPrompt { Id = "test1", Name = "Test" });

        var result = _service.GetPrompt("test1");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Name, Is.EqualTo("Test"));
    }

    [Test]
    public void GetPrompt_ReturnsNullForNonExistent()
    {
        var result = _service.GetPrompt("nonexistent");

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task CreatePromptAsync_CreatesNewPrompt()
    {
        var result = await _service.CreatePromptAsync("New Prompt", "Initial message", SessionMode.Build);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Name, Is.EqualTo("New Prompt"));
            Assert.That(result.InitialMessage, Is.EqualTo("Initial message"));
            Assert.That(result.Mode, Is.EqualTo(SessionMode.Build));
            Assert.That(_dataStore.AgentPrompts, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task UpdatePromptAsync_UpdatesExistingPrompt()
    {
        var original = await _service.CreatePromptAsync("Original", "Original message", SessionMode.Plan);

        var updated = await _service.UpdatePromptAsync(original.Id, "Updated", "Updated message", SessionMode.Build);

        Assert.Multiple(() =>
        {
            Assert.That(updated.Name, Is.EqualTo("Updated"));
            Assert.That(updated.InitialMessage, Is.EqualTo("Updated message"));
            Assert.That(updated.Mode, Is.EqualTo(SessionMode.Build));
        });
    }

    [Test]
    public async Task DeletePromptAsync_RemovesPrompt()
    {
        var prompt = await _service.CreatePromptAsync("ToDelete", null, SessionMode.Build);

        await _service.DeletePromptAsync(prompt.Id);

        Assert.That(_service.GetPrompt(prompt.Id), Is.Null);
    }

    #region Template Rendering Tests

    [Test]
    public void RenderTemplate_ReplacesPlaceholders()
    {
        var template = "Working on {{title}} ({{id}})";
        var context = new PromptContext
        {
            Title = "My Feature",
            Id = "abc123",
            Description = "A description",
            Branch = "feature/my-feature",
            Type = "Feature"
        };

        var result = _service.RenderTemplate(template, context);

        Assert.That(result, Is.EqualTo("Working on My Feature (abc123)"));
    }

    [Test]
    public void RenderTemplate_ReplacesAllPlaceholders()
    {
        var template = "{{title}}\nID: {{id}}\nType: {{type}}\nBranch: {{branch}}\nDescription: {{description}}";
        var context = new PromptContext
        {
            Title = "Test Feature",
            Id = "XYZ789",
            Description = "This is a test",
            Branch = "test-branch",
            Type = "Bug"
        };

        var result = _service.RenderTemplate(template, context);

        Assert.That(result, Does.Contain("Test Feature"));
        Assert.That(result, Does.Contain("XYZ789"));
        Assert.That(result, Does.Contain("Bug"));
        Assert.That(result, Does.Contain("test-branch"));
        Assert.That(result, Does.Contain("This is a test"));
    }

    [Test]
    public void RenderTemplate_HandlesNullDescription()
    {
        var template = "Title: {{title}}, Description: {{description}}";
        var context = new PromptContext
        {
            Title = "Test",
            Id = "123",
            Description = null,
            Branch = "branch",
            Type = "Task"
        };

        var result = _service.RenderTemplate(template, context);

        Assert.That(result, Is.EqualTo("Title: Test, Description: "));
    }

    [Test]
    public void RenderTemplate_HandlesNullTemplate()
    {
        var context = new PromptContext
        {
            Title = "Test",
            Id = "123",
            Description = null,
            Branch = "branch",
            Type = "Task"
        };

        var result = _service.RenderTemplate(null, context);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void RenderTemplate_IsCaseInsensitive()
    {
        var template = "{{TITLE}} and {{Title}} and {{title}}";
        var context = new PromptContext
        {
            Title = "Test",
            Id = "123",
            Description = null,
            Branch = "branch",
            Type = "Task"
        };

        var result = _service.RenderTemplate(template, context);

        Assert.That(result, Is.EqualTo("Test and Test and Test"));
    }

    #endregion

    #region Default Prompts Tests

    [Test]
    public async Task EnsureDefaultPromptsAsync_CreatesDefaultsWhenEmpty()
    {
        await _service.EnsureDefaultPromptsAsync();

        var prompts = _service.GetAllPrompts();
        Assert.That(prompts, Has.Count.EqualTo(3)); // Plan, Build, Rebase
        Assert.That(prompts.Any(p => p.Name == "Plan"), Is.True);
        Assert.That(prompts.Any(p => p.Name == "Build"), Is.True);
        Assert.That(prompts.Any(p => p.Name == "Rebase"), Is.True);
    }

    [Test]
    public async Task EnsureDefaultPromptsAsync_DoesNotDuplicateExisting()
    {
        await _service.CreatePromptAsync("CustomPrompt", "Custom message", SessionMode.Build);

        await _service.EnsureDefaultPromptsAsync();

        var prompts = _service.GetAllPrompts();
        Assert.That(prompts, Has.Count.EqualTo(4)); // Custom + 3 defaults (Plan, Build, Rebase)
    }

    [Test]
    public async Task EnsureDefaultPromptsAsync_SetsCorrectModes()
    {
        await _service.EnsureDefaultPromptsAsync();

        var planPrompt = _service.GetAllPrompts().FirstOrDefault(p => p.Name == "Plan");
        var buildPrompt = _service.GetAllPrompts().FirstOrDefault(p => p.Name == "Build");
        var rebasePrompt = _service.GetAllPrompts().FirstOrDefault(p => p.Name == "Rebase");

        Assert.Multiple(() =>
        {
            Assert.That(planPrompt!.Mode, Is.EqualTo(SessionMode.Plan));
            Assert.That(buildPrompt!.Mode, Is.EqualTo(SessionMode.Build));
            Assert.That(rebasePrompt!.Mode, Is.EqualTo(SessionMode.Build));
        });
    }

    #endregion
}
