using Microsoft.AspNetCore.SignalR;

namespace Homespun.Features.Notifications;

/// <summary>
/// SignalR hub for real-time notification delivery.
/// </summary>
public class NotificationHub(INotificationService notificationService) : Hub
{
    /// <summary>
    /// Join a project group to receive project-specific notifications.
    /// </summary>
    public async Task JoinProjectGroup(string projectId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"project-{projectId}");
    }

    /// <summary>
    /// Leave a project group.
    /// </summary>
    public async Task LeaveProjectGroup(string projectId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"project-{projectId}");
    }

    /// <summary>
    /// Get all active notifications for a project.
    /// </summary>
    public IReadOnlyList<Notification> GetActiveNotifications(string? projectId = null)
    {
        return notificationService.GetActiveNotifications(projectId);
    }

    /// <summary>
    /// Dismiss a notification.
    /// </summary>
    public async Task DismissNotification(string notificationId)
    {
        notificationService.DismissNotification(notificationId);
        await Clients.All.SendAsync("NotificationDismissed", notificationId);
    }
}

/// <summary>
/// Extension methods for broadcasting notifications via SignalR.
/// </summary>
public static class NotificationHubExtensions
{
    /// <summary>
    /// Broadcasts a new notification to all connected clients.
    /// </summary>
    public static async Task BroadcastNotificationAdded(
        this IHubContext<NotificationHub> hubContext,
        Notification notification)
    {
        // Send to all clients
        await hubContext.Clients.All.SendAsync("NotificationAdded", notification);

        // Also send to project-specific group if applicable
        if (!string.IsNullOrEmpty(notification.ProjectId))
        {
            await hubContext.Clients.Group($"project-{notification.ProjectId}")
                .SendAsync("NotificationAdded", notification);
        }
    }

    /// <summary>
    /// Broadcasts a notification dismissal to all connected clients.
    /// </summary>
    public static async Task BroadcastNotificationDismissed(
        this IHubContext<NotificationHub> hubContext,
        string notificationId)
    {
        await hubContext.Clients.All.SendAsync("NotificationDismissed", notificationId);
    }
}
