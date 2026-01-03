namespace TreeAgent.Web.Features.Agents.Services;

public interface IClaudeCodeProcessFactory
{
    IClaudeCodeProcess Create(string agentId, string workingDirectory, string? systemPrompt = null);
}