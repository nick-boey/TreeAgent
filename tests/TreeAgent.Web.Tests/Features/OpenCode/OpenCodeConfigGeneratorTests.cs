using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using TreeAgent.Web.Features.OpenCode;
using TreeAgent.Web.Features.OpenCode.Models;
using TreeAgent.Web.Features.OpenCode.Services;

namespace TreeAgent.Web.Tests.Features.OpenCode;

[TestFixture]
public class OpenCodeConfigGeneratorTests
{
    private Mock<ILogger<OpenCodeConfigGenerator>> _mockLogger = null!;
    private IOptions<OpenCodeOptions> _options = null!;
    private OpenCodeConfigGenerator _generator = null!;
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _mockLogger = new Mock<ILogger<OpenCodeConfigGenerator>>();
        _options = Options.Create(new OpenCodeOptions
        {
            DefaultModel = "anthropic/claude-sonnet-4-5"
        });
        _generator = new OpenCodeConfigGenerator(_options, _mockLogger.Object);
        _tempDir = Path.Combine(Path.GetTempPath(), $"opencode-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Test]
    public void CreateDefaultConfig_UsesDefaultModel_WhenNoModelProvided()
    {
        var config = _generator.CreateDefaultConfig();

        Assert.That(config.Model, Is.EqualTo("anthropic/claude-sonnet-4-5"));
    }

    [Test]
    public void CreateDefaultConfig_UsesProvidedModel_WhenModelSpecified()
    {
        var config = _generator.CreateDefaultConfig("openai/gpt-4");

        Assert.That(config.Model, Is.EqualTo("openai/gpt-4"));
    }

    [Test]
    public void CreateDefaultConfig_SetsAllPermissionsToAllow()
    {
        var config = _generator.CreateDefaultConfig();

        Assert.That(config.Permission, Is.Not.Null);
        Assert.That(config.Permission!["edit"], Is.EqualTo("allow"));
        Assert.That(config.Permission["bash"], Is.EqualTo("allow"));
        Assert.That(config.Permission["write"], Is.EqualTo("allow"));
        Assert.That(config.Permission["read"], Is.EqualTo("allow"));
    }

    [Test]
    public void CreateDefaultConfig_DisablesAutoupdate()
    {
        var config = _generator.CreateDefaultConfig();

        Assert.That(config.Autoupdate, Is.False);
    }

    [Test]
    public void CreateDefaultConfig_EnablesCompaction()
    {
        var config = _generator.CreateDefaultConfig();

        Assert.That(config.Compaction, Is.Not.Null);
        Assert.That(config.Compaction!.Auto, Is.True);
        Assert.That(config.Compaction.Prune, Is.True);
    }

    [Test]
    public void CreateDefaultConfig_SetsSchemaUrl()
    {
        var config = _generator.CreateDefaultConfig();

        Assert.That(config.Schema, Is.EqualTo("https://opencode.ai/config.json"));
    }

    [Test]
    public async Task GenerateConfigAsync_CreatesConfigFile()
    {
        var config = _generator.CreateDefaultConfig();

        await _generator.GenerateConfigAsync(_tempDir, config);

        var configPath = Path.Combine(_tempDir, "opencode.json");
        Assert.That(File.Exists(configPath), Is.True);
    }

    [Test]
    public async Task GenerateConfigAsync_WritesValidJson()
    {
        var config = _generator.CreateDefaultConfig("test/model");

        await _generator.GenerateConfigAsync(_tempDir, config);

        var configPath = Path.Combine(_tempDir, "opencode.json");
        var json = await File.ReadAllTextAsync(configPath);
        var parsed = JsonSerializer.Deserialize<OpenCodeConfig>(json);
        
        Assert.That(parsed, Is.Not.Null);
        Assert.That(parsed!.Model, Is.EqualTo("test/model"));
    }

    [Test]
    public async Task GenerateConfigAsync_WritesFormattedJson()
    {
        var config = _generator.CreateDefaultConfig();

        await _generator.GenerateConfigAsync(_tempDir, config);

        var configPath = Path.Combine(_tempDir, "opencode.json");
        var json = await File.ReadAllTextAsync(configPath);
        
        // Formatted JSON should contain newlines
        Assert.That(json, Does.Contain("\n"));
    }
}
