using System.Text.Json;
using Homespun.Features.OpenCode.Models;
using Microsoft.Extensions.Options;

namespace Homespun.Features.OpenCode.Services;

/// <summary>
/// Generates opencode.json configuration files for worktrees.
/// </summary>
public class OpenCodeConfigGenerator : IOpenCodeConfigGenerator
{
    private readonly OpenCodeOptions _options;
    private readonly ILogger<OpenCodeConfigGenerator> _logger;
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public OpenCodeConfigGenerator(IOptions<OpenCodeOptions> options, ILogger<OpenCodeConfigGenerator> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task GenerateConfigAsync(string worktreePath, OpenCodeConfig config, CancellationToken ct = default)
    {
        var configPath = Path.Combine(worktreePath, "opencode.json");
        
        var json = JsonSerializer.Serialize(config, JsonOptions);
        await File.WriteAllTextAsync(configPath, json, ct);
        
        _logger.LogInformation("Generated OpenCode config at {ConfigPath}", configPath);
    }

    public OpenCodeConfig CreateDefaultConfig(string? model = null)
    {
        var config = new OpenCodeConfig
        {
            Schema = "https://opencode.ai/config.json",
            Model = model ?? _options.DefaultModel,
            Permission = new Dictionary<string, string>
            {
                ["edit"] = "allow",
                ["bash"] = "allow",
                ["write"] = "allow",
                ["read"] = "allow",
                ["webfetch"] = "allow"
            },
            Autoupdate = false,
            Compaction = new OpenCodeCompactionConfig
            {
                Auto = true,
                Prune = true
            }
        };

        return config;
    }
}
