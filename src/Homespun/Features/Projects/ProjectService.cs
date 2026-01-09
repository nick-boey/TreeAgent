using Homespun.Features.Beads.Services;
using Homespun.Features.Commands;
using Homespun.Features.GitHub;
using Homespun.Features.PullRequests.Data;
using Homespun.Features.PullRequests.Data.Entities;

namespace Homespun.Features.Projects;

/// <summary>
/// Result of a project creation attempt.
/// </summary>
public class CreateProjectResult
{
    public bool Success { get; init; }
    public Project? Project { get; init; }
    public string? ErrorMessage { get; init; }

    public static CreateProjectResult Ok(Project project) => new() { Success = true, Project = project };
    public static CreateProjectResult Error(string message) => new() { Success = false, ErrorMessage = message };
}

public class ProjectService(
    IDataStore dataStore,
    IGitHubService gitHubService,
    ICommandRunner commandRunner,
    IBeadsInitializer beadsInitializer,
    IConfiguration configuration,
    ILogger<ProjectService> logger)
{
    /// <summary>
    /// Base path for all project worktrees.
    /// Uses HOMESPUN_BASE_PATH environment variable if set, otherwise defaults to ~/.homespun/src
    /// </summary>
    private string HomespunBasePath
    {
        get
        {
            // Check for explicit configuration first (for container environments)
            var configuredPath = configuration["HOMESPUN_BASE_PATH"];
            if (!string.IsNullOrEmpty(configuredPath))
            {
                return configuredPath;
            }
            
            // Check for HOMESPUN_DATA_PATH and derive base path from it
            var dataPath = configuration["HOMESPUN_DATA_PATH"];
            if (!string.IsNullOrEmpty(dataPath))
            {
                // If data path is /data/.homespun/homespun-data.json, base path should be /data/.homespun/src
                var dataDir = Path.GetDirectoryName(dataPath);
                if (!string.IsNullOrEmpty(dataDir))
                {
                    return Path.Combine(dataDir, "src");
                }
            }
            
            // Default: ~/.homespun/src
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".homespun", "src");
        }
    }

    public Task<List<Project>> GetAllAsync()
    {
        logger.LogDebug("GetAllAsync called, HomespunBasePath={HomespunBasePath}", HomespunBasePath);
        var projects = dataStore.Projects
            .OrderByDescending(p => p.UpdatedAt)
            .ToList();
        return Task.FromResult(projects);
    }

    public async Task<Project?> GetByIdAsync(string id)
    {
        var project = dataStore.GetProject(id);
        
        if (project != null)
        {
            // Initialize beads if not already initialized
            await InitializeBeadsIfNeededAsync(project);
        }
        
        return project;
    }

    /// <summary>
    /// Creates a new project from a GitHub repository.
    /// </summary>
    /// <param name="ownerRepo">GitHub owner and repository in "owner/repo" format</param>
    /// <returns>Result containing the created project or an error message</returns>
    /// <summary>
    /// Creates a new local project with a fresh git repository.
    /// </summary>
    /// <param name="name">Project name (used for folder name)</param>
    /// <param name="defaultBranch">Default branch name (defaults to "main")</param>
    public async Task<CreateProjectResult> CreateLocalAsync(string name, string defaultBranch = "main")
    {
        // Validate name
        if (string.IsNullOrWhiteSpace(name))
        {
            return CreateProjectResult.Error("Project name is required.");
        }
        
        if (!IsValidProjectName(name))
        {
            return CreateProjectResult.Error("Invalid project name. Use only letters, numbers, hyphens, and underscores.");
        }
        
        if (string.IsNullOrWhiteSpace(defaultBranch))
        {
            defaultBranch = "main";
        }

        // Calculate path: ~/.homespun/src/<name>/<defaultBranch>
        var repoPath = Path.Combine(HomespunBasePath, name);
        var localPath = Path.Combine(repoPath, defaultBranch);

        // Check if already exists
        if (Directory.Exists(localPath))
        {
            return CreateProjectResult.Error($"Project already exists at {localPath}");
        }

        // Create directory
        Directory.CreateDirectory(localPath);

        try
        {
            // Initialize git repo
            var initResult = await commandRunner.RunAsync("git", "init", localPath);
            if (!initResult.Success)
            {
                return CreateProjectResult.Error($"Failed to initialize git: {initResult.Error}");
            }

            // Set default branch name
            await commandRunner.RunAsync("git", $"branch -M {defaultBranch}", localPath);

            // Create initial commit (required for beads and worktrees)
            var commitResult = await commandRunner.RunAsync("git", "commit --allow-empty -m \"Initial commit\"", localPath);
            if (!commitResult.Success)
            {
                return CreateProjectResult.Error($"Failed to create initial commit: {commitResult.Error}");
            }

            // Create project entity (no GitHub owner/repo)
            var project = new Project
            {
                Name = name,
                LocalPath = localPath,
                GitHubOwner = null,
                GitHubRepo = null,
                DefaultBranch = defaultBranch
            };

            await dataStore.AddProjectAsync(project);
            await InitializeBeadsIfNeededAsync(project);

            logger.LogInformation("Created local project {Name} at {LocalPath}", name, localPath);
            return CreateProjectResult.Ok(project);
        }
        catch (Exception ex)
        {
            // Clean up on failure
            try
            {
                if (Directory.Exists(localPath))
                {
                    Directory.Delete(localPath, true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
            
            logger.LogError(ex, "Failed to create local project {Name}", name);
            return CreateProjectResult.Error($"Failed to create project: {ex.Message}");
        }
    }

    private static bool IsValidProjectName(string name)
    {
        // Allow alphanumeric, hyphens, underscores
        return System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-zA-Z0-9_-]+$");
    }

    public async Task<CreateProjectResult> CreateAsync(string ownerRepo)
    {
        logger.LogInformation("CreateAsync called with ownerRepo: {OwnerRepo}", ownerRepo);
        
        // Parse owner/repo
        var parts = ownerRepo.Split('/', 2);
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
        {
            logger.LogWarning("Invalid ownerRepo format: {OwnerRepo}", ownerRepo);
            return CreateProjectResult.Error("Invalid format. Expected 'owner/repository'.");
        }

        var owner = parts[0].Trim();
        var repo = parts[1].Trim();
        logger.LogInformation("Parsed owner={Owner}, repo={Repo}", owner, repo);

        // Get default branch from GitHub
        logger.LogInformation("Fetching default branch from GitHub for {Owner}/{Repo}", owner, repo);
        try
        {
            var defaultBranch = await gitHubService.GetDefaultBranchAsync(owner, repo);
            if (defaultBranch == null)
            {
                logger.LogWarning("GitHub API returned null for default branch of {Owner}/{Repo}", owner, repo);
                return CreateProjectResult.Error($"Could not fetch repository '{owner}/{repo}' from GitHub. Check that the repository exists and GITHUB_TOKEN is configured.");
            }
            logger.LogInformation("Default branch for {Owner}/{Repo} is {DefaultBranch}", owner, repo, defaultBranch);

            // Calculate local path: ~/.homespun/src/<repo>/<branch>
            var repoPath = Path.Combine(HomespunBasePath, repo);
            var localPath = Path.Combine(repoPath, defaultBranch);
            logger.LogInformation("Calculated paths: HomespunBasePath={BasePath}, repoPath={RepoPath}, localPath={LocalPath}", 
                HomespunBasePath, repoPath, localPath);

            // Create directory structure if it doesn't exist
            logger.LogInformation("Creating directory structure at {RepoPath}", repoPath);
            Directory.CreateDirectory(repoPath);
            logger.LogInformation("Directory created/verified: {RepoPath}, Exists={Exists}", repoPath, Directory.Exists(repoPath));

            // Clone the repository to the local path
            var cloneUrl = $"https://github.com/{owner}/{repo}.git";
            logger.LogInformation("Cloning repository from {CloneUrl} to {LocalPath}", cloneUrl, localPath);
            var cloneResult = await commandRunner.RunAsync("git", $"clone \"{cloneUrl}\" \"{localPath}\"", repoPath);
            logger.LogInformation("Clone result: Success={Success}, ExitCode={ExitCode}, Output={Output}, Error={Error}", 
                cloneResult.Success, cloneResult.ExitCode, cloneResult.Output, cloneResult.Error);

            if (!cloneResult.Success)
            {
                // Check if it already exists
                var gitDirExists = Directory.Exists(Path.Combine(localPath, ".git"));
                logger.LogInformation("Clone failed. Checking if already cloned: localPath exists={LocalPathExists}, .git exists={GitDirExists}", 
                    Directory.Exists(localPath), gitDirExists);
                
                if (Directory.Exists(localPath) && gitDirExists)
                {
                    logger.LogInformation("Repository already cloned at {LocalPath}, continuing", localPath);
                }
                else
                {
                    logger.LogError("Failed to clone repository {Owner}/{Repo}: {Error}", owner, repo, cloneResult.Error);
                    return CreateProjectResult.Error($"Failed to clone repository: {cloneResult.Error}");
                }
            }

            var project = new Project
            {
                Name = repo,
                LocalPath = localPath,
                GitHubOwner = owner,
                GitHubRepo = repo,
                DefaultBranch = defaultBranch
            };

            logger.LogInformation("Adding project to data store: {ProjectName} at {LocalPath}", project.Name, project.LocalPath);
            await dataStore.AddProjectAsync(project);

            // Initialize beads for new project
            logger.LogInformation("Initializing beads for project {ProjectName}", project.Name);
            await InitializeBeadsIfNeededAsync(project);

            logger.LogInformation("Project {ProjectName} created successfully", project.Name);
            return CreateProjectResult.Ok(project);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception during CreateAsync for {OwnerRepo}", ownerRepo);
            return CreateProjectResult.Error($"Failed to create project: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Initializes beads for a project if not already initialized.
    /// Sets up the beads-sync branch for synchronization.
    /// </summary>
    private async Task InitializeBeadsIfNeededAsync(Project project)
    {
        try
        {
            if (!await beadsInitializer.IsInitializedAsync(project.LocalPath))
            {
                logger.LogInformation("Initializing beads for project {ProjectName} at {LocalPath}", 
                    project.Name, project.LocalPath);
                
                var success = await beadsInitializer.InitializeAsync(project.LocalPath, syncBranch: "beads-sync");
                
                if (success)
                {
                    logger.LogInformation("Successfully initialized beads for project {ProjectName}", project.Name);
                }
                else
                {
                    logger.LogWarning("Failed to initialize beads for project {ProjectName}", project.Name);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error initializing beads for project {ProjectName}", project.Name);
        }
    }

    public async Task<Project?> UpdateAsync(
        string id,
        string? defaultModel = null)
    {
        var project = dataStore.GetProject(id);
        if (project == null) return null;

        project.DefaultModel = defaultModel;
        project.UpdatedAt = DateTime.UtcNow;

        await dataStore.UpdateProjectAsync(project);
        return project;
    }

    public async Task<bool> DeleteAsync(string id)
    {
        var project = dataStore.GetProject(id);
        if (project == null) return false;

        await dataStore.RemoveProjectAsync(id);
        return true;
    }
}
