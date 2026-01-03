using Microsoft.EntityFrameworkCore;
using TreeAgent.Web.Features.Agents.Data;
using TreeAgent.Web.Features.Agents.Hubs;
using TreeAgent.Web.Features.PullRequests.Data;

namespace TreeAgent.Web.Features.Agents.Services;

public class AgentService : IDisposable
{
    private readonly TreeAgentDbContext _db;
    private readonly ClaudeCodeProcessManager _processManager;
    private readonly MessageParser _messageParser;
    private readonly IAgentHubNotifier _hubNotifier;
    private bool _disposed;

    public event Action<string, ParsedMessage>? OnMessageReceived;
    public event Action<string, AgentStatus>? OnStatusChanged;

    public AgentService(TreeAgentDbContext db, ClaudeCodeProcessManager processManager, IAgentHubNotifier hubNotifier)
    {
        _db = db;
        _processManager = processManager;
        _messageParser = new MessageParser();
        _hubNotifier = hubNotifier;

        _processManager.OnMessageReceived += HandleMessageReceived;
        _processManager.OnStatusChanged += HandleStatusChanged;
    }

    public async Task<Agent?> GetByIdAsync(string id)
    {
        return await _db.Agents
            .Include(a => a.Feature)
            .ThenInclude(f => f.Project)
            .FirstOrDefaultAsync(a => a.Id == id);
    }

    public async Task<List<Agent>> GetByFeatureIdAsync(string featureId)
    {
        return await _db.Agents
            .Where(a => a.FeatureId == featureId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<Agent>> GetAllActiveAsync()
    {
        return await _db.Agents
            .Include(a => a.Feature)
            .ThenInclude(f => f.Project)
            .Where(a => a.Status == AgentStatus.Running || a.Status == AgentStatus.Idle)
            .ToListAsync();
    }

    public async Task<Agent> CreateAsync(string featureId, string? systemPrompt = null)
    {
        var agent = new Agent
        {
            FeatureId = featureId,
            SystemPrompt = systemPrompt,
            Status = AgentStatus.Idle
        };

        _db.Agents.Add(agent);
        await _db.SaveChangesAsync();
        return agent;
    }

    public async Task<bool> StartAsync(string agentId)
    {
        var agent = await _db.Agents
            .Include(a => a.Feature)
            .ThenInclude(f => f.Project)
            .FirstOrDefaultAsync(a => a.Id == agentId);

        if (agent == null)
            return false;

        var workingDirectory = agent.Feature.WorktreePath ?? agent.Feature.Project.LocalPath;

        var success = await _processManager.StartAgentAsync(agentId, workingDirectory, agent.SystemPrompt);

        if (success)
        {
            agent.Status = AgentStatus.Running;
            agent.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        return success;
    }

    public async Task<bool> StopAsync(string agentId)
    {
        var success = await _processManager.StopAgentAsync(agentId);

        if (success)
        {
            var agent = await _db.Agents.FindAsync(agentId);
            if (agent != null)
            {
                agent.Status = AgentStatus.Stopped;
                agent.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }
        }

        return success;
    }

    public async Task<bool> SendMessageAsync(string agentId, string message)
    {
        var success = await _processManager.SendMessageAsync(agentId, message);

        if (success)
        {
            var dbMessage = new Message
            {
                AgentId = agentId,
                Role = "user",
                Content = message
            };

            _db.Messages.Add(dbMessage);
            await _db.SaveChangesAsync();
        }

        return success;
    }

    public AgentStatus GetStatus(string agentId)
    {
        return _processManager.GetAgentStatus(agentId);
    }

    public async Task<List<Message>> GetMessagesAsync(string agentId, int limit = 100)
    {
        return await _db.Messages
            .Where(m => m.AgentId == agentId)
            .OrderByDescending(m => m.Timestamp)
            .Take(limit)
            .OrderBy(m => m.Timestamp)
            .ToListAsync();
    }

    private async void HandleMessageReceived(string agentId, string rawMessage)
    {
        var parsed = _messageParser.Parse(rawMessage);
        if (parsed == null) return;

        try
        {
            var message = new Message
            {
                AgentId = agentId,
                Role = "assistant",
                Content = parsed.Content ?? rawMessage,
                Metadata = rawMessage
            };

            _db.Messages.Add(message);
            await _db.SaveChangesAsync();

            // Send real-time notification
            await _hubNotifier.SendMessageAsync(agentId, "assistant", parsed.Content ?? rawMessage, rawMessage);

            OnMessageReceived?.Invoke(agentId, parsed);
        }
        catch
        {
            // Log error but don't crash
        }
    }

    private async void HandleStatusChanged(string agentId, AgentStatus status)
    {
        try
        {
            var agent = await _db.Agents.FindAsync(agentId);
            if (agent != null)
            {
                agent.Status = status;
                agent.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }

            // Send real-time notification
            await _hubNotifier.SendStatusChangeAsync(agentId, status.ToString());

            OnStatusChanged?.Invoke(agentId, status);
        }
        catch
        {
            // Log error but don't crash
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _processManager.OnMessageReceived -= HandleMessageReceived;
        _processManager.OnStatusChanged -= HandleStatusChanged;
    }
}
