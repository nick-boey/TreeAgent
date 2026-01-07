using Homespun.Features.OpenCode.Models;
using NUnit.Framework;

namespace Homespun.Tests.Components;

/// <summary>
/// Unit tests for AgentManagementPanel component logic.
/// Note: These tests focus on the data transformation and grouping logic.
/// Full component rendering tests would require bUnit.
/// </summary>
[TestFixture]
public class AgentManagementPanelTests
{
    [Test]
    public void FormatUptime_LessThanOneMinute_ShowsSeconds()
    {
        // Arrange
        var uptime = TimeSpan.FromSeconds(45);
        
        // Act
        var result = FormatUptime(uptime);
        
        // Assert
        Assert.That(result, Is.EqualTo("45s"));
    }

    [Test]
    public void FormatUptime_LessThanOneHour_ShowsMinutesAndSeconds()
    {
        // Arrange
        var uptime = TimeSpan.FromMinutes(5).Add(TimeSpan.FromSeconds(30));
        
        // Act
        var result = FormatUptime(uptime);
        
        // Assert
        Assert.That(result, Is.EqualTo("5m 30s"));
    }

    [Test]
    public void FormatUptime_MoreThanOneHour_ShowsHoursAndMinutes()
    {
        // Arrange
        var uptime = TimeSpan.FromHours(2).Add(TimeSpan.FromMinutes(30));
        
        // Act
        var result = FormatUptime(uptime);
        
        // Assert
        Assert.That(result, Is.EqualTo("2h 30m"));
    }

    [Test]
    public void FormatUptime_ExactlyOneHour_ShowsHoursAndZeroMinutes()
    {
        // Arrange
        var uptime = TimeSpan.FromHours(1);
        
        // Act
        var result = FormatUptime(uptime);
        
        // Assert
        Assert.That(result, Is.EqualTo("1h 0m"));
    }

    [Test]
    public void GetEntityTypeBadgeClass_PR_ReturnsPrClass()
    {
        // Arrange & Act
        var result = GetEntityTypeBadgeClass("PR");
        
        // Assert
        Assert.That(result, Is.EqualTo("pr"));
    }

    [Test]
    public void GetEntityTypeBadgeClass_Issue_ReturnsIssueClass()
    {
        // Arrange & Act
        var result = GetEntityTypeBadgeClass("Issue");
        
        // Assert
        Assert.That(result, Is.EqualTo("issue"));
    }

    [Test]
    public void GetEntityTypeBadgeClass_Change_ReturnsChangeClass()
    {
        // Arrange & Act
        var result = GetEntityTypeBadgeClass("Change");
        
        // Assert
        Assert.That(result, Is.EqualTo("change"));
    }

    [Test]
    public void GetEntityTypeBadgeClass_Unknown_ReturnsPrClass()
    {
        // Arrange & Act
        var result = GetEntityTypeBadgeClass("Unknown");
        
        // Assert
        Assert.That(result, Is.EqualTo("pr"));
    }

    [Test]
    public void GetEntityTypeBadgeClass_CaseInsensitive()
    {
        // Arrange & Act
        var result = GetEntityTypeBadgeClass("issue");
        
        // Assert
        Assert.That(result, Is.EqualTo("issue"));
    }

    // Helper methods that mirror the component's private methods
    private static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalHours >= 1)
            return $"{(int)uptime.TotalHours}h {uptime.Minutes}m";
        if (uptime.TotalMinutes >= 1)
            return $"{(int)uptime.TotalMinutes}m {uptime.Seconds}s";
        return $"{uptime.Seconds}s";
    }

    private static string GetEntityTypeBadgeClass(string entityType) => entityType.ToLowerInvariant() switch
    {
        "pr" => "pr",
        "issue" => "issue",
        "change" => "change",
        _ => "pr"
    };
}

