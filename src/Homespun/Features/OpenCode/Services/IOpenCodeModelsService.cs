using Homespun.Features.OpenCode.Data.Models;
using Homespun.Features.PullRequests.Data;

namespace Homespun.Features.OpenCode.Services;

public interface IOpenCodeModelsService
{
    /// <summary>
    /// Gets all available models from OpenCode (cached).
    /// </summary>
    Task<IReadOnlyList<ModelInfo>> GetModelsAsync();

    /// <summary>
    /// Refreshes the cached model list from OpenCode.
    /// </summary>
    Task<IReadOnlyList<ModelInfo>> RefreshModelsAsync();

    /// <summary>
    /// Gets all models grouped by provider with favorites first.
    /// </summary>
    IOrderedEnumerable<IGrouping<string, ModelInfo>> GetModelsGroupedByProvider(
        IReadOnlyList<string> favoriteModelIds,
        string? searchTerm = null,
        string? providerFilter = null);

    /// <summary>
    /// Gets favorite models first, then all other models.
    /// </summary>
    Task<IReadOnlyList<ModelInfo>> GetModelsForSelectorAsync();

    /// <summary>
    /// Gets all favorite model IDs.
    /// </summary>
    Task<IReadOnlyList<string>> GetFavoriteModelIdsAsync();

    /// <summary>
    /// Adds a model to favorites.
    /// </summary>
    Task AddFavoriteAsync(string modelId);

    /// <summary>
    /// Removes a model from favorites.
    /// </summary>
    Task RemoveFavoriteAsync(string modelId);

    /// <summary>
    /// Checks if a model is favorited.
    /// </summary>
    Task<bool> IsFavoriteAsync(string modelId);
}
