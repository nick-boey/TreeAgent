using System.Collections.Concurrent;

namespace Homespun.Features.Notifications;

/// <summary>
/// Thread-safe service for managing application notifications.
/// Registered as a singleton to persist notifications across requests.
/// </summary>
public class NotificationService(ILogger<NotificationService> logger) : INotificationService
{
    private readonly ConcurrentDictionary<string, Notification> _notifications = new();

    public event Action<Notification>? OnNotificationAdded;
    public event Action<string>? OnNotificationDismissed;

    public void AddNotification(Notification notification)
    {
        // Handle deduplication
        if (!string.IsNullOrEmpty(notification.DeduplicationKey))
        {
            // Remove any existing notification with the same key
            var existingNotification = _notifications.Values
                .FirstOrDefault(n => n.DeduplicationKey == notification.DeduplicationKey);
            
            if (existingNotification != null)
            {
                _notifications.TryRemove(existingNotification.Id, out _);
                logger.LogDebug(
                    "Replaced existing notification with deduplication key {Key}",
                    notification.DeduplicationKey);
            }
        }

        if (_notifications.TryAdd(notification.Id, notification))
        {
            logger.LogInformation(
                "Added notification {NotificationId}: {Title}",
                notification.Id, notification.Title);
            
            OnNotificationAdded?.Invoke(notification);
        }
    }

    public void DismissNotification(string notificationId)
    {
        if (_notifications.TryRemove(notificationId, out var notification))
        {
            logger.LogInformation(
                "Dismissed notification {NotificationId}: {Title}",
                notificationId, notification.Title);
            
            OnNotificationDismissed?.Invoke(notificationId);
        }
    }

    public void DismissNotificationsByKey(string deduplicationKey)
    {
        var toRemove = _notifications.Values
            .Where(n => n.DeduplicationKey == deduplicationKey)
            .ToList();

        foreach (var notification in toRemove)
        {
            DismissNotification(notification.Id);
        }
    }

    public IReadOnlyList<Notification> GetActiveNotifications(string? projectId = null)
    {
        var notifications = _notifications.Values.AsEnumerable();

        if (projectId != null)
        {
            // Return global notifications (no project) and project-specific ones
            notifications = notifications.Where(n => n.ProjectId == null || n.ProjectId == projectId);
        }

        return notifications
            .OrderByDescending(n => n.CreatedAt)
            .ToList();
    }

    public bool HasNotificationWithKey(string deduplicationKey)
    {
        return _notifications.Values.Any(n => n.DeduplicationKey == deduplicationKey);
    }
}
