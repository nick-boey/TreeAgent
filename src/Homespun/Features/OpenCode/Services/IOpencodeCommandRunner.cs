using Homespun.Features.OpenCode.Data.Models;

namespace Homespun.Features.OpenCode.Services;

public interface IOpencodeCommandRunner
{
    Task<IReadOnlyList<ModelInfo>> GetModelsAsync();
}
