using Homespun.Features.Commands;
using Microsoft.Extensions.Logging;

namespace Homespun.Features.Beads.Services;

/// <summary>
/// Service for initializing beads in a repository.
/// </summary>
public class BeadsInitializer : IBeadsInitializer
{
    private readonly ICommandRunner _commandRunner;
    private readonly IBeadsService _beadsService;
    private readonly ILogger<BeadsInitializer> _logger;
    
    public BeadsInitializer(
        ICommandRunner commandRunner, 
        IBeadsService beadsService,
        ILogger<BeadsInitializer> logger)
    {
        _commandRunner = commandRunner;
        _beadsService = beadsService;
        _logger = logger;
    }
    
    public async Task<bool> InitializeAsync(string projectPath, string? syncBranch = "beads-sync")
    {
        if (await IsInitializedAsync(projectPath))
        {
            _logger.LogDebug("Beads already initialized in {ProjectPath}", projectPath);
            
            // Configure sync branch if specified
            if (!string.IsNullOrEmpty(syncBranch))
            {
                return await ConfigureSyncBranchAsync(projectPath, syncBranch);
            }
            
            return true;
        }
        
        _logger.LogInformation("Initializing beads in {ProjectPath} with sync branch {SyncBranch}", 
            projectPath, syncBranch);
        
        // Build the init command
        var args = "init";
        if (!string.IsNullOrEmpty(syncBranch))
        {
            args += $" --branch {syncBranch}";
        }
        
        var result = await _commandRunner.RunAsync("bd", args, projectPath);
        
        if (!result.Success)
        {
            _logger.LogError("Failed to initialize beads in {ProjectPath}: {Error}", 
                projectPath, result.Error);
            return false;
        }
        
        _logger.LogInformation("Successfully initialized beads in {ProjectPath}", projectPath);
        return true;
    }
    
    public async Task<bool> IsInitializedAsync(string projectPath)
    {
        return await _beadsService.IsInitializedAsync(projectPath);
    }
    
    public async Task<bool> ConfigureSyncBranchAsync(string projectPath, string syncBranch)
    {
        _logger.LogDebug("Configuring beads sync branch to {SyncBranch} in {ProjectPath}", 
            syncBranch, projectPath);
        
        var result = await _commandRunner.RunAsync("bd", $"config set sync.branch {syncBranch}", projectPath);
        
        if (!result.Success)
        {
            _logger.LogWarning("Failed to configure beads sync branch: {Error}", result.Error);
            return false;
        }
        
        return true;
    }
}
