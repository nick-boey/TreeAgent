using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Homespun.Features.GitHub;

/// <summary>
/// Provides environment variables for GitHub authentication in spawned processes.
/// Creates a GIT_ASKPASS script that echoes the GITHUB_TOKEN for git credential prompts.
/// </summary>
public partial class GitHubEnvironmentService : IGitHubEnvironmentService, IDisposable
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<GitHubEnvironmentService> _logger;
    private readonly string _homespunDir;
    private readonly string _askPassScriptPath;
    private bool _disposed;

    public GitHubEnvironmentService(
        IConfiguration configuration,
        ILogger<GitHubEnvironmentService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _homespunDir = ResolveHomespunDirectory();
        _askPassScriptPath = CreateGitAskPassScript();
    }

    /// <summary>
    /// Resolves the Homespun data directory, using the same logic as Program.cs.
    /// In containers, this uses HOMESPUN_DATA_PATH which points to a writable volume.
    /// </summary>
    private string ResolveHomespunDirectory()
    {
        // Check for configured data path (used in containers)
        var configuredDataPath = _configuration["HOMESPUN_DATA_PATH"];
        if (!string.IsNullOrEmpty(configuredDataPath))
        {
            var dataDir = Path.GetDirectoryName(configuredDataPath);
            if (!string.IsNullOrEmpty(dataDir))
            {
                _logger.LogDebug("Using data directory from HOMESPUN_DATA_PATH: {DataDir}", dataDir);
                return dataDir;
            }
        }

        // Default to ~/.homespun
        var defaultDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".homespun");
        _logger.LogDebug("Using default homespun directory: {Dir}", defaultDir);
        return defaultDir;
    }

    /// <summary>
    /// Gets the GitHub token using the same priority as GitHubService:
    /// 1. User secrets (GitHub:Token)
    /// 2. Configuration (GITHUB_TOKEN)
    /// 3. Environment variable (GITHUB_TOKEN)
    /// </summary>
    private string? GetGitHubToken()
    {
        return _configuration["GitHub:Token"]
            ?? _configuration["GITHUB_TOKEN"]
            ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN");
    }

    public bool IsConfigured => !string.IsNullOrEmpty(GetGitHubToken());

    public IDictionary<string, string> GetGitHubEnvironment()
    {
        var env = new Dictionary<string, string>();

        // GitHub token for authentication
        var token = GetGitHubToken();
        if (!string.IsNullOrEmpty(token))
        {
            env["GITHUB_TOKEN"] = token;
            env["GH_TOKEN"] = token; // gh CLI also uses GH_TOKEN
            env["GIT_ASKPASS"] = _askPassScriptPath;
            env["GIT_TERMINAL_PROMPT"] = "0"; // Disable interactive prompts

            _logger.LogDebug(
                "GitHub environment configured with token and GIT_ASKPASS at {Path}",
                _askPassScriptPath);
        }
        else
        {
            _logger.LogWarning("No GitHub token configured - environment will not include authentication");
        }

        // Git identity for commits (required for bd sync and other git operations)
        var gitName = GetGitIdentityName();
        var gitEmail = GetGitIdentityEmail();

        env["GIT_AUTHOR_NAME"] = gitName;
        env["GIT_AUTHOR_EMAIL"] = gitEmail;
        env["GIT_COMMITTER_NAME"] = gitName;
        env["GIT_COMMITTER_EMAIL"] = gitEmail;

        _logger.LogDebug("Git identity configured: {Name} <{Email}>", gitName, gitEmail);

        return env;
    }

    /// <summary>
    /// Gets the git author/committer name from configuration or defaults to "Homespun Bot".
    /// </summary>
    private string GetGitIdentityName()
    {
        return _configuration["Git:AuthorName"]
            ?? _configuration["GIT_AUTHOR_NAME"]
            ?? Environment.GetEnvironmentVariable("GIT_AUTHOR_NAME")
            ?? "Homespun Bot";
    }

    /// <summary>
    /// Gets the git author/committer email from configuration or defaults to "homespun@localhost".
    /// </summary>
    private string GetGitIdentityEmail()
    {
        return _configuration["Git:AuthorEmail"]
            ?? _configuration["GIT_AUTHOR_EMAIL"]
            ?? Environment.GetEnvironmentVariable("GIT_AUTHOR_EMAIL")
            ?? "homespun@localhost";
    }

    public string? GetMaskedToken()
    {
        var token = GetGitHubToken();
        if (string.IsNullOrEmpty(token))
            return null;

        if (token.Length <= 8)
            return "***";

        // Show first 4 and last 4 characters
        return $"{token[..4]}***{token[^4..]}";
    }

    public async Task<GitHubAuthStatus> CheckGhAuthStatusAsync(CancellationToken ct = default)
    {
        var hasToken = IsConfigured;

        try
        {
            // Run gh auth status to check CLI authentication
            var startInfo = new ProcessStartInfo
            {
                FileName = "gh",
                Arguments = "auth status",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            // Inject GITHUB_TOKEN so gh CLI can use it
            var token = GetGitHubToken();
            if (!string.IsNullOrEmpty(token))
            {
                startInfo.Environment["GITHUB_TOKEN"] = token;
                startInfo.Environment["GH_TOKEN"] = token;
            }

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var output = await process.StandardOutput.ReadToEndAsync(ct);
            var error = await process.StandardError.ReadToEndAsync(ct);

            await process.WaitForExitAsync(ct);

            var combinedOutput = $"{output}\n{error}";

            // Parse the output to extract username
            // gh auth status output contains "Logged in to github.com account <username>"
            var usernameMatch = UsernameRegex().Match(combinedOutput);
            var username = usernameMatch.Success ? usernameMatch.Groups[1].Value : null;

            if (process.ExitCode == 0)
            {
                return new GitHubAuthStatus
                {
                    IsAuthenticated = true,
                    Username = username,
                    Message = "Authenticated with GitHub",
                    AuthMethod = hasToken ? GitHubAuthMethod.Both : GitHubAuthMethod.GhCli
                };
            }
            else
            {
                // gh auth status failed - check if we at least have a token
                if (hasToken)
                {
                    return new GitHubAuthStatus
                    {
                        IsAuthenticated = true,
                        Message = "Token configured (gh CLI not authenticated)",
                        AuthMethod = GitHubAuthMethod.Token
                    };
                }

                return new GitHubAuthStatus
                {
                    IsAuthenticated = false,
                    ErrorMessage = error.Trim(),
                    AuthMethod = GitHubAuthMethod.None
                };
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to check gh auth status");

            // gh CLI might not be installed - check if we have a token
            if (hasToken)
            {
                return new GitHubAuthStatus
                {
                    IsAuthenticated = true,
                    Message = "Token configured (gh CLI not available)",
                    AuthMethod = GitHubAuthMethod.Token
                };
            }

            return new GitHubAuthStatus
            {
                IsAuthenticated = false,
                ErrorMessage = $"gh CLI not available: {ex.Message}",
                AuthMethod = GitHubAuthMethod.None
            };
        }
    }

    /// <summary>
    /// Creates a platform-appropriate GIT_ASKPASS script that echoes the GITHUB_TOKEN.
    /// </summary>
    private string CreateGitAskPassScript()
    {
        // Ensure directory exists
        if (!Directory.Exists(_homespunDir))
        {
            Directory.CreateDirectory(_homespunDir);
        }

        string scriptPath;
        string scriptContent;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            scriptPath = Path.Combine(_homespunDir, "git-askpass.cmd");
            // Windows batch script - echo the GITHUB_TOKEN environment variable
            scriptContent = "@echo off\r\necho %GITHUB_TOKEN%\r\n";
        }
        else
        {
            scriptPath = Path.Combine(_homespunDir, "git-askpass.sh");
            // Unix shell script - echo the GITHUB_TOKEN environment variable
            scriptContent = "#!/bin/sh\necho \"$GITHUB_TOKEN\"\n";
        }

        // Write the script (overwrite if exists to ensure correct content)
        File.WriteAllText(scriptPath, scriptContent);

        // On Unix, make the script executable
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                var chmod = Process.Start(new ProcessStartInfo
                {
                    FileName = "chmod",
                    Arguments = $"+x \"{scriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                chmod?.WaitForExit();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to set executable permission on git-askpass script");
            }
        }

        _logger.LogInformation("Created GIT_ASKPASS script at {Path}", scriptPath);
        return scriptPath;
    }

    [GeneratedRegex(@"Logged in to [^\s]+ account ([^\s\(]+)", RegexOptions.IgnoreCase)]
    private static partial Regex UsernameRegex();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Optionally clean up the script file
        // For now, leave it in place as it doesn't contain sensitive data
        // (the token is read from the environment at runtime)
    }
}
