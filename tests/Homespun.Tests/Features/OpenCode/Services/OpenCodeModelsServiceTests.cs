using Homespun.Features.OpenCode.Data.Models;
using Homespun.Features.OpenCode.Services;
using Homespun.Features.PullRequests.Data;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.OpenCode.Services;

[TestFixture]
public class OpenCodeModelsServiceTests
{
    private Mock<IDataStore> _mockDataStore = null!;
    private Mock<IOpencodeCommandRunner> _mockCommandRunner = null!;
    private Mock<ILogger<OpenCodeModelsService>> _mockLogger = null!;

    [SetUp]
    public void SetUp()
    {
        _mockDataStore = new Mock<IDataStore>();
        _mockCommandRunner = new Mock<IOpencodeCommandRunner>();
        _mockLogger = new Mock<ILogger<OpenCodeModelsService>>();
    }

    [Test]
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

        Assert.That(result1.Count, Is.EqualTo(2));
        Assert.That(result2, Is.SameAs(result1));
        _mockCommandRunner.Verify(r => r.GetModelsAsync(), Times.Once);
    }

    [Test]
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

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Id, Is.EqualTo("test-1"));
        _mockCommandRunner.Verify(r => r.GetModelsAsync(), Times.Once);
    }

    [Test]
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

    [Test]
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

    [Test]
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

        Assert.That(isFavorite1, Is.True);
        Assert.That(isFavorite2, Is.False);
    }

    [Test]
    public async Task GetModelsGroupedByProvider_ShouldGroupByProvider_WithFavoritesFirst()
    {
        var models = new List<ModelInfo>
        {
            new ModelInfo { Id = "opus-4", ProviderId = "anthropic", Name = "Opus 4" },
            new ModelInfo { Id = "gpt-4", ProviderId = "github-copilot", Name = "GPT-4" },
            new ModelInfo { Id = "sonnet-4", ProviderId = "anthropic", Name = "Sonnet 4" }
        };

        _mockCommandRunner
            .Setup(r => r.GetModelsAsync())
            .ReturnsAsync(models);

        var service = new OpenCodeModelsService(
            _mockCommandRunner.Object,
            _mockDataStore.Object,
            _mockLogger.Object);

        // Load models into the service first
        await service.GetModelsAsync();

        var favoriteIds = new List<string> { "anthropic/opus-4" };
        var grouped = service.GetModelsGroupedByProvider(favoriteIds);

        var groupList = grouped.ToList();
        Assert.That(groupList, Has.Count.EqualTo(2));

        var firstGroup = groupList[0];
        Assert.That(firstGroup.Key, Is.EqualTo("anthropic"));
        Assert.That(firstGroup.Any(m => m.Id == "opus-4"), Is.True);
    }

    [Test]
    public async Task GetModelsGroupedByProvider_ShouldFilterBySearchTerm()
    {
        var models = new List<ModelInfo>
        {
            new ModelInfo { Id = "opus-4", ProviderId = "anthropic", Name = "Claude Opus 4" },
            new ModelInfo { Id = "gpt-4", ProviderId = "github-copilot", Name = "GPT-4" },
            new ModelInfo { Id = "sonnet-4", ProviderId = "anthropic", Name = "Claude Sonnet 4" }
        };

        _mockCommandRunner
            .Setup(r => r.GetModelsAsync())
            .ReturnsAsync(models);

        var service = new OpenCodeModelsService(
            _mockCommandRunner.Object,
            _mockDataStore.Object,
            _mockLogger.Object);

        // Load models into the service first
        await service.GetModelsAsync();

        var favoriteIds = new List<string>();
        var grouped = service.GetModelsGroupedByProvider(favoriteIds, searchTerm: "Opus");

        var groupList = grouped.ToList();
        Assert.That(groupList, Has.Count.EqualTo(1));
        Assert.That(groupList[0].Count(), Is.EqualTo(1));
        Assert.That(groupList[0].First().Id, Is.EqualTo("opus-4"));
    }

    [Test]
    public async Task GetModelsGroupedByProvider_ShouldFilterByProvider()
    {
        var models = new List<ModelInfo>
        {
            new ModelInfo { Id = "opus-4", ProviderId = "anthropic", Name = "Opus 4" },
            new ModelInfo { Id = "gpt-4", ProviderId = "github-copilot", Name = "GPT-4" },
            new ModelInfo { Id = "sonnet-4", ProviderId = "anthropic", Name = "Sonnet 4" }
        };

        _mockCommandRunner
            .Setup(r => r.GetModelsAsync())
            .ReturnsAsync(models);

        var service = new OpenCodeModelsService(
            _mockCommandRunner.Object,
            _mockDataStore.Object,
            _mockLogger.Object);

        // Load models into the service first
        await service.GetModelsAsync();

        var favoriteIds = new List<string>();
        var grouped = service.GetModelsGroupedByProvider(favoriteIds, providerFilter: "anthropic");

        var groupList = grouped.ToList();
        Assert.That(groupList, Has.Count.EqualTo(1));
        Assert.That(groupList[0].Key, Is.EqualTo("anthropic"));
        Assert.That(groupList[0].Count(), Is.EqualTo(2));
    }
}
