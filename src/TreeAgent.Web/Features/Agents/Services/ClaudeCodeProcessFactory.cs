namespace TreeAgent.Web.Features.Agents.Services;

public class ClaudeCodeProcessFactory(string? claudeCodePath = null) : IClaudeCodeProcessFactory
{
    private readonly string _claudeCodePath = claudeCodePath ?? new ClaudeCodePathResolver().Resolve();

    public IClaudeCodeProcess Create(string agentId, string workingDirectory, string? systemPrompt = null)
    {
        return new ClaudeCodeProcess(agentId, _claudeCodePath, workingDirectory, systemPrompt);
    }
}