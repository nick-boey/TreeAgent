using Homespun.Features.OpenCode.Data.Models;
using Homespun.Features.OpenCode.Services;
using Homespun.Features.PullRequests.Data;
using Moq;
using Xunit;

namespace Homespun.Tests.Features.OpenCode.Services;

public class OpenCodeModelsServiceTests
{
    private readonly Mock<IDataStore> _mockDataStore = new();
    private readonly Mock<IOpencodeCommandRunner> _mockCommandRunner = new();
    private readonly Mock<ILogger<OpenCodeModelsService>> _mockLogger = new();

    [Fact]
    public async Task GetModelsAsync_ShouldCacheResults()
    {
        var service = new OpenCodeModelsService(
            _mockCommandRunner.Object,
            _mockDataStore.Object,
            _mockLogger.Object);

        var expectedModels = new List<ModelInfo>
        {
            new ModelInfo { Id = "test-1", ProviderId = "anthropic", Name = "Test Model 1" },
            new ModelInfo { Id = "test-2", ProviderId = "github-copilot", Name = "Test Model 2" }
        };

        _mockCommandRunner
            .Setup(r => r.GetModelsAsync())
            .ReturnsAsync(expectedModels);

        var result1 = await service.GetModelsAsync();
        var result2 = await service.GetModelsAsync();

        Assert.Equal(2, result1.Count);
        Assert.Same(result1, result2);
        _mockCommandRunner.Verify(r => r.GetModelsAsync(), Times.Once);
    }

    [Fact]
    public async Task RefreshModelsAsync_ShouldCallCommandRunner()
    {
        var service = new OpenCodeModelsService(
            _mockCommandRunner.Object,
            _mockDataStore.Object,
            _mockLogger.Object);

        var expectedModels = new List<ModelInfo>
        {
            new ModelInfo { Id = "test-1", ProviderId = "anthropic", Name = "Test Model 1" }
        };

        _mockCommandRunner
            .Setup(r => r.GetModelsAsync())
            .ReturnsAsync(expectedModels);

        var result = await service.RefreshModelsAsync();

        Assert.Single(result);
        Assert.Equal("test-1", result[0].Id);
        _mockCommandRunner.Verify(r => r.GetModelsAsync(), Times.Once);
    }

    [Fact]
    public async Task AddFavoriteAsync_ShouldAddToDataStore()
    {
        var service = new OpenCodeModelsService(
            _mockCommandRunner.Object,
            _mockDataStore.Object,
            _mockLogger.Object);

        var modelId = "anthropic/claude-opus-4";

        await service.AddFavoriteAsync(modelId);

        _mockDataStore.Verify(d => d.AddFavoriteModelAsync(modelId), Times.Once);
    }

    [Fact]
    public async Task RemoveFavoriteAsync_ShouldRemoveFromDataStore()
    {
        var service = new OpenCodeModelsService(
            _mockCommandRunner.Object,
            _mockDataStore.Object,
            _mockLogger.Object);

        var modelId = "anthropic/claude-opus-4";

        await service.RemoveFavoriteAsync(modelId);

        _mockDataStore.Verify(d => d.RemoveFavoriteModelAsync(modelId), Times.Once);
    }

    [Fact]
    public async Task IsFavoriteAsync_ShouldCheckDataStore()
    {
        _mockDataStore.Setup(d => d.IsFavoriteModel("anthropic/claude-opus-4")).Returns(true);
        _mockDataStore.Setup(d => d.IsFavoriteModel("github-copilot/gpt-4")).Returns(false);

        var service = new OpenCodeModelsService(
            _mockCommandRunner.Object,
            _mockDataStore.Object,
            _mockLogger.Object);

        var isFavorite1 = await service.IsFavoriteAsync("anthropic/claude-opus-4");
        var isFavorite2 = await service.IsFavoriteAsync("github-copilot/gpt-4");

        Assert.True(isFavorite1);
        Assert.False(isFavorite2);
    }

    [Fact]
    public void GetModelsGroupedByProvider_ShouldGroupByProvider_WithFavoritesFirst()
    {
        var service = new OpenCodeModelsService(
            _mockCommandRunner.Object,
            _mockDataStore.Object,
            _mockLogger.Object);

        var models = new List<ModelInfo>
        {
            new ModelInfo { Id = "opus-4", ProviderId = "anthropic", Name = "Opus 4" },
            new ModelInfo { Id = "gpt-4", ProviderId = "github-copilot", Name = "GPT-4" },
            new ModelInfo { Id = "sonnet-4", ProviderId = "anthropic", Name = "Sonnet 4" }
        };

        var favoriteIds = new List<string> { "anthropic/opus-4" };

        var grouped = service.GetModelsGroupedByProvider(favoriteIds);

        var groupList = grouped.ToList();
        Assert.Equal(2, groupList.Count);

        var firstGroup = groupList[0];
        Assert.Equal("anthropic", firstGroup.Key);
        Assert.Contains(firstGroup, m => m.Id == "opus-4");
    }

    [Fact]
    public void GetModelsGroupedByProvider_ShouldFilterBySearchTerm()
    {
        var service = new OpenCodeModelsService(
            _mockCommandRunner.Object,
            _mockDataStore.Object,
            _mockLogger.Object);

        var models = new List<ModelInfo>
        {
            new ModelInfo { Id = "opus-4", ProviderId = "anthropic", Name = "Claude Opus 4" },
            new ModelInfo { Id = "gpt-4", ProviderId = "github-copilot", Name = "GPT-4" },
            new ModelInfo { Id = "sonnet-4", ProviderId = "anthropic", Name = "Claude Sonnet 4" }
        };

        var favoriteIds = new List<string>();

        var grouped = service.GetModelsGroupedByProvider(favoriteIds, searchTerm: "Opus");

        var groupList = grouped.ToList();
        Assert.Single(groupList);
        Assert.Single(groupList[0]);
        Assert.Equal("opus-4", groupList[0].First().Id);
    }

    [Fact]
    public void GetModelsGroupedByProvider_ShouldFilterByProvider()
    {
        var service = new OpenCodeModelsService(
            _mockCommandRunner.Object,
            _mockDataStore.Object,
            _mockLogger.Object);

        var models = new List<ModelInfo>
        {
            new ModelInfo { Id = "opus-4", ProviderId = "anthropic", Name = "Opus 4" },
            new ModelInfo { Id = "gpt-4", ProviderId = "github-copilot", Name = "GPT-4" },
            new ModelInfo { Id = "sonnet-4", ProviderId = "anthropic", Name = "Sonnet 4" }
        };

        var favoriteIds = new List<string>();

        var grouped = service.GetModelsGroupedByProvider(favoriteIds, providerFilter: "anthropic");

        var groupList = grouped.ToList();
        Assert.Single(groupList);
        Assert.Equal("anthropic", groupList[0].Key);
        Assert.Equal(2, groupList[0].Count());
    }
}
