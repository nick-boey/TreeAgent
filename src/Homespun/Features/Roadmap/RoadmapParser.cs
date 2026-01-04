using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Homespun.Features.Roadmap;

/// <summary>
/// Parses and validates ROADMAP.json files.
/// </summary>
public static class RoadmapParser
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        WriteIndented = true
    };

    // Pattern for valid ID: lowercase alphanumeric and hyphens only
    private static readonly Regex IdPattern = new("^[a-z0-9-]+$", RegexOptions.Compiled);

    /// <summary>
    /// Parses a ROADMAP.json string into a Roadmap object.
    /// </summary>
    /// <param name="json">The JSON string to parse</param>
    /// <returns>The parsed Roadmap</returns>
    /// <exception cref="RoadmapValidationException">Thrown when validation fails</exception>
    public static Roadmap Parse(string json)
    {
        Roadmap? roadmap;

        try
        {
            roadmap = JsonSerializer.Deserialize<Roadmap>(json, SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new RoadmapValidationException($"Invalid JSON: {ex.Message}", ex);
        }

        if (roadmap == null)
        {
            throw new RoadmapValidationException("Failed to parse roadmap: null result");
        }

        Validate(roadmap);
        return roadmap;
    }

    /// <summary>
    /// Serializes a Roadmap object to JSON string.
    /// </summary>
    public static string Serialize(Roadmap roadmap)
    {
        return JsonSerializer.Serialize(roadmap, SerializerOptions);
    }

    /// <summary>
    /// Loads and parses a ROADMAP.json file.
    /// </summary>
    /// <param name="filePath">Path to the ROADMAP.json file</param>
    /// <returns>The parsed Roadmap</returns>
    public static async Task<Roadmap> LoadAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new RoadmapValidationException($"ROADMAP.json file not found: {filePath}");
        }

        var json = await File.ReadAllTextAsync(filePath);
        return Parse(json);
    }

    /// <summary>
    /// Saves a Roadmap to a file.
    /// </summary>
    public static async Task SaveAsync(Roadmap roadmap, string filePath)
    {
        roadmap.LastUpdated = DateTime.UtcNow;
        var json = Serialize(roadmap);
        await File.WriteAllTextAsync(filePath, json);
    }

    private static void Validate(Roadmap roadmap)
    {
        if (string.IsNullOrWhiteSpace(roadmap.Version))
        {
            throw new RoadmapValidationException("Missing required field: version");
        }

        ValidateChanges(roadmap.Changes, "root");
    }

    private static void ValidateChanges(List<RoadmapChange> changes, string path)
    {
        var seenIds = new HashSet<string>();

        for (int i = 0; i < changes.Count; i++)
        {
            var change = changes[i];
            var changePath = $"{path}/changes[{i}]";

            // Validate required fields
            if (string.IsNullOrWhiteSpace(change.Id))
            {
                throw new RoadmapValidationException($"Missing required field: id at {changePath}");
            }

            if (!IdPattern.IsMatch(change.Id))
            {
                throw new RoadmapValidationException(
                    $"Invalid id pattern at {changePath}: '{change.Id}'. " +
                    "ID must contain only lowercase letters, numbers, and hyphens.");
            }

            if (seenIds.Contains(change.Id))
            {
                throw new RoadmapValidationException($"Duplicate id at {changePath}: '{change.Id}'");
            }
            seenIds.Add(change.Id);

            if (string.IsNullOrWhiteSpace(change.Group))
            {
                throw new RoadmapValidationException($"Missing required field: group at {changePath}");
            }

            if (string.IsNullOrWhiteSpace(change.Title))
            {
                throw new RoadmapValidationException($"Missing required field: title at {changePath}");
            }

            // Type is validated during deserialization, but check for default value
            // which might indicate a parsing issue
            if (!Enum.IsDefined(typeof(ChangeType), change.Type))
            {
                throw new RoadmapValidationException($"Invalid type at {changePath}");
            }

            // Recursively validate children
            if (change.Children.Count > 0)
            {
                ValidateChanges(change.Children, $"{changePath}/{change.Id}");
            }
        }
    }
}
