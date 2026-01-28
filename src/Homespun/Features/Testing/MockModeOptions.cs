namespace Homespun.Features.Testing;

/// <summary>
/// Configuration options for mock mode.
/// </summary>
public class MockModeOptions
{
    /// <summary>
    /// The configuration section name.
    /// </summary>
    public const string SectionName = "MockMode";

    /// <summary>
    /// Gets or sets whether mock mode is enabled.
    /// When true, mock services are used instead of real implementations.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Gets or sets whether to seed demo data on startup.
    /// </summary>
    public bool SeedData { get; set; } = true;
}