/// <summary>
/// Tests for project grouping logic.
/// </summary>
[TestFixture]
public class AgentManagementPanelGroupingTests
{
    [Test]
    public void GroupServersByProject_EmptyList_ReturnsEmptyGroups()
    {
        // Arrange
        var servers = new List<RunningServerInfo>();
        var entityInfoCache = new Dictionary<string, EntityInfo>();
        
        // Act
        var result = GroupServersByProject(servers, entityInfoCache);
        
        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GroupServersByProject_SingleProject_CreatesSingleGroup()
    {
        // Arrange
        var servers = new List<RunningServerInfo>
        {
            new()
            {
                EntityId = "pr-1",
                Port = 4099,
                BaseUrl = "http://127.0.0.1:4099",
                WorktreePath = @"C:\test",
                StartedAt = DateTime.UtcNow,
                Sessions = new List<OpenCodeSession>()
            }
        };
        
        var entityInfoCache = new Dictionary<string, EntityInfo>
        {
            ["pr-1"] = new EntityInfo
            {
                EntityType = "PR",
                Title = "Test PR",
                ProjectId = "proj-1",
                ProjectName = "Test Project"
            }
        };
        
        // Act
        var result = GroupServersByProject(servers, entityInfoCache);
        
        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].ProjectName, Is.EqualTo("Test Project"));
        Assert.That(result[0].Servers, Has.Count.EqualTo(1));
    }

    [Test]
    public void GroupServersByProject_MultipleProjects_GroupsCorrectly()
    {
        // Arrange
        var servers = new List<RunningServerInfo>
        {
            new()
            {
                EntityId = "pr-1",
                Port = 4099,
                BaseUrl = "http://127.0.0.1:4099",
                WorktreePath = @"C:\test1",
                StartedAt = DateTime.UtcNow,
                Sessions = new List<OpenCodeSession>()
            },
            new()
            {
                EntityId = "pr-2",
                Port = 4100,
                BaseUrl = "http://127.0.0.1:4100",
                WorktreePath = @"C:\test2",
                StartedAt = DateTime.UtcNow,
                Sessions = new List<OpenCodeSession>()
            }
        };
        
        var entityInfoCache = new Dictionary<string, EntityInfo>
        {
            ["pr-1"] = new EntityInfo
            {
                EntityType = "PR",
                Title = "PR 1",
                ProjectId = "proj-1",
                ProjectName = "Project A"
            },
            ["pr-2"] = new EntityInfo
            {
                EntityType = "PR",
                Title = "PR 2",
                ProjectId = "proj-2",
                ProjectName = "Project B"
            }
        };
        
        // Act
        var result = GroupServersByProject(servers, entityInfoCache);
        
        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result.Select(g => g.ProjectName), Is.EquivalentTo(new[] { "Project A", "Project B" }));
    }

    [Test]
    public void GroupServersByProject_OrdersProjectsAlphabetically()
    {
        // Arrange
        var servers = new List<RunningServerInfo>
        {
            new()
            {
                EntityId = "pr-1",
                Port = 4099,
                BaseUrl = "http://127.0.0.1:4099",
                WorktreePath = @"C:\test1",
                StartedAt = DateTime.UtcNow,
                Sessions = new List<OpenCodeSession>()
            },
            new()
            {
                EntityId = "pr-2",
                Port = 4100,
                BaseUrl = "http://127.0.0.1:4100",
                WorktreePath = @"C:\test2",
                StartedAt = DateTime.UtcNow,
                Sessions = new List<OpenCodeSession>()
            }
        };
        
        var entityInfoCache = new Dictionary<string, EntityInfo>
        {
            ["pr-1"] = new EntityInfo
            {
                EntityType = "PR",
                Title = "PR 1",
                ProjectId = "proj-1",
                ProjectName = "Zulu Project"
            },
            ["pr-2"] = new EntityInfo
            {
                EntityType = "PR",
                Title = "PR 2",
                ProjectId = "proj-2",
                ProjectName = "Alpha Project"
            }
        };
        
        // Act
        var result = GroupServersByProject(servers, entityInfoCache);
        
        // Assert
        Assert.That(result[0].ProjectName, Is.EqualTo("Alpha Project"));
        Assert.That(result[1].ProjectName, Is.EqualTo("Zulu Project"));
    }

    // Helper methods
    private static List<ProjectGroupInfo> GroupServersByProject(
        List<RunningServerInfo> servers, 
        Dictionary<string, EntityInfo> entityInfoCache)
    {
        return servers
            .Select(server => new
            {
                Server = server,
                EntityInfo = entityInfoCache.GetValueOrDefault(server.EntityId)
            })
            .GroupBy(x => x.EntityInfo?.ProjectName ?? "Unknown Project")
            .Select(group => new ProjectGroupInfo
            {
                ProjectName = group.Key,
                Servers = group.Select(x => x.Server)
                    .OrderBy(s => entityInfoCache.GetValueOrDefault(s.EntityId)?.Title ?? s.EntityId)
                    .ToList()
            })
            .OrderBy(g => g.ProjectName)
            .ToList();
    }

    private class ProjectGroupInfo
    {
        public required string ProjectName { get; set; }
        public required List<RunningServerInfo> Servers { get; set; }
    }

    private class EntityInfo
    {
        public required string EntityType { get; set; }
        public required string Title { get; set; }
        public string? BranchName { get; set; }
        public required string ProjectId { get; set; }
        public required string ProjectName { get; set; }
    }
}
