namespace Homespun.Features.Beads.Services;

/// <summary>
/// Configuration options for the beads database service.
/// </summary>
public class BeadsDatabaseOptions
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "BeadsDatabase";

    /// <summary>
    /// Debounce interval in milliseconds before writing to database.
    /// Default: 2000 (2 seconds)
    /// </summary>
    public int DebounceIntervalMs { get; set; } = 2000;

    /// <summary>
    /// SQLite busy timeout in milliseconds.
    /// Default: 5000 (5 seconds)
    /// </summary>
    public int BusyTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// Maximum retry attempts for database operations.
    /// Default: 3
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Maximum history items to keep per project.
    /// Default: 100
    /// </summary>
    public int MaxHistoryItems { get; set; } = 100;

    /// <summary>
    /// Whether to run bd sync before applying changes.
    /// Default: true
    /// </summary>
    public bool SyncBeforeApply { get; set; } = true;

    /// <summary>
    /// Whether to run bd sync after applying changes.
    /// Default: true
    /// </summary>
    public bool SyncAfterApply { get; set; } = true;
}
