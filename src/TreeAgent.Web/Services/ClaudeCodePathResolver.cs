namespace TreeAgent.Web.Services;

/// <summary>
/// Resolves the path to the Claude Code executable.
/// Checks environment variable first, then default installation locations.
/// </summary>
public class ClaudeCodePathResolver
{
    private readonly string? _environmentVariable;
    private readonly Func<string, bool> _fileExistsCheck;
    private readonly Func<string?> _getWindowsLocalAppData;
    private readonly Func<string?> _getWindowsAppData;
    private readonly Func<string?> _getHomeDirectory;
    private readonly Func<bool> _isWindows;

    /// <summary>
    /// Creates a new ClaudeCodePathResolver with default system dependencies.
    /// </summary>
    public ClaudeCodePathResolver() : this(
        Environment.GetEnvironmentVariable("CLAUDE_CODE_PATH"),
        File.Exists,
        () => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        () => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        () => OperatingSystem.IsWindows())
    {
    }

    /// <summary>
    /// Creates a new ClaudeCodePathResolver with injectable dependencies for testing.
    /// </summary>
    public ClaudeCodePathResolver(
        string? environmentVariable,
        Func<string, bool> fileExistsCheck,
        Func<string?>? getWindowsLocalAppData = null,
        Func<string?>? getWindowsAppData = null,
        Func<string?>? getHomeDirectory = null,
        Func<bool>? isWindows = null)
    {
        _environmentVariable = environmentVariable;
        _fileExistsCheck = fileExistsCheck;
        _getWindowsLocalAppData = getWindowsLocalAppData ?? (() => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        _getWindowsAppData = getWindowsAppData ?? (() => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        _getHomeDirectory = getHomeDirectory ?? (() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        _isWindows = isWindows ?? OperatingSystem.IsWindows;
    }

    /// <summary>
    /// Resolves the path to the Claude Code executable.
    /// </summary>
    /// <returns>
    /// The path to the Claude Code executable. Returns:
    /// 1. CLAUDE_CODE_PATH environment variable if set
    /// 2. First existing path from default installation locations
    /// 3. "claude" if no path is found (relies on PATH)
    /// </returns>
    public string Resolve()
    {
        // First priority: environment variable
        if (!string.IsNullOrEmpty(_environmentVariable))
        {
            return _environmentVariable;
        }

        // Second priority: check default installation locations
        foreach (var path in GetDefaultPaths())
        {
            if (_fileExistsCheck(path))
            {
                return path;
            }
        }

        // Fallback: rely on PATH
        return "claude";
    }

    /// <summary>
    /// Gets the list of default installation paths to check.
    /// </summary>
    public IEnumerable<string> GetDefaultPaths()
    {
        if (_isWindows())
        {
            return GetWindowsDefaultPaths();
        }
        else
        {
            return GetLinuxDefaultPaths();
        }
    }

    private IEnumerable<string> GetWindowsDefaultPaths()
    {
        var paths = new List<string>();

        // Native installer location (checked first as it's the preferred installation)
        var localAppData = _getWindowsLocalAppData();
        if (!string.IsNullOrEmpty(localAppData))
        {
            paths.Add(Path.Combine(localAppData, "Programs", "claude-code", "claude.exe"));
        }

        // NPM global installation
        var appData = _getWindowsAppData();
        if (!string.IsNullOrEmpty(appData))
        {
            paths.Add(Path.Combine(appData, "npm", "claude.cmd"));
        }

        return paths;
    }

    private IEnumerable<string> GetLinuxDefaultPaths()
    {
        var paths = new List<string>();

        // Native installer location (checked first as it's the preferred installation)
        paths.Add("/usr/local/bin/claude");

        var home = _getHomeDirectory();
        if (!string.IsNullOrEmpty(home))
        {
            // Use forward slashes for Linux paths (Path.Combine uses OS-specific separator)
            var normalizedHome = home.Replace('\\', '/');

            // NPM global with custom prefix (~/.npm-global)
            paths.Add($"{normalizedHome}/.npm-global/bin/claude");

            // User local bin (~/.local/bin)
            paths.Add($"{normalizedHome}/.local/bin/claude");
        }

        return paths;
    }
}
