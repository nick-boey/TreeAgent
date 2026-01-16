using Homespun.Features.OpenCode.Data.Models;
using Homespun.Features.PullRequests.Data;
using Microsoft.Extensions.Logging;

namespace Homespun.Features.OpenCode.Services;

public class OpenCodeModelsService(
    IOpencodeCommandRunner opencodeCommandRunner,
    IDataStore dataStore,
    ILogger<OpenCodeModelsService> logger) : IOpenCodeModelsService
{
    private IReadOnlyList<ModelInfo>? _cachedModels;
    private readonly object _cacheLock = new();

    public async Task<IReadOnlyList<ModelInfo>> GetModelsAsync()
    {
        if (_cachedModels != null)
        {
            return _cachedModels;
        }

        return await RefreshModelsAsync();
    }

    public async Task<IReadOnlyList<ModelInfo>> RefreshModelsAsync()
    {
        logger.LogInformation("Refreshing models from OpenCode...");

        try
        {
            var models = await opencodeCommandRunner.GetModelsAsync();
            _cachedModels = models;
            logger.LogInformation("Loaded {Count} models from OpenCode", models.Count);
            return models;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to refresh models from OpenCode");
            _cachedModels = [];
            return _cachedModels;
        }
    }

    public IOrderedEnumerable<IGrouping<string, ModelInfo>> GetModelsGroupedByProvider(
        IReadOnlyList<string> favoriteModelIds,
        string? searchTerm = null,
        string? providerFilter = null)
    {
        var models = _cachedModels ?? [];

        var filteredModels = models.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var lowerSearchTerm = searchTerm.ToLower();
            filteredModels = filteredModels.Where(m =>
                m.Name.ToLower().Contains(lowerSearchTerm) ||
                m.FullId.ToLower().Contains(lowerSearchTerm));
        }

        if (!string.IsNullOrWhiteSpace(providerFilter))
        {
            filteredModels = filteredModels.Where(m =>
                m.ProviderId.Equals(providerFilter, StringComparison.OrdinalIgnoreCase));
        }

        return filteredModels
            .OrderByDescending(m => favoriteModelIds.Contains(m.FullId))
            .ThenBy(m => m.Name)
            .GroupBy(m => m.ProviderId)
            .OrderByDescending(g => g.Any(m => favoriteModelIds.Contains(m.FullId)))
            .ThenBy(g => g.Key);
    }

    public async Task<IReadOnlyList<ModelInfo>> GetModelsForSelectorAsync()
    {
        var favoriteModelIds = await GetFavoriteModelIdsAsync();
        var models = await GetModelsAsync();

        return models
            .OrderByDescending(m => favoriteModelIds.Contains(m.FullId))
            .ThenBy(m => m.ProviderId)
            .ThenBy(m => m.Name)
            .ToList();
    }

    public async Task<IReadOnlyList<string>> GetFavoriteModelIdsAsync()
    {
        return dataStore.FavoriteModels.ToList();
    }

    public async Task AddFavoriteAsync(string modelId)
    {
        await dataStore.AddFavoriteModelAsync(modelId);
    }

    public async Task RemoveFavoriteAsync(string modelId)
    {
        await dataStore.RemoveFavoriteModelAsync(modelId);
    }

    public async Task<bool> IsFavoriteAsync(string modelId)
    {
        return dataStore.IsFavoriteModel(modelId);
    }
}
