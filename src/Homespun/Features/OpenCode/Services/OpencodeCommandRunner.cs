using System.Text.Json;
using Homespun.Features.Commands;
using Homespun.Features.OpenCode.Data.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Homespun.Features.OpenCode.Services;

public class OpencodeCommandRunner(
    ICommandRunner commandRunner,
    IOptions<OpenCodeOptions> options,
    ILogger<OpencodeCommandRunner> logger) : IOpencodeCommandRunner
{
    public async Task<IReadOnlyList<ModelInfo>> GetModelsAsync()
    {
        var result = await commandRunner.RunAsync(
            options.Value.ExecutablePath,
            "models --verbose",
            Environment.CurrentDirectory);

        if (!result.Success)
        {
            logger.LogError("Failed to get models from OpenCode: {Error}", result.Error);
            throw new InvalidOperationException(
                $"Failed to get models from OpenCode: {result.Error}");
        }

        return ParseModelsOutput(result.Output);
    }

    private static IReadOnlyList<ModelInfo> ParseModelsOutput(string output)
    {
        var models = new List<ModelInfo>();

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            if (string.IsNullOrEmpty(line))
                continue;

            if (line.StartsWith("opencode/") || line.Contains('/'))
            {
                var modelIdLine = line;

                var jsonStartIndex = i + 1;
                if (jsonStartIndex >= lines.Length)
                    continue;

                var jsonLines = new List<string>();
                int braceCount = 0;
                bool inJson = false;

                for (int j = jsonStartIndex; j < lines.Length; j++)
                {
                    var jsonLine = lines[j];

                    if (!inJson && jsonLine.Trim().StartsWith('{'))
                    {
                        inJson = true;
                    }

                    if (inJson)
                    {
                        jsonLines.Add(jsonLine);
                        braceCount += CountBraces(jsonLine, '{');
                        braceCount -= CountBraces(jsonLine, '}');

                        if (braceCount == 0 && jsonLine.Trim().EndsWith('}'))
                        {
                            i = j;
                            break;
                        }
                    }
                }

                if (jsonLines.Count > 0)
                {
                    try
                    {
                        var json = string.Join('\n', jsonLines);
                        var model = JsonSerializer.Deserialize<ModelInfo>(json, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                        if (model != null)
                        {
                            models.Add(model);
                        }
                    }
                    catch (JsonException ex)
                    {
                        Console.WriteLine($"Failed to parse model JSON: {ex.Message}");
                    }
                }
            }
        }

        return models;
    }

    private static int CountBraces(string line, char brace)
    {
        return line.Count(c => c == brace);
    }
}
